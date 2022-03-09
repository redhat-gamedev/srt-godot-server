# Space Ring Things - Godot Edition

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