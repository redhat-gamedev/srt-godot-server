using Godot;
using System;
using System.IO;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using ProtoBuf;
using redhatgamedev.srt.v1;

public class AMQPserver : Node
{
  public Serilog.Core.Logger _serilogger;

  // TODO: make config file
  String url = "amqp://localhost:5672";
  String commandInQueue = "COMMAND.IN";
  String securityInQueue = "SECURITY.IN";
  // for receiving game event updates from clients
  ReceiverLink commandInReceiver;
  // for receiving security updates from clients
  ReceiverLink securityInReceiver;

  String gameEventOutQueue = "GAME.EVENT.OUT";
  String securityOutQueue = "SECURITY.OUT";
  // for sending game events to all clients
  SenderLink gameEventOutSender;
  // for sending security events to all clients
  SenderLink securityOutSender;
  // for debug sending updates
  SenderLink commandInSender;
  SenderLink securityInSender;

  ConnectionFactory factory;
  Connection amqpConnection;
  Session amqpSession;

  // for the server we're interfaced with
  Server MyServer;

  void CommandReceived(IReceiverLink receiver, Message message)
  {
    _serilogger.Verbose("AMQPserver.cs: Client game event received!");
    // accept the message so that it gets removed from the queue
    receiver.Accept(message);

    byte[] binaryBody = (byte[])message.Body;

    MemoryStream st = new MemoryStream(binaryBody, false);

    // prep a command buffer for processing the message
    Command commandBuffer;
    commandBuffer = Serializer.Deserialize<Command>(st);

    MyServer.ProcessGameEvent(commandBuffer);
  }

  void SecurityReceived(IReceiverLink receiver, Message message)
  {
    _serilogger.Debug("AMQPserver.cs: Client security event received!");
    // accept the message so that it gets removed from the queue
    receiver.Accept(message);

    byte[] binaryBody = (byte[])message.Body;

    MemoryStream st = new MemoryStream(binaryBody, false);

    // prep a command buffer for processing the message
    Security securityBuffer;
    securityBuffer = Serializer.Deserialize<Security>(st);

    MyServer.ProcessSecurityEvent(securityBuffer);
  }


  public void SendGameEvent(GameEvent gameEvent)
  {
    _serilogger.Verbose("AMQPserver.cs: Sending game event");
    // serialize it into a byte stream
    MemoryStream st = new MemoryStream();
    Serializer.Serialize<GameEvent>(st, gameEvent);

    byte[] msgBytes = st.ToArray();

    Message msg = new Message(msgBytes);

    // create and destroy messages are mega important, so don't set a TTL for those
    // updates are not as important, so set a low TTL for those
    if (!(gameEvent.game_event_type == GameEvent.GameEventType.GameEventTypeCreate 
      | gameEvent.game_event_type == GameEvent.GameEventType.GameEventTypeDestroy))
      { msg.Header = new Header() { Ttl = 250 }; }

    // don't care about the ack on our message being received
    gameEventOutSender.Send(msg, null, null);

    // this should work but there's something weird and it blows up the 
    // connection
    //gameEventOutSender.Send(msg);
  }

  // only used for debug
  public void SendCommand(Command CommandBuffer)
  {
    _serilogger.Debug("AMQPServer.cs: Sending command");

    // serialize it into a byte stream
    MemoryStream st = new MemoryStream();
    Serializer.Serialize<Command>(st, CommandBuffer);

    byte[] msgBytes = st.ToArray();

    Message msg = new Message(msgBytes);

    // don't care about the ack on our message being received
    commandInSender.Send(msg, null, null);

    // this should work but there's something weird and it blows up the 
    // connection
    //commandInSender.Send(msg);
  }

  public void SendSecurity(Security security)
  {
    _serilogger.Debug("AMQPServer.cs: Sending security command");

    // serialize it into a byte stream
    MemoryStream st = new MemoryStream();
    Serializer.Serialize<Security>(st, security);

    byte[] msgBytes = st.ToArray();

    Message msg = new Message(msgBytes);

    // don't care about the ack on our message being received
    securityInSender.Send(msg, null, null);

    // this should work but there's something weird and it blows up the 
    // connection
    //commandInSender.Send(msg);
  }

  async void InitializeAMQP()
  {
    // TODO: should probably wrap in some kind of try and catch failure to connect?
    //       is this even async?
    // TODO: include connection details
    _serilogger.Debug("AMQPserver.cs: Initializing AMQP connection");
    Connection.DisableServerCertValidation = true;

    //Trace.TraceLevel = TraceLevel.Frame;
    //Trace.TraceListener = (l, f, a) => Console.WriteLine(DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));
    factory = new ConnectionFactory();
    Address address = new Address(url);
    amqpConnection = await factory.CreateAsync(address);
    amqpSession = new Session(amqpConnection);

    // set up queues and topics ////////////////////////////////////////////////
    // topics are multicast
    // queues are anycast
    // https://stackoverflow.com/a/51595195

    // output to clients ///////////////////////////////////////////////////////
    // multicast topic for the server to send game event updates to clients
    Target gameEventOutTarget = new Target
    {
      Address = gameEventOutQueue,
      Capabilities = new Symbol[] { new Symbol("topic") }
    };
    gameEventOutSender = new SenderLink(amqpSession, "srt-game-server-command-sender", gameEventOutTarget, null);

    // multicast topic for the server to send security updates to clients
    Target securityOutTarget = new Target
    {
      Address = securityOutQueue,
      Capabilities = new Symbol[] { new Symbol("topic") }
    };
    securityOutSender = new SenderLink(amqpSession, "srt-game-server-security-sender", securityOutTarget, null);

    // inputs from clients /////////////////////////////////////////////////////
    // anycast queue for the server to receive command events from clients
    Source commandInSource = new Source
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    commandInReceiver = new ReceiverLink(amqpSession, "srt-game-server-command-receiver", commandInSource, null);
    commandInReceiver.Start(10, CommandReceived);

    // anycast queue for the server to receive security events from clients
    Source securityInSource = new Source
    {
      Address = securityInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    securityInReceiver = new ReceiverLink(amqpSession, "srt-game-server-security-receiver", securityInSource, null);
    securityInReceiver.Start(10, SecurityReceived);


    // DEBUG stuff /////////////////////////////////////////////////////////////
    // we send commands to the COMMAND.IN from the debug UI
    Target commandInTarget = new Target
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    commandInSender = new SenderLink(amqpSession, "srt-game-server-debug-command-sender", commandInTarget, null);

    // we send security messages to SECURITY.IN from the debug UI
    Target securityInTarget = new Target
    { 
      Address = securityInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    securityInSender = new SenderLink(amqpSession, "srt-game-server-debug-security-sender", securityInTarget, null);

    _serilogger.Debug("AMQPserver.cs: Finished initializing AMQP connection");
  }

  public void LoadConfig()
  {
    var serverConfig = new ConfigFile();
    Godot.Error err = serverConfig.Load("Config/server.cfg");

    // if the file was loaded successfully, read the vars
    if (err == Godot.Error.Ok) 
    {
      url = (String) serverConfig.GetValue("amqp","server_string");
    }
    // pull values from env -- will get nulls if any vars are not set
    String envAMQPUrl = System.Environment.GetEnvironmentVariable("SRT_AMQP_URL");

    if (envAMQPUrl != null) url = envAMQPUrl;

    _serilogger.Information($"AMQPServer.cs: AMQP url is {url}");
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    MyServer = GetNode<Server>("/root/Server");
    _serilogger = MyServer._serilogger;
    LoadConfig();
    InitializeAMQP();
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
