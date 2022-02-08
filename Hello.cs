using Godot;
using System;
using System.IO;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using ProtoBuf;
using redhatgamedev.srt;

public class Hello : Sprite
{
  // TODO: make config file
  String url = "amqp://10.88.0.10:5672";
  String commandInQueue = "COMMAND.IN";
  String gameEventOutQueue = "GAME.EVENT.OUT";
  SenderLink gameEventOutSender;
  ReceiverLink gameEventOutReceiver;

  void ProcessSecurityGameEvent(SecurityCommandBuffer securityCommandBuffer) {
    GD.Print("Processing security command buffer!");
  }
  void GameEventReceived(IReceiverLink receiver, Message message)
  {
    GD.Print("Event received!");
    // accept the message so that it gets removed from the queue
    receiver.Accept(message);

    byte[] binaryBody = (byte[])message.Body;

    MemoryStream st = new MemoryStream(binaryBody, false);

    // prep a command buffer for processing the message
    CommandBuffer commandBuffer;
    commandBuffer = Serializer.Deserialize<CommandBuffer>(st);

    switch(commandBuffer.Type) {
      case CommandBuffer.CommandBufferType.Security:
        GD.Print("Security event!");
        ProcessSecurityGameEvent(commandBuffer.securityCommandBuffer);
        break;
      case CommandBuffer.CommandBufferType.Rawinput:
        GD.Print("Raw input event!");
        break;
    }
  }

  async void InitializeAMQP()
  {
    Connection.DisableServerCertValidation = true;
    ConnectionFactory factory = new ConnectionFactory();

    // should use async non-blocking connection factory
    Address address = new Address(url);
    var connection = await factory.CreateAsync(address);

    //Connection connection = new Connection(address);
    Session session = new Session(connection);

    // topics are multicast
    // queues are anycast
    // https://stackoverflow.com/a/51595195

    // multicast topic for the server to send game event updates to clients
    Target gameEventOutTarget = new Target
      {
        Address = gameEventOutQueue,
        Capabilities = new Symbol[] { new Symbol("topic") }
      };
    gameEventOutSender = new SenderLink(session, "srt-game-server-sender", gameEventOutTarget, null);

    // anycast queue for the server to receive events from clients
    Source commandInSource = new Source
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    gameEventOutReceiver = new ReceiverLink(session, "srt-game-server-receiver", commandInSource, null);
    gameEventOutReceiver.Start(10, GameEventReceived);
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    GD.Print("Hello from C# to Godot :)");
    InitializeAMQP(); 
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
