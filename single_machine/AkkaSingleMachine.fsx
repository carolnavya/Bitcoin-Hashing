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
// configuration of the System
let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : DEBUG
            loglevel : ERROR
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
                    hostname = localhost
                }
            }
        }")

// initialize system
let bitcoinSystem = ActorSystem.Create("BitcoinHashing",configuration)

// Variable to store output
type WorkerOutput = {
    Coin: string;
    Hash: string;
}

// variable to store job information that will be required for workers 
type TaskInfo = {
    MaxStrings: uint64;
    ZeroCount: uint64;
    Prefix: string;
    WorkSize: uint64;
    WorkerCount:uint64;

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

type Worker(name) =
    inherit Actor()
    override x.OnReceive(message:obj) = 
        let sender = x.Sender

        match message with
        | :? TaskInfo as input ->
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
                        // sending the mined result to the Supervisor actor
                        sender<! answer
            sender <! "Done"
        | _ -> //exception handling
                let failureMessage = name + " Failed!"
                failwith failureMessage

type Supervisor (name) =  
    inherit Actor()
    let mutable completedWorkSize = 0UL
    let mutable nWorkers = 0UL
    let mutable parent: IActorRef = null

    override x.OnReceive(message: obj)=
        let sender = x.Sender

        match message with
        | :? TaskInfo as input -> //initializing worker actors
            nWorkers <- input.WorkerCount
            parent <- sender
            let workers = [0UL..input.WorkerCount-1UL] 
                          |> List.map(fun workerID -> let properties = [|"worker"+(workerID|>string) :> obj|]
                                                      bitcoinSystem.ActorOf(Props(typedefof<Worker>, properties)))
            let mutable i = 0
            for idx in [0UL..nWorkers-1UL] do
                i <- int(idx)
                workers |> List.item i <! input

        | :? WorkerOutput as solution -> //printing mined coins
            printfn "%s\t%s\n" solution.Coin solution.Hash 
            parent <! "Done"
        | :? string -> // terminating the completed worker and stopping it's message queue
            sender <! PoisonPill.Instance
            completedWorkSize <- completedWorkSize+1UL
            if completedWorkSize = nWorkers then //
                parent <! "Done"
        | _ -> //exception handling
            let failureMessage = name + " Failed!"
            failwith failureMessage         


let main(nZeros:uint64) =
    let nStrings = uint64(100000000)
    let pCount = System.Environment.ProcessorCount |> uint64 //attaining processor count
    let mutable nActors = pCount*1UL //type inference
    let mutable runRemote = false //Check if process should run remotely or not
    let workSize = uint64(nStrings)/uint64(nActors) //amount of work allocated to each worker
    
    //initialize supervisor actor
    let supervisor = bitcoinSystem.ActorOf(Props(typedefof<Supervisor>, [| "supervisor" :> obj |]))

    let out :WorkerOutput = { Coin = null; Hash = null;}
    
    let info: TaskInfo = {
        MaxStrings = nStrings;
        ZeroCount = nZeros;
        Prefix = "cpagolu";
        WorkSize = workSize;
        WorkerCount = nActors;
    }
    // start timing the program
    let proc = Process.GetCurrentProcess()
    let cpuTimeStamp = proc.TotalProcessorTime
    let timer = Stopwatch()
    timer.Start()
    // sending task info to supervisor
    let task = supervisor <? info
    // performing the task asynchronously
    Async.RunSynchronously(task) |> ignore
    // terminate supervisor and stop the message queue
    supervisor <! PoisonPill.Instance
    bitcoinSystem.Terminate() |> ignore
    bitcoinSystem.WhenTerminated.Wait(100) |> ignore
    //time analysis
    let cpuTime = (proc.TotalProcessorTime-cpuTimeStamp).TotalMilliseconds
    printfn "CPU time = %d ms" (int64 cpuTime)
    printfn "Real time = %d ms" timer.ElapsedMilliseconds
    printfn "CPU/RealTime ratio %f" (cpuTime/ float timer.ElapsedMilliseconds)
   //Exit program
    printfn "\nPress Enter to exit"
    System.Console.ReadLine() |> ignore
    0

// taking command line arguments
match fsi.CommandLineArgs with
    | [|_; n|] -> 
        let nZeros = uint64(n)
        main(nZeros)
    | _ -> 
        printfn "Error: Invalid Arguments. Alteast pass the number of leading zeros argument."
        0