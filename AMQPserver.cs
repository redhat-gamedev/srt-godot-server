using Godot;
using System;
using System.IO;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using ProtoBuf;
using redhatgamedev.srt;

public class AMQPserver : Node
{
  CSLogger cslogger;

  // TODO: make config file
  String url = "amqp://10.88.0.10:5672";
  String commandInQueue = "COMMAND.IN";
  String gameEventOutQueue = "GAME.EVENT.OUT";

  ConnectionFactory factory;
  Connection amqpConnection;
  Session amqpSession;
  // for sending game events to all clients
  SenderLink gameEventOutSender;

  // for receiving updates from clients
  ReceiverLink commandInReceiver;

  // for debug sending updates
  SenderLink commandInSender;

  // for the server we're interfaced with
  Server MyServer;

  void GameEventReceived(IReceiverLink receiver, Message message)
  {
    cslogger.Verbose("Event received!");
    // accept the message so that it gets removed from the queue
    receiver.Accept(message);

    byte[] binaryBody = (byte[])message.Body;

    MemoryStream st = new MemoryStream(binaryBody, false);

    // prep a command buffer for processing the message
    CommandBuffer commandBuffer;
    commandBuffer = Serializer.Deserialize<CommandBuffer>(st);

    MyServer.ProcessGameEvent(commandBuffer);
  }

  // only used for debug
  public void SendCommand(CommandBuffer CommandBuffer)
  {
    // serialize it into a byte stream
    MemoryStream st = new MemoryStream();
    Serializer.Serialize<CommandBuffer>(st, CommandBuffer);

    byte[] msgBytes = st.ToArray();

    Message msg = new Message(msgBytes);

    // don't care about the ack on our message being received
    commandInSender.Send(msg, null, null);

    // this should work but there's something weird and it blows up the 
    // connection
    //commandInSender.Send(msg);
  }

  async void InitializeAMQP()
  {
    // TODO: should probably wrap in some kind of try and catch failure to connect?
    //       is this even async?
    // TODO: include connection details
    cslogger.Debug("Initializing AMQP connection");
    Connection.DisableServerCertValidation = true;

    //Trace.TraceLevel = TraceLevel.Frame;
    //Trace.TraceListener = (l, f, a) => Console.WriteLine(DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));
    factory = new ConnectionFactory();

    Address address = new Address(url);
    amqpConnection = await factory.CreateAsync(address);

    //Connection connection = new Connection(address);
    amqpSession = new Session(amqpConnection);

    // topics are multicast
    // queues are anycast
    // https://stackoverflow.com/a/51595195

    // multicast topic for the server to send game event updates to clients
    Target gameEventOutTarget = new Target
    {
      Address = gameEventOutQueue,
      Capabilities = new Symbol[] { new Symbol("topic") }
    };
    gameEventOutSender = new SenderLink(amqpSession, "srt-game-server-sender", gameEventOutTarget, null);

    // anycast queue for the server to receive events from clients
    Source commandInSource = new Source
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    commandInReceiver = new ReceiverLink(amqpSession, "srt-game-server-receiver", commandInSource, null);
    commandInReceiver.Start(10, GameEventReceived);

    Target commandInTarget = new Target
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    commandInSender = new SenderLink(amqpSession, "srt-game-server-debug-sender", commandInTarget, null);

    cslogger.Debug("Finished initializing AMQP connection");
  }
  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    // initialize the logging configuration
    Node gdlogger = GetNode<Node>("/root/GDLogger");
    gdlogger.Call("load_config", "res://logger.cfg");
    cslogger = GetNode<CSLogger>("/root/CSLogger");

    MyServer = GetNode<Server>("/root/Server");
    InitializeAMQP();
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
