# Bitcoin-Hashing
Mining bitcoins by generating random input strings and hashing them using the SHA256 algorithm. These mined coins are checked to ensure they have the required number of leading zeros in their prefix specified by the user. F# and AKKA actor model is used to build a good solution that runs efficiently on multi-core machines.

**PROBLEM STATEMENT**

Bitcoins are one of the most popular cryptocurrencies that are used commonly. The Bitcoins utilize the hardness of cryptographic hashing to ensure only a limited supply of coins are available. The aim of this project is to mine coins by generating random input strings and hashing them using the SHA256 algorithm. These mined coins are checked to ensure they have the required number of leading zeros in their prefix specified by the user. For this purpose, we use F# and an actor model to build a good solution that runs efficiently on multi-core machines.

**SOLUTION**

AKKA.net and f# are being used to implement this project. The following models were built keeping the problem requirements in mind.

**1. SINGLE MACHINE IMPLEMENTATION**

- In this implementation, all the actors are spawned in the same machine. The program takes the number of leading zeros as input from the command line and spawns the SUPERVISOR actor.

- The SUPERVISOR initializes and supervises a pool of WORKER actors. The task of the

- WORKER actors are to generate random strings, hash them using SHA256, and verify if the hash has the specified leading zeros in the prefix.

- If the string hash contains the specified number of zeros, the WORKER sends the resulting hash and the coin to the SUPERVISOR who displays the result to the user.

TO RUN THIS MODEL:

```
cd single_machine/ dotnet fsi --langversion:preview AkkaSingleMachine.fsx <number of leading zeros>
```

**2. DISTRIBUTED IMPLEMENTATION**

- In this implementation, there exists a server and several clients.
- The server takes the number of leading zeros as input from the command line and spawns a SUPERVISOR actor.
- The clients take the IP Address of the host and initialize WORKER actors of their ownwho participate in the mining process.
- The SUPERVISOR actor initializes and manages its own pool of WORKER actors. In addition to this, it also handles WORKERS initiated by the Clients.
- The Server’s SUPERVISOR can run independently if there are no Clients available

TO RUN THIS MODEL:
```
cd distributed\_machine/ dotnet fsi --langversion:preview RemoteServer.fsx <number of leading zeros>

cd distributed\_machine/ dotnet fsi --langversion:preview RemoteClient.fsx <server IP address>

Press ctrl+Z to stop the mining process
```

**NOTE:**

*Please make sure you change the hostname to the IP address of your server in RemoteServer.fsx (line 36).*

*Always run the server before the clients.*
