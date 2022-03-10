using Godot;
using System;
using System.Collections.Generic;
using redhatgamedev.srt;

public class Server : Node
{
  CSLogger cslogger;

  AMQPserver MessageInterface;

  [Export]
  Dictionary<String, Node2D> playerObjects = new Dictionary<string, Node2D>();

  void SendGameUpdates()
  {
    cslogger.Verbose("Server.cs: Sending updates about game state to clients");

    foreach(KeyValuePair<String, Node2D> entry in playerObjects)
    {
      cslogger.Verbose($"Server.cs: Sending update for player: {entry.Key}");

      // find the PlayerShip
      PlayerShip thePlayer = entry.Value.GetNode<PlayerShip>("PlayerShip");

      // create the buffer for the specific player and send it
      EntityGameEventBuffer egeb = thePlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Update);

      // send the player create event message
      MessageInterface.SendGameEvent(egeb);
    }
  }

  public void RemovePlayer(String UUID)
  {
    cslogger.Debug($"Server.cs: Removing player: {UUID}");
    Node2D thePlayerToRemove = playerObjects[UUID];
    PlayerShip thePlayer = thePlayerToRemove.GetNode<PlayerShip>("PlayerShip");

    // TODO: should this get wrapped with a try or something?
    thePlayerToRemove.QueueFree();
    playerObjects.Remove(UUID);

    // create the buffer for the specific player and send it
    EntityGameEventBuffer egeb = thePlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Destroy);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
    
  }

  void InstantiatePlayer(String UUID)
  {
    PackedScene playerShipThing = (PackedScene)ResourceLoader.Load("res://Player.tscn");
    Node2D playerShipThingInstance = (Node2D)playerShipThing.Instance();

    PlayerShip newPlayer = playerShipThingInstance.GetNode<PlayerShip>("PlayerShip");
    newPlayer.uuid = UUID;

    playerObjects.Add(UUID, playerShipThingInstance);

    // figure out the current screen size
    // TODO: this isn't going to work in the future, I don't think
    Node rootNode = GetNode<Node>("/root");
    Vector2 ScreenSize = rootNode.GetViewport().Size;

    // TODO: need to ensure players are not on top of one another for real
    // badly randomize start position
    int minX = (int)(ScreenSize.x / 2 * 0.3);
    int minY = (int)(ScreenSize.y / 2 * 0.3);

    Random rnd = new Random();
    int xOffset = rnd.Next(0, minX * 2);
    int yOffset = rnd.Next(0, minY * 2);

    playerShipThingInstance.Position = new Vector2(x: minX + xOffset,
                                y: minY + yOffset);

    AddChild(playerShipThingInstance);
    cslogger.Debug("Server.cs: Added player instance!");

    // create the protobuf for the player joining
    EntityGameEventBuffer egeb = newPlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Create);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
  }

  void ProcessMoveCommand(CommandBuffer cb)
  {
    cslogger.Verbose("Server.cs: Processing move command!");
    DualStickRawInputCommandBuffer dsricb = cb.rawInputCommandBuffer.dualStickRawInputCommandBuffer;

    String uuid = cb.rawInputCommandBuffer.Uuid;
    Node2D playerRoot = playerObjects[uuid];

    // find the PlayerShip
    PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");

    // process thrust and rotation
    Vector2 thrust = new Vector2(dsricb.pbv2Move.X, dsricb.pbv2Move.Y);

    // push the thrust input onto the player's array
    movePlayer.MovementQueue.Enqueue(thrust);
  }

  void ProcessShootCommand(CommandBuffer cb)
  {
    cslogger.Debug("Server.cs: Processing shoot command!");
    DualStickRawInputCommandBuffer dsricb = cb.rawInputCommandBuffer.dualStickRawInputCommandBuffer;

    String uuid = cb.rawInputCommandBuffer.Uuid;
    Node2D playerRoot = playerObjects[uuid];

    // find the PlayerShip
    PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");

    movePlayer.FireMissile();
  }

  void ProcessSecurityGameEvent(SecurityCommandBuffer securityCommandBuffer)
  {
    cslogger.Verbose("Server.cs: Processing security command buffer!");
    switch (securityCommandBuffer.Type)
    {
      case SecurityCommandBuffer.SecurityCommandBufferType.Join:
        cslogger.Info($"Server.cs: Join UUID: {securityCommandBuffer.Uuid}");
        InstantiatePlayer(securityCommandBuffer.Uuid);
        break;
      case SecurityCommandBuffer.SecurityCommandBufferType.Leave:
        cslogger.Info($"Server.cs: Leave UUID: {securityCommandBuffer.Uuid}");
        break;
    }
  }

  public void ProcessGameEvent(CommandBuffer CommandBuffer)
  {
    switch (CommandBuffer.Type)
    {
      case CommandBuffer.CommandBufferType.Security:
        cslogger.Verbose("Server.cs: Security event!");
        ProcessSecurityGameEvent(CommandBuffer.securityCommandBuffer);
        break;
      case CommandBuffer.CommandBufferType.Rawinput:
        cslogger.Verbose("Raw input event!");

        if (CommandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Move != null)
        { ProcessMoveCommand(CommandBuffer); }

        if (CommandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Shoot != null)
        { ProcessShootCommand(CommandBuffer); }
        break;
    }
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    // initialize the logging configuration
    Node gdlogger = GetNode<Node>("/root/GDLogger");
    gdlogger.Call("load_config", "res://logger.cfg");
    cslogger = GetNode<CSLogger>("/root/CSLogger");

    cslogger.Info("Space Ring Things (SRT) Game Server");

    MessageInterface = GetNode<AMQPserver>("/root/AMQPserver");

    cslogger.Info("Beginning game server");
    // TODO: output the current config
  }


  // ****** THINGS RELATED TO DEBUG ******
  void ProcessInputEvent(Vector2 velocity, Vector2 shoot)
  {
    // fetch the UUID from the text field to use in the message
    LineEdit textField = GetNode<LineEdit>("PlayerID");

    // if there is no player in the dictionary, do nothing
    // this catches accidental keyboard hits
    if (!playerObjects.ContainsKey(textField.Text)) { return; }

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
    MessageInterface.SendCommand(cb);
  }

  // TODO: should move debug to its own scene that's optionally loaded
  void _on_JoinAPlayer_pressed()
  {
    LineEdit textField = GetNode<LineEdit>("PlayerID");

    // don't do anything if this UUID already exists
    if (playerObjects.ContainsKey(textField.Text)) { return; }

    cslogger.Debug($"Server.cs: Sending join with UUID: {textField.Text}");

    // construct a join message from the text in the debug field
    SecurityCommandBuffer scb = new SecurityCommandBuffer();
    scb.Uuid = textField.Text;
    scb.Type = SecurityCommandBuffer.SecurityCommandBufferType.Join;

    CommandBuffer cb = new CommandBuffer();
    cb.Type = CommandBuffer.CommandBufferType.Security;
    cb.securityCommandBuffer = scb;
    MessageInterface.SendCommand(cb);
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

    SendGameUpdates();
  }
}
