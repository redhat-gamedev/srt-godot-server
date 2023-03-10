# Space Ring Things - Godot Edition
This repository is the Godot-based muliplayer server for the "Space Ring Things"
game. For the game client, you will need to look at the [client
repository](https://github.com/redhat-gamedev/srt-godot-client). The game also
uses Protobufs for messaging, and makes use of Git submodules in order to pull
in the [Protobuf descriptions](https://github.com/redhat-gamedev/srt-protobufs).

## Prerequisites
The game is currently being built with Linux (Fedora) and using Godot 3.5.1 with
Mono.

For debugging and development purposes you will want to be running an Artemis
AMQP broker locally. 

You will also need the Protobuf repo as a submodule.

The following sections detail the various prerequisites more specifically.

### Dotnet and Nuget
You will want to install the `dotnet` package/runtime as well as the `nuget`
package.

### VSCode IDE
The primary tested IDE and workflow involves VSCode. You will also want the
following extensions:

* C# (by Microsoft)
* C# Tools for Godot
* godot-tools
* NuGet Package Manager

### Nuget restore
Once you have installed dotnet and nuget as well as gotten your IDE set up, you
will want to run a `nuget restore` inside the repository folder to initialize
the C# libraries that are used.

### Artemis via Podman
In order to get a specific IP for a local container running
Artemis, you will need to do this as the `root` system user. Podman or Docker 
will both work, although the Docker syntax may be slightly different.

```
podman run --name artemis -it -p 8161:8161 -p 5672:5672 --ip 10.88.0.2 -e AMQ_USER=admin -e AMQ_PASSWORD=admin -e AMQ_ALLOW_ANONYMOUS=true quay.io/artemiscloud/activemq-artemis-broker:latest
```

The above command will run an Artemis container and bind it to the Podman
network on the IP address 10.88.0.2. It will expose ports 8161 (the management
console) as well as 5672 (the AMQP port). 

### Artemis via Shell
You can download the ActiveMQ Artemis broker from the [Apache download
site](https://activemq.apache.org/components/artemis/download/). Currently we
have tested 2.22.0.

Expand the archive somewhere and then, in that folder:

```
./bin/artemis create --user admin --password admin --allow-anonymous srt
```

Then, to run the broker, in that same folder:

```
./srt/bin/artemis run
```

### Protobufs
Submodules to point to the Networking/protobufs that are required. Make sure to
`--recurse-submodules` when you clone this repository. If you've already cloned
prior to this change, then run the following `git submodule update --init`.

Make sure that you are using the same commit/tag for the protobuf submodule in
both the client and server when testing.

### Debugging
If you are debugging the Godot server and Godot client on the same machine you
will want to do one of the following:

* Option 1. Goto `Editor->Editor Settings...` and search for the `Remote Port`
  setting (it's under `Network->Debug'). Change the port so that the client and
  server use different ports.

* Option 2. In the Server project, goto `Project->Export...` and export to your
  platform, then run the exported server.