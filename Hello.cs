using Godot;
using System;
using System.Threading;
using Amqp;
using Amqp.Framing;
using Amqp.Types;

using Redhatgamedev.Srt;
public class Hello : Sprite
{
  String commandInQueue = "COMMAND.IN";
  String gameEventOutQueue = "GAME.EVENT.OUT";
  SenderLink gameEventOutSender;
  ReceiverLink gameEventOutReceiver;

  //const CommandBuffer = preload("res://proto/CommandBuffer.gd");
  //const GameEventBuffer = preload("res://proto/GameEventBuffer.proto");

  void gameEventReceived(IReceiverLink receiver, Message message)
  {
    GD.Print("Event received!");
    // accept the message so that it gets removed from the queue
    receiver.Accept(message);

    // debug print the message
    object body = message.Body;
    GD.Print(body.ToString());

    // prep a command buffer for processing the message
    //CommandBuffer commandBuffer = new CommandBuffer();
  }
  //
  // Return message as string.
  //
  static String GetContent(Message msg)
  {
    object body = msg.Body;
    return body == null ? null : body.ToString();
  }

  async void initializeAMQP()
  {
    String url = "amqp://10.88.0.10:5672";
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
    gameEventOutReceiver.Start(9999, gameEventReceived);
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    GD.Print("Hello from C# to Godot :)");
    initializeAMQP(); 
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
