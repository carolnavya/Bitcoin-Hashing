printfn "HELLO! I AM THE CLIENT SERVER!!!"

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
// configuration of the Client System
let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                
            }
            remote {
                helios.tcp {
                    port = 9000
                    hostname = localhost
                }
            }
        }")

// initialize client system
let clientSystem = ActorSystem.Create("BitcoinAtClient",configuration)

// taking Server IP Address as input
let mutable ipAdd = ""
match fsi.CommandLineArgs with
    | [|_; ip|] -> 
        ipAdd <- ip
        printfn ""
    | _ -> 
        printfn "Error: Invalid Arguments. Alteast pass the IP Address of the server."
        Environment.Exit 1
        
// initialize connection to Server
let serverEcho = clientSystem.ActorSelection("akka.tcp://RemoteBitcoinAtClient@{0}:9000/user/server".Replace("{0}",ipAdd))

// mined output information
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

// Client Worker
type Worker(name) =
    inherit Actor()
    override x.OnReceive(message:obj) = 
        let sender = x.Sender
        match message with
        | :? TaskInfo as input -> // Client worker in action
            let mutable coin = "abc"
            for i in [0UL..input.WorkSize-1UL] do
                let randnum = (System.Random()).Next(1, 36) |> uint64 //generate random number
                coin <- randomStrGenerator randnum |> concatstr //generate coin
                let mutable hashVal = SHAEncrypter coin // hashing coin
                if compareTo(hashVal,input.ZeroCount) then // checking for leading number of zeros
                        let mutable answer:WorkerOutput = {
                            Coin = coin;
                            Hash = hashVal;
                        }
                         // sending the mined result to the Supervisor actor
                        sender<! answer
            sender <! "Done"
        | _ -> //exception handling
                let failureMessage = name + " Failed to pass message to the server!"
                failwith failureMessage

// Client Supervisor
type Supervisor (name) =  
    inherit Actor()
    override x.OnReceive(message: obj)=
        let sender = x.Sender
        match message with
        | :? ClientInfo as input -> 
            printfn "Sending Client info to Server"
            serverEcho.Tell(input)
        | :? WorkerOutput as solution ->
            printfn "Successfully found a bitcoin. Sending it to Server"
            printfn "%s\t%s\n" solution.Coin solution.Hash 
            serverEcho.Tell(solution)
        | _ -> //exception handling
            let failureMessage = name + " Failed to pass message to the server supervisor!"
            failwith failureMessage 

let main(ipAdd:string) =
    let pCount = System.Environment.ProcessorCount |> uint64 //attaining processor count
    let mutable nActors = pCount*1UL //type inference
    //initialize Client supervisor actor
    let supervisor = clientSystem.ActorOf(Props(typedefof<Supervisor>, [| "supervisor" :> obj |]))
    //initialize Client Actors
    let mutable clientWorkers = [0UL..nActors-1UL] 
                                |> List.map(fun workerID -> let properties = [|"clientWorker"+(workerID|>string) :> obj|]
                                                            clientSystem.ActorOf(Props(typedefof<Worker>, properties)))
    
    
    //initialize client info
    let info: ClientInfo = {
        ClientID = "client1";
        ServerIP = ipAdd;
        TotalClientActors = nActors;
        ActorRefs = clientWorkers;
        ClientWorkSize = 0UL;
    }
    // sending task info to supervisor
    let task = supervisor <? info
    // performing the task asynchronously
    Async.RunSynchronously(task) |> ignore
    // terminate supervisor and stop the message queue
    supervisor <! PoisonPill.Instance
    clientSystem.Terminate() |> ignore
    clientSystem.WhenTerminated.Wait(100) |> ignore
    // Exit program
    printfn "\nPress Enter to exit"
    System.Console.ReadLine() |> ignore
    0

main(ipAdd)