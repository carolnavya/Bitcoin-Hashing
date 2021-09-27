printfn "HELLO! I AM THE SERVER!!!"

printfn "Downloading dependencies..."
#r "nuget: Akka.FSharp, 1.4.25"
#r "nuget: Akka"
#r "nuget: FSharp.Data,4.1.1"
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"
printfn "Completed downloading required dependencies"

#time "on" 
open System
open System.Diagnostics
open System.Security.Cryptography
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open Akka.TestKit

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 9000
                    hostname = 172.16.103.194
                }
            }
        }")
// initialize client system
let serverSystem = ActorSystem.Create("BitcoinAtServer",configuration)

let mutable clientWorkerCount = 0UL
let mutable clientWorkers =[]
let mutable nZeros = 0UL
// taking Server leading number of zeros as input
match fsi.CommandLineArgs with
    | [|_; n|] -> 
        nZeros <- uint64(n)
    | _ -> 
        printfn "Error: Invalid Arguments. Alteast pass the number of leading zeros argument."
        Environment.Exit 1

// Variable to store output
type WorkerOutput = {
    Coin: string;
    Hash: string;
}

// Cliend info to be sent to the server
type ClientInfo = {
    ClientID : string;
    ServerIP : string;
    TotalClientActors: uint64;
    ActorRefs: List<IActorRef>;
    ClientWorkSize: uint64;
}

// Information necessary to perform action by Actors
type TaskInfo = {
    MaxStrings: uint64;
    ZeroCount: uint64;
    Prefix: string;
    WorkSize: uint64;
    WorkerCount:uint64;
    ClientActors: List<IActorRef>;
}

//get information required for workers 
let getInput = 
    let pCount = System.Environment.ProcessorCount |> uint64
    let details:TaskInfo = {
        MaxStrings = uint64(100000000);
        ZeroCount = nZeros;
        Prefix = "cpagolu";
        WorkSize = uint64(100000000)/uint64(pCount);
        WorkerCount = pCount;
        ClientActors = [];
    }
    details

//concat string with prefix
let concatstr suffix =
    "cpagolu" + ";" + suffix
    
//Generate random String 
let randomStrGenerator(strlen : uint64) =
    let randstr = System.Random()
    let mutable str = ""
    let chars = "abcdefghijklmnopqrstuvwxyz0123456789"
    let mutable count = 0
    for i in 1UL .. strlen do
      count <- (randstr.Next() % 36)
      str <- String.concat "" [str; chars.[count].ToString()]
    str

// SHA256 hashing
let SHAEncrypter (coin: string) = 
    System.Text.Encoding.ASCII.GetBytes coin
    |> (new SHA256Managed()).ComputeHash
    |>System.BitConverter.ToString
    |> fun x -> x.Replace ("-", "")

// generate the prefix for given number of zeros ex for n=2 "00"
let leadingZeroPrefix n = 
  (System.Text.StringBuilder(), {1UL..n})
  ||> Seq.fold(fun b _ -> b.Append("0"))|> sprintf "%O"

// compare hash to check the leading number of zeros reside in the prefix
let compareTo (hashed:string, z:uint64)=
  let prefix = leadingZeroPrefix z
  match hashed with
  | hashed when hashed.StartsWith(prefix)-> true
  | _ -> false

// Server Worker
type ServerWorker(name) =
    inherit Actor()
    override x.OnReceive(message:obj) = 
        let sender = x.Sender
        match message with
        | :? TaskInfo as input -> // Server workers in action
            let mutable coin = "abc"
            for i in [0UL..input.WorkSize-1UL] do
                let randnum = (System.Random()).Next(1, 36) |> uint64 //generate random number
                coin <- randomStrGenerator randnum |> concatstr  //generate coin
                let mutable hashVal = SHAEncrypter coin // hashing coin
                if compareTo(hashVal,input.ZeroCount) then // checking for leading number of zeros
                        let mutable answer:WorkerOutput = {
                            Coin = coin;
                            Hash = hashVal;
                        }
                        // sending mined coins to the Server Supervisor
                        sender<! answer
            sender <! "Done"
        | _ -> //exception handling
                let failureMessage = name + " Failed!"
                failwith failureMessage

type ServerSupervisor (name) =  
    inherit Actor()
    let mutable completedWorkSize = 0UL
    let mutable nWorkers = 0UL
    let mutable parent: IActorRef = null

    override x.OnReceive(message: obj)=
        let parent = x.Sender
        //printfn "Matching meesage..."
        match message with
        | :? TaskInfo as input -> // Server Actors in action
            nWorkers <- input.WorkerCount
            let nWorkers = input.WorkerCount-1UL
            let workers = [0UL..nWorkers] 
                          |> List.map(fun workerID -> let properties = [|"Serverworker"+(workerID|>string) :> obj|]
                                                      serverSystem.ActorOf(Props(typedefof<ServerWorker>, properties)))
            let mutable i = 0
            for idx in [0UL..nWorkers] do
                i <- int(idx)
                workers |> List.item i <! input
        | :? ClientInfo as input -> // Handling Client Actors
            printfn "Received the client input"
            let nClients = input.TotalClientActors
            let workersOfClient = input.ActorRefs
            clientWorkers <- clientWorkers |> List.append workersOfClient
            let currentWorkerCount = clientWorkerCount
            clientWorkerCount <- nClients + (nClients*input.ClientWorkSize)
            let jobInfo = getInput
            let workInput: TaskInfo = {
                MaxStrings = jobInfo.MaxStrings;
                ZeroCount = jobInfo.ZeroCount;
                Prefix = jobInfo.Prefix;
                WorkSize = jobInfo.MaxStrings/nClients
                WorkerCount = nClients;
                ClientActors = workersOfClient;
            }
            let mutable i =0
            // giving out work to the Client Workers
            for idx in [0UL..nClients] do
                i<- int(idx)
                clientWorkers |> List.item i <! workInput
        | :? WorkerOutput as solution -> //printing mined coins
            printfn "%s\t%s\n" solution.Coin solution.Hash 
            parent <! "Done"
        | :? string -> // terminating the completed worker and stopping it's message queue
            parent <! PoisonPill.Instance
            completedWorkSize <- completedWorkSize+1UL
            if completedWorkSize = nWorkers then
                parent <! "Done"
        | _ -> //exception handling
            let failureMessage = name + " Failed!"
            failwith failureMessage         


let main(nZeros : uint64) = 
    
    //initialize Server Supervisor
    let serverSupervisor = serverSystem.ActorOf(Props(typedefof<ServerSupervisor>, [| "serverSupervisor" :> obj |]))

    let out :WorkerOutput = { Coin = null; Hash = null;}
    
    // get info required for the tasks to run
    let info: TaskInfo = getInput
   
    // sending task info to supervisor
    let task = serverSupervisor <? info
    // performing the task asynchronously
    Async.RunSynchronously(task) |> ignore
    // terminate supervisor and stop the message queue
    serverSupervisor <! PoisonPill.Instance
    serverSystem.Terminate() |> ignore
    serverSystem.WhenTerminated.Wait(100) |> ignore
    // Exit program
    printfn "\nPress Enter to exit"
    System.Console.ReadLine() |> ignore
    0


main(nZeros)
