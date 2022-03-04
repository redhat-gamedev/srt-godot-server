using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using Amqp;
using Amqp.Framing;
using Amqp.Types;
using ProtoBuf;
using redhatgamedev.srt;

public class Server : Node
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

  [Export]
  Dictionary<String, Player> playerObjects = new Dictionary<string, Player>();

  void InstantiatePlayer(String UUID)
  {
    PackedScene playerScene = (PackedScene)ResourceLoader.Load("res://Player.tscn");
    Player newPlayer = (Player)playerScene.Instance();
    newPlayer.uuid = UUID;

    playerObjects.Add(UUID, newPlayer);

    // figure out the current screen size
    // TODO: this isn't going to work in the future, I don't think
    Node rootNode = GetNode<Node>("/root");
    Vector2 ScreenSize = rootNode.GetViewport().Size;

    // badly randomize start position
    int minX = (int)(ScreenSize.x / 2 * 0.3);
    int minY = (int)(ScreenSize.y / 2 * 0.3);

    Random rnd = new Random();
    int xOffset = rnd.Next(0, minX * 2);
    int yOffset = rnd.Next(0, minY * 2);

    newPlayer.Position = new Vector2(x: minX + xOffset,
                                y: minY + yOffset);

    AddChild(newPlayer);
    cslogger.Debug("Added player instance!");
  }

  void ProcessSecurityGameEvent(SecurityCommandBuffer securityCommandBuffer)
  {
    cslogger.Verbose("Processing security command buffer!");
    switch (securityCommandBuffer.Type)
    {
      case SecurityCommandBuffer.SecurityCommandBufferType.Join:
        cslogger.Info($"Join UUID: {securityCommandBuffer.Uuid}");
        InstantiatePlayer(securityCommandBuffer.Uuid);
        break;
      case SecurityCommandBuffer.SecurityCommandBufferType.Leave:
        cslogger.Info($"Leave UUID: {securityCommandBuffer.Uuid}");
        break;
    }
  }
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

    switch (commandBuffer.Type)
    {
      case CommandBuffer.CommandBufferType.Security:
        cslogger.Verbose("Security event!");
        ProcessSecurityGameEvent(commandBuffer.securityCommandBuffer);
        break;
      case CommandBuffer.CommandBufferType.Rawinput:
        cslogger.Verbose("Raw input event!");

        if (commandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Move != null)
        { ProcessMoveCommand(commandBuffer); }

        if (commandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Shoot != null)
        { ProcessShootCommand(commandBuffer); }
        break;
    }
  }

  void ProcessMoveCommand(CommandBuffer cb)
  {
    cslogger.Verbose("Processing move command!");
    DualStickRawInputCommandBuffer dsricb = cb.rawInputCommandBuffer.dualStickRawInputCommandBuffer;

    String uuid = cb.rawInputCommandBuffer.Uuid;
    Player movePlayer = playerObjects[uuid];

    // process thrust and rotation
    Vector2 thrust = new Vector2(dsricb.pbv2Move.X, dsricb.pbv2Move.Y);

    // push the thrust input onto the player's array
    movePlayer.MovementQueue.Enqueue(thrust);
  }

  void ProcessShootCommand(CommandBuffer cb)
  {
    cslogger.Debug("Processing shoot command!");
    DualStickRawInputCommandBuffer dsricb = cb.rawInputCommandBuffer.dualStickRawInputCommandBuffer;

    String uuid = cb.rawInputCommandBuffer.Uuid;
    Player movePlayer = playerObjects[uuid];

    movePlayer.FireMissile();
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

  void _on_JoinAPlayer_pressed()
  {
    LineEdit textField = GetNode<LineEdit>("PlayerID");

    // don't do anything if this UUID already exists
    if (playerObjects.ContainsKey(textField.Text)) { return; }

    cslogger.Debug($"Sending join with UUID: {textField.Text}");

    // construct a join message from the text in the debug field
    SecurityCommandBuffer scb = new SecurityCommandBuffer();
    scb.Uuid = textField.Text;
    scb.Type = SecurityCommandBuffer.SecurityCommandBufferType.Join;

    CommandBuffer cb = new CommandBuffer();
    cb.Type = CommandBuffer.CommandBufferType.Security;
    cb.securityCommandBuffer = scb;
    SendCommand(cb);
  }

  void ProcessInputEvent(Vector2 velocity, Vector2 shoot)
  {
    // fetch the UUID from the text field to use in the message
    LineEdit textField = GetNode<LineEdit>("PlayerID");

    // there was some kind of input, so construct a message to send to the server
    CommandBuffer cb = new CommandBuffer();
    cb.Type = CommandBuffer.CommandBufferType.Rawinput;

    RawInputCommandBuffer ricb = new RawInputCommandBuffer();
    ricb.Type = RawInputCommandBuffer.RawInputCommandBufferType.Dualstick;
    ricb.Uuid = textField.Text;

    DualStickRawInputCommandBuffer dsricb = new DualStickRawInputCommandBuffer();
    if ( (velocity.Length() > 0) || (shoot.Length() > 0) )

    if (velocity.Length() > 0)
    {
      Box2d.PbVec2 b2dMove = new Box2d.PbVec2();
      b2dMove.X = velocity.x;
      b2dMove.Y = velocity.y;
      dsricb.pbv2Move = b2dMove;
    }

    if (shoot.Length() > 0)
    {
      // TODO: make this actually depend on ship direction
      Box2d.PbVec2 b2dShoot = new Box2d.PbVec2();
      b2dShoot.Y = 1;
      dsricb.pbv2Shoot = b2dShoot;
    }

    ricb.dualStickRawInputCommandBuffer = dsricb;
    cb.rawInputCommandBuffer = ricb;
    SendCommand(cb);
  }

  void SendCommand(CommandBuffer cb)
  {
    // serialize it into a byte stream
    MemoryStream st = new MemoryStream();
    Serializer.Serialize<CommandBuffer>(st, cb);

    byte[] msgBytes = st.ToArray();

    Message msg = new Message(msgBytes);

    // don't care about the ack on our message being received
    commandInSender.Send(msg, null, null);

    // this should work but there's something weird and it blows up the 
    // connection
    //commandInSender.Send(msg);
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    // initialize the logging configuration
    Node gdlogger = GetNode<Node>("/root/GDLogger");
    gdlogger.Call("load_config", "res://logger.cfg");
    cslogger = GetNode<CSLogger>("/root/CSLogger");

    cslogger.Info("Space Ring Things (SRT) Game Server");
    InitializeAMQP();

    cslogger.Info("Beginning game server");
    // TODO: output the current config
  }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {

    // TODO: should probably have some exception fire if we don't connect to
    // AMQ within an appropriate period of time, or get disconnected, etc.

    // look for any inputs, subsequently sent a control message
    var velocity = Vector2.Zero; // The player's movement direction.
    var shoot = Vector2.Zero; // the player's shoot status

    if (Input.IsActionPressed("rotate_right"))
    {
      velocity.x += 1;
    }

    if (Input.IsActionPressed("rotate_left"))
    {
      velocity.x -= 1;
    }

    if (Input.IsActionPressed("thrust_forward"))
    {
      velocity.y += 1;
    }

    if (Input.IsActionPressed("thrust_reverse"))
    {
      velocity.y -= 1;
    }

    if (Input.IsActionPressed("fire"))
    {
      shoot.y = 1;
    }

    if ( (velocity.Length() > 0) || (shoot.Length() > 0) )
    {
      ProcessInputEvent(velocity, shoot);
    }
  }
}
