# Space Ring Things - Godot Edition

## Ship size
Roughly 20m long x 10m wide to start
Currently ~64px at server view

## Play area
10km circle to start (500x ship length)
Currently 32,000px across at server view

## Compiling Protobuf
You will need the `dotnet` command line tool (or equivalent).

Intstall the Protogen tooling:
```
dotnet tool install --global protobuf-net.Protogen --version 3.0.101
```

Then, in the `proto` folder:

```
protogen --csharp_out=. *.proto
```