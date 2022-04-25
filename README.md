# Space Ring Things - Godot Edition
This repository holds both the game server and the game client for a Godot-based
multiplayer game called "Space Ring Things".

## Prerequisites
The game is currently being built with Linux and using Godot 3.4.2 with Mono.

For debugging and development purposes you will want to be running an Artemis
AMQP broker locally. In order to get a specific IP for a local container running
Artemis, you will need to do this as the `root` system user. Podman or Docker 
will both work, although the Docker syntax may be slightly different.

```
podman run --name artemis -it -p 8161:8161 -p 5672:5672 --ip 10.88.0.2 -e AMQ_USER=admin -e AMQ_PASSWORD=admin -e AMQ_ALLOW_ANONYMOUS=true quay.io/artemiscloud/activemq-artemis-broker:latest
```

The above command will run an Artemis container and bind it to the Podman
network on the IP address 10.88.0.2. It will expose ports 8161 (the management
console) as well as 5672 (the AMQP port). 

## Compiling Protobuf
After making changes to the protocol buffer definitions, they need to be
compiled to C# code. You will need the `dotnet` command line tool (or
equivalent) in order to do this.

Intstall the Protogen tooling:
```
dotnet tool install --global protobuf-net.Protogen --version 3.0.101
```

Then, in the `proto` folder:

```
protogen --csharp_out=. *.proto
```
## Ship size
Roughly 20m long x 10m wide to start
Currently ~64px at server view

## Play area
10km circle to start (500x ship length)
Currently 32,000px across at server view
