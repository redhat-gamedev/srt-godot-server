using Godot;
using System;
using System.Collections.Generic;
using redhatgamedev.srt.v1;
using Serilog;

public class Server : Node
{
  int DebugUIRefreshTime = 1; // 1000ms = 1sec
  float DebugUIRefreshTimer = 0;

  Random rnd = new Random();

  //levelSwitch = new LoggingLevelSwitch();
  Serilog.Core.LoggingLevelSwitch levelSwitch = new Serilog.Core.LoggingLevelSwitch();
  public Serilog.Core.Logger _serilogger;

  AMQPserver MessageInterface;

  [Export]
  Dictionary<String, Node2D> playerObjects = new Dictionary<string, Node2D>();

  // the "width" of a hex is 2 * size
  [Export]
  public Int32 SectorSize = 1600;

  public Layout HexLayout;

  // starting ring radius is zero - just one sector
  [Export]
  public int RingRadius = 0;

  // the sector map will only store the number of players in each sector
  // it only gets updated when a new player joins
  [Export]
  Dictionary<String, int> sectorMap = new Dictionary<string, int>();

  [Export]
  public Int32 StarFieldRadiusPixels;

  // The starfield's center may shift during play at small ring sizes due to the
  // way that sectors are added
  [Export]
  Vector2 StarFieldCenter = new Vector2(0, 0);

  [Export]
  float CameraMinZoom = 4f;

  [Export]
  float CameraMaxZoom = 0.1f;

  [Export]
  float CameraZoomStepSize = 0.1f;

  Vector2 CameraCurrentZoom = new Vector2(1, 1);

  Queue<Security> PlayerJoinQueue = new Queue<Security>();

  public Queue<String> PlayerRemoveQueue = new Queue<String>();

  public Queue<Security> SecurityEventQueue = new Queue<Security>();

  public Queue<Command> GameEventQueue = new Queue<Command>();

  /* PLAYER DEFAULTS AND CONFIG */

  float PlayerDefaultThrust = 1f;
  float PlayerDefaultMaxSpeed = 5;
  float PlayerDefaultRotationThrust = 1.5f;
  int PlayerDefaultHitPoints = 100;
  int PlayerDefaultMissileSpeed = 300;
  float PlayerDefaultMissileLife = 2;
  int PlayerDefaultMissileDamage = 25;
  int PlayerDefaultMissileReloadTime = 2;

  /* END PLAYER DEFAULTS AND CONFIG */

  // SCENE PRELOADS
  PackedScene PlayerShipThing = (PackedScene)ResourceLoader.Load("res://Player.tscn");

  // END SCENE PRELOADS

  void SendGameUpdates()
  {
    _serilogger.Verbose("Server.cs: Sending updates about game state to clients");

    Godot.Collections.Array players = GetTree().GetNodesInGroup("player_ships");
    foreach (PlayerShip player in players)
    {
      _serilogger.Verbose($"Server.cs: Sending update for player: {player.uuid}");
      // create the buffer for the specific player
      GameEvent gameEvent = player.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeUpdate);

      // send the event for the player
      MessageInterface.SendGameEvent(gameEvent);
    }

    //foreach (KeyValuePair<String, Node2D> entry in playerObjects)
    //{
    //  _serilogger.Verbose($"Server.cs: Sending update for player: {entry.Key}");

    //  // find the PlayerShip
    //  PlayerShip thePlayer = entry.Value.GetNode<PlayerShip>("PlayerShip");

    //  // create the buffer for the specific player and send it
    //  GameEvent gameEvent = thePlayer.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeUpdate);

    //  // send the player create event message
    //  MessageInterface.SendGameEvent(gameEvent);
    //}

    // TODO: we never send a create message for the missile
    foreach (SpaceMissile missile in GetTree().GetNodesInGroup("missiles"))
    {
      _serilogger.Verbose($"Server.cs: Processing missile: {missile.uuid}");
      // create the buffer for the missile
      GameEvent gameEvent = missile.CreateMissileGameEventBuffer(GameEvent.GameEventType.GameEventTypeUpdate, missile.MyPlayer.uuid);

      // send the buffer for the missile
      MessageInterface.SendGameEvent(gameEvent);
    }
  }

  // called from the player model
  // should this be handled IN the player model itself?

  // TODO: this seems to assume all removes are destroys, but what about simple leaves?
  public void RemovePlayer(String UUID)
  {
    _serilogger.Debug($"Server.cs: Removing player: {UUID}");
    Node2D thePlayerToRemove = playerObjects[UUID];
    PlayerShip thePlayer = thePlayerToRemove.GetNode<PlayerShip>("PlayerShip");

    // create the buffer for the specific player and send it
    GameEvent gameEvent = thePlayer.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeDestroy);

    // TODO: should this get wrapped with a try or something?
    thePlayerToRemove.QueueFree();

    playerObjects.Remove(UUID);

    // send the player create event message
    MessageInterface.SendGameEvent(gameEvent);
  }

  public void RemoveMissile(SpaceMissile missile)
  {
    _serilogger.Debug($"Server.cs: Removing missile: {missile.uuid}");

    // TODO: should this get wrapped with a try or something?
    missile.QueueFree();

    // create the buffer for the specific player and send it
    GameEvent gameEvent = missile.CreateMissileGameEventBuffer(GameEvent.GameEventType.GameEventTypeDestroy, missile.MyPlayer.uuid);

    // send the player create event message
    MessageInterface.SendGameEvent(gameEvent);
  }

  Hex TraverseSectors()
  {
    Hex theCenter = new Hex(0, 0, 0);

    // based around the function from
    // https://www.redblobgames.com/grids/hexagons/#rings

    // need to iterate over all the rings
    for (int x = 1; x <= RingRadius; x++)
    {
      // pick the 0th sector in a ring
      Hex theSector = theCenter.Add(Hex.directions[4].Scale(x));

      // traverse the ring
      for (int i = 0; i < 6; i++)
      {
        for (int j = 0; j < RingRadius; j++)
        {
          string theKey = $"{theSector.q},{theSector.r}";
          if (sectorMap.ContainsKey(theKey))
          {
            // the sector map has the sector we're looking at, so verify how many
            // players are in it. if there are less than two players, use it.
            if (sectorMap[theKey] < 2) { return theSector; }
          }
          else
          {
            // the sector map doesn't have the key for the sector we're looking
            // at. this means the sector definitely has zero players in it.
            return theSector;
          }

          // if we get here, it means that the current sector is full, so move
          // to the next neighbor
          theSector = theSector.Neighbor(i);
        }
      }
    }

    // we got to the end of the rings without finding a sector, so make a new
    // ring and return that ring's first sector

    RingRadius++;
    return theCenter.Add(Hex.directions[4].Scale(RingRadius));
  }

  void UpdateSectorMap()
  {
    // re-initilize the sector map
    sectorMap.Clear();

    foreach (KeyValuePair<String, Node2D> entry in playerObjects)
    {
      PlayerShip thePlayer = entry.Value.GetNode<PlayerShip>("PlayerShip");
      FractionalHex theHex = HexLayout.PixelToHex(new Point(thePlayer.GlobalPosition.x, thePlayer.GlobalPosition.y));
      Hex theRoundedHex = theHex.HexRound();

      // the key can be axial coordinates
      String theSectorKey = $"{theRoundedHex.q},{theRoundedHex.r}";

      // check if the key exists in the dict
      if (sectorMap.ContainsKey(theSectorKey))
      {
        // increment it if it does
        sectorMap[theSectorKey] += 1;
      }
      else
      {
        // initialize it to 1 if it doesn't
        sectorMap[theSectorKey] = 1;
      }
    }
  }

  public void InstantiateMissile(SpaceMissile missile)
  {
    // this only sends the missile creation event buffer

    _serilogger.Debug($"Server.cs: Sending missile creation message for missile {missile.uuid} player {missile.MyPlayer.uuid}");

    // create the protobuf for the player joining
    GameEvent egeb = missile.CreateMissileGameEventBuffer(GameEvent.GameEventType.GameEventTypeCreate, missile.MyPlayer.uuid);

    // send the missile create event message
    MessageInterface.SendGameEvent(egeb);
  }

  void InstantiatePlayer(String UUID)
  {
    // Update the sector map in preparation for traversing the rings, expanding
    // the radius, and etc.  need to do this before adding the new player object
    // because we don't know where that player will go until we traverse the
    // existing sectors, and because the physics process will kick in as soon
    // as the node is created
    UpdateSectorMap();

    // start with the center
    Hex theSector = new Hex(0, 0, 0);

    Node2D playerShipThingInstance = (Node2D)PlayerShipThing.Instance();

    PlayerShip newPlayer = playerShipThingInstance.GetNode<PlayerShip>("PlayerShip");
    newPlayer.uuid = UUID;

    // assign the configured values
    newPlayer.Thrust = PlayerDefaultThrust;
    newPlayer.MaxSpeed = PlayerDefaultMaxSpeed;
    newPlayer.RotationThrust = PlayerDefaultRotationThrust;
    newPlayer.HitPoints = PlayerDefaultHitPoints;
    newPlayer.MissileSpeed = PlayerDefaultMissileSpeed;
    newPlayer.MissileLife = PlayerDefaultMissileLife;
    newPlayer.MissileDamage = PlayerDefaultMissileDamage;

    playerObjects.Add(UUID, playerShipThingInstance);
    newPlayer.AddToGroup("player_ships");

    // if there are more than two players, it means we are now at the point
    // where we have to start calculating ring things
    if (playerObjects.Count > 2)
    {

      // if the ring radius is zero, and we have more than two players, we need
      // to increase it, otherwise things will already blow up
      if (RingRadius == 0)
      {
        RingRadius++;
      }

      // it's possible that we have insufficient players in sector 0,0,0, so
      // check that first for funzos
      if (sectorMap["0,0"] < 2)
      {
        // do nothing since we already assigned the sector to use to 0,0,0
      }

      else
      {
        theSector = TraverseSectors();
      }
    }

    // reset the starfield radius - should also move the center
    StarFieldRadiusPixels = (RingRadius + 1) * SectorSize * 2;

    // now that the sector to insert the player has been selected, find its
    // pixel center
    Point theSectorCenter = HexLayout.HexToPixel(theSector);

    // TODO: need to ensure players are not on top of one another for real.  we
    // will spawn two players into a sector to start, so we should check if
    // there's already a player in the sector first. if there is, we should
    // place the new player equidistant from the already present player

    // badly randomize start position
    int theMin = (int)(SectorSize * 0.3);
    int xOffset = rnd.Next(-1 * theMin, theMin);
    int yOffset = rnd.Next(-1 * theMin, theMin);

    playerShipThingInstance.GlobalPosition = new Vector2(x: (Int32)theSectorCenter.x + xOffset,
                                y: (Int32)theSectorCenter.y + yOffset);

    AddChild(playerShipThingInstance);
    _serilogger.Information("Server.cs: Added player instance!");

    // create the protobuf for the player joining
    GameEvent egeb = newPlayer.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeCreate);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
  }

  void ProcessMoveCommand(Command cb)
  {
    _serilogger.Verbose("Server.cs: Processing move command!");

    String uuid = cb.Uuid;
    Node2D playerRoot = playerObjects[uuid];

    // find the PlayerShip
    PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");

    // process thrust and rotation
    Vector2 thrust = new Vector2(cb.InputX, cb.InputY);

    // push the thrust input onto the player's array
    movePlayer.MovementQueue.Enqueue(thrust);
  }

  void ProcessShootCommand(Command cb)
  {
    _serilogger.Debug("Server.cs: Processing shoot command!");

    // find the PlayerShip
    String playerUUID = cb.Uuid;
    Node2D playerRoot = playerObjects[playerUUID];
    PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");
    // TODO: should we perform a check here to see if we should bother firing
    // the missile, or leave that to the playership.firemissile method alone?

    String missileUUID;
    // check if a missile uuid was suggested and, if not, generate one
    if (cb.MissileUuid == "")
    {
      _serilogger.Debug("supplied missileUUID was null - generating");
      missileUUID = System.Guid.NewGuid().ToString();
    }
    else
    {
      missileUUID = cb.MissileUuid;
    }
    _serilogger.Debug($"missile uuid is: {missileUUID}");

    movePlayer.FireMissile(missileUUID);
  }

  void ProcessPlayerLeave(Security securityBuffer)
  {
    // find the player object
    Node2D playerShip;
    if (playerObjects.TryGetValue(securityBuffer.Uuid, out playerShip))
    {
      // we were able to find an object, so do the leave
      _serilogger.Debug($"Server.cs: Leaving player with UUID: {securityBuffer.Uuid}");

      // TODO: should this be a specific leave instead of destroying a player?
      RemovePlayer(securityBuffer.Uuid);
    }

  }

  void ProcessPlayerJoins()
  {
    while (PlayerJoinQueue.Count > 0)
    {
      Security scb = PlayerJoinQueue.Dequeue();
      InstantiatePlayer(scb.Uuid);
    }
  }

  void ProcessPlayerRemoval()
  {
    while (PlayerRemoveQueue.Count > 0)
    {
      RemovePlayer(PlayerRemoveQueue.Dequeue());
    }
  }
  
  void ProcessGameEvents()
  {
    while (GameEventQueue.Count > 0)
    {
      Command commandBuffer = GameEventQueue.Dequeue();
      switch (commandBuffer.command_type)
      {
        case Command.CommandType.CommandTypeMove:
          _serilogger.Verbose("Server.cs: Move command received");
          ProcessMoveCommand(commandBuffer);
          break;
        case Command.CommandType.CommandTypeShoot:
          _serilogger.Verbose("Server.cs: Shoot command received");
          ProcessShootCommand(commandBuffer);
          break;
        case Command.CommandType.CommandTypeUnspecified:
          _serilogger.Error("Server.cs: Unspecified command received");
          break;
      }
    }
  }
  
  void ProcessSecurityEvents()
  {
    while (SecurityEventQueue.Count >0)
    {
      Security securityBuffer = SecurityEventQueue.Dequeue();
      switch (securityBuffer.security_type)
      {
        case Security.SecurityType.SecurityTypeUnspecified:
          _serilogger.Error("Server.cs: Received an unspecified security event");
          break;
        case Security.SecurityType.SecurityTypeAnnounce:
          _serilogger.Debug($"Server.cs: Received a client announce request from {securityBuffer.Uuid}");
          SendAnnounceDetails(securityBuffer.Uuid);
          break;
        case Security.SecurityType.SecurityTypeJoin:
          _serilogger.Debug($"Server.cs: Got player join for UUID: {securityBuffer.Uuid}");
          // TODO: buffer this because sometimes it collides with sending game
          // updates and an exception is fired because the player collection is
          // modified during looping over it
          PlayerJoinQueue.Enqueue(securityBuffer);
          break;
        case Security.SecurityType.SecurityTypeLeave:
          _serilogger.Debug($"Server.cs: Got player leave for UUID: {securityBuffer.Uuid}");
          ProcessPlayerLeave(securityBuffer);
          break;
      }
    }
  }

  void ProcessInputEvent(Vector2 velocity, Vector2 shoot)
  {
    // fetch the UUID from the text field to use in the message
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");

    // if there is no player in the dictionary, do nothing
    // this catches accidental keyboard hits
    if (!playerObjects.ContainsKey(textField.Text)) { return; }

    // there was some kind of input, so construct a message to send to the server
    //CommandBuffer cb = new CommandBuffer();
    //cb.Type = CommandBuffer.CommandBufferType.Rawinput;

    //RawInputCommandBuffer ricb = new RawInputCommandBuffer();
    //ricb.Type = RawInputCommandBuffer.RawInputCommandBufferType.Dualstick;
    //ricb.Uuid = textField.Text;

    //DualStickRawInputCommandBuffer dsricb = new DualStickRawInputCommandBuffer();
    //if ( (velocity.Length() > 0) || (shoot.Length() > 0) )

    if (velocity.Length() > 0)
    {
      _serilogger.Verbose("Server.cs: velocity length is greater than zero - move");
      Command cb = new Command();
      cb.command_type = Command.CommandType.CommandTypeMove;
      cb.Uuid = textField.Text;

      cb.InputX = (int)velocity.x;
      cb.InputY = (int)velocity.y;
      MessageInterface.SendCommand(cb);
    }

    if (shoot.Length() > 0)
    {
      _serilogger.Verbose("Server.cs: shoot length is greater than zero - shoot");
      Command cb = new Command();
      cb.command_type = Command.CommandType.CommandTypeShoot;
      cb.Uuid = textField.Text;

      cb.InputX = (int)shoot.x;
      cb.InputY = (int)shoot.y;
      MessageInterface.SendCommand(cb);
    }

  }

  // Configuration and Related
  public void SendAnnounceDetails(String UUID)
  {

    // create the announce message
    Security announceMessage = new Security();
    announceMessage.security_type = Security.SecurityType.SecurityTypeAnnounce;
    announceMessage.Uuid = UUID;
    announceMessage.ShipThrust = PlayerDefaultThrust;
    announceMessage.MaxSpeed = PlayerDefaultMaxSpeed;
    announceMessage.RotationThrust = PlayerDefaultRotationThrust;
    announceMessage.HitPoints = PlayerDefaultHitPoints;
    announceMessage.MissileSpeed = PlayerDefaultMissileSpeed;
    announceMessage.MissileLife = PlayerDefaultMissileLife;
    announceMessage.MissileDamage = PlayerDefaultMissileDamage;
    announceMessage.MissileReload = PlayerDefaultMissileReloadTime;

    _serilogger.Debug($"Server.cs: Sending announce details to client {UUID}");
    MessageInterface.SendSecurity(announceMessage);
  }

  public void LoadConfig()
  {
    _serilogger.Information("Server.cs: Configuring");

    var serverConfig = new ConfigFile();
    // save the config file load status to err to check which value to use (config or env) later
    Error err = serverConfig.Load("Config/server.cfg");

    int DesiredLogLevel = 3;

    // if the file was loaded successfully, read the vars
    if (err == Error.Ok)
    {
      SectorSize = (Int32)serverConfig.GetValue("game", "sector_size");
      // player settings
      // https://stackoverflow.com/questions/24447387/cast-object-containing-int-to-float-results-in-invalidcastexception
      PlayerDefaultThrust = Convert.ToSingle(serverConfig.GetValue("player", "thrust"));
      PlayerDefaultMaxSpeed = Convert.ToSingle(serverConfig.GetValue("player", "max_speed"));
      PlayerDefaultRotationThrust = Convert.ToSingle(serverConfig.GetValue("player", "rotation_thrust"));
      PlayerDefaultHitPoints = (int)serverConfig.GetValue("player", "hit_points");
      PlayerDefaultMissileSpeed = (int)serverConfig.GetValue("player", "missile_speed");
      PlayerDefaultMissileLife = Convert.ToSingle(serverConfig.GetValue("player", "missile_life"));
      PlayerDefaultMissileDamage = (int)serverConfig.GetValue("player", "missile_damage");
      PlayerDefaultMissileReloadTime = (int)serverConfig.GetValue("player", "missile_reload");
      DesiredLogLevel = (int)serverConfig.GetValue("game", "log_level");
    }

    // pull values from env -- will get nulls if any vars are not set
    String envSectorSize = System.Environment.GetEnvironmentVariable("SRT_SECTOR_SIZE");
    String envPlayerThrust = System.Environment.GetEnvironmentVariable("SRT_PLAYER_THRUST");
    String envPlayerSpeed = System.Environment.GetEnvironmentVariable("SRT_PLAYER_SPEED");
    String envPlayerRotation = System.Environment.GetEnvironmentVariable("SRT_PLAYER_ROTATION");
    String envPlayerHealth = System.Environment.GetEnvironmentVariable("SRT_PLAYER_HEALTH");
    String envMissileSpeed = System.Environment.GetEnvironmentVariable("SRT_MISSILE_SPEED");
    String envMissileLife = System.Environment.GetEnvironmentVariable("SRT_MISSILE_LIFE");
    String envMissileDamage = System.Environment.GetEnvironmentVariable("SRT_MISSILE_DAMAGE");
    String envMissileReloadTime = System.Environment.GetEnvironmentVariable("SRT_MISSILE_RELOAD_TIME");
    String envLogLevel = System.Environment.GetEnvironmentVariable("SRT_LOG_LEVEL");

    // override any loaded config with env
    if (envSectorSize != null) SectorSize = Int32.Parse(envSectorSize);
    if (envPlayerThrust != null) PlayerDefaultThrust = float.Parse(envPlayerThrust);
    if (envPlayerSpeed != null) PlayerDefaultMaxSpeed = float.Parse(envPlayerSpeed);
    if (envPlayerRotation != null) PlayerDefaultRotationThrust = float.Parse(envPlayerSpeed);
    if (envPlayerHealth != null) PlayerDefaultHitPoints = int.Parse(envPlayerHealth);
    if (envMissileSpeed != null) PlayerDefaultMissileSpeed = int.Parse(envMissileSpeed);
    if (envMissileLife != null) PlayerDefaultMissileLife = float.Parse(envMissileLife);
    if (envMissileDamage != null) PlayerDefaultMissileDamage = int.Parse(envMissileDamage);
    if (envMissileReloadTime != null) PlayerDefaultMissileReloadTime = int.Parse(envMissileReloadTime);
    if (envLogLevel != null) DesiredLogLevel = int.Parse(envLogLevel);

    switch (DesiredLogLevel)
    {
      case 0:
        _serilogger.Information("Server.cs: Setting minimum log level to: Fatal");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Fatal;
        break;
      case 1:
        _serilogger.Information("Server.cs: Setting minimum log level to: Error");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Error;
        break;
      case 2:
        _serilogger.Information("Server.cs: Setting minimum log level to: Warning");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Warning;
        break;
      case 3:
        _serilogger.Information("Server.cs: Setting minimum log level to: Information");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Information;
        break;
      case 4:
        _serilogger.Information("Server.cs: Setting minimum log level to: Debug");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
        break;
      case 5:
        _serilogger.Information("Server.cs: Setting minimum log level to: Verbose");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
        break;
      default:
        _serilogger.Information("Server.cs: Unknown log level specified, defaulting to: Information");
        levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
        break;
    }

    // output the config state
    _serilogger.Information($"Server.cs: Sector Size:         {SectorSize}");
    _serilogger.Information($"Server.cs: Player Thrust:       {PlayerDefaultThrust}");
    _serilogger.Information($"Server.cs: Player Speed:        {PlayerDefaultMaxSpeed}");
    _serilogger.Information($"Server.cs: Player Rotation:     {PlayerDefaultRotationThrust}");
    _serilogger.Information($"Server.cs: Player HP:           {PlayerDefaultHitPoints}");
    _serilogger.Information($"Server.cs: Missile Speed:       {PlayerDefaultMissileSpeed}");
    _serilogger.Information($"Server.cs: Missile Life:        {PlayerDefaultMissileLife}");
    _serilogger.Information($"Server.cs: Missile Damage:      {PlayerDefaultMissileDamage}");
    _serilogger.Information($"Server.cs: Missile Reload Time: {PlayerDefaultMissileReloadTime}");
  }

  void UpdateDebugUI()
  {
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    Tree UIPlayerTree = theCanvas.GetNode<Tree>("CurrentPlayerTree");

    // clear the list and then iterate over all players to rebuild it
    UIPlayerTree.Clear();
    TreeItem UIPlayerTreeRoot = UIPlayerTree.CreateItem();

    foreach (KeyValuePair<String, Node2D> entry in playerObjects)
    {
      TreeItem player = UIPlayerTree.CreateItem(UIPlayerTreeRoot);
      player.SetText(0, entry.Key);
    }
  }

  // Godot Builtins

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    levelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Information;
    _serilogger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.Console().CreateLogger();
    _serilogger.Information("Server.cs: Space Ring Things (SRT) Game Server");
    _serilogger.Information("Server.cs: Attempting AMQP initialization");

    MessageInterface = new AMQPserver();
    AddChild(MessageInterface);

    _serilogger.Information("Server.cs: Beginning game server");

    LoadConfig();

    // initialize the starfield size to the initial sector size
    // the play area is clamped 
    StarFieldRadiusPixels = SectorSize;

    // initialize the hexboard layout
    HexLayout = new Layout(Layout.flat, new Point(SectorSize, SectorSize), new Point(0, 0));

    //  GetTree().Quit();
  }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {

    ProcessSecurityEvents();
    ProcessGameEvents();
    ProcessPlayerRemoval();

    // loosely based on: https://godotengine.org/qa/116981/object-follow-mouse-in-radius
    // get the UUID of the text box and set that ship's camera to active
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");
    if (playerObjects.ContainsKey(textField.Text))
    {
      Node2D playerForCamera = playerObjects[textField.Text];
      Camera2D playerCamera = playerForCamera.GetNode<Camera2D>("PlayerShip/Camera2D");
      if (!playerCamera.Current) { playerCamera.MakeCurrent(); }
    }

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

    if ((velocity.Length() > 0) || (shoot.Length() > 0))
    {
      // TODO: this should probably be putting things on the queue instead
      ProcessInputEvent(velocity, shoot);
    }

    ProcessPlayerJoins();

    SendGameUpdates();

    // https://gdscript.com/solutions/godot-timing-tutorial/
    // check if we should update the debug UI, which itself should only be done if 
    // we are in a graphical mode
    // TODO: only if in graphical debug mode
    // TODO: should also probably use timer node
    DebugUIRefreshTimer += delta;
    if (DebugUIRefreshTimer >= DebugUIRefreshTime)
    {
      // update the UI tree
      DebugUIRefreshTimer = 0;
      _serilogger.Verbose($"Server.cs: Updating UI tree");
      UpdateDebugUI();
    }
  }

  // Internal Signals

  // TODO: improve via signal connection passing in the tree as an arg
  void _on_CurrentPlayerTree_item_selected()
  {
    // figure out which tree item was selected
    _serilogger.Debug($"Server.cs: Handling debug UI tree clicked");
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    Tree UIPlayerTree = theCanvas.GetNode<Tree>("CurrentPlayerTree");
    TreeItem selected = UIPlayerTree.GetSelected();
    _serilogger.Debug($"Server.cs: Tree item selected: {selected.GetText(0)}");

    // update the debug UI text field to match the selected item
    // this causes the debug UI to re-focus on the selected item
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");
    textField.Text = selected.GetText(0);
  }

  // TODO: should move debug to its own scene that's optionally loaded
  void _on_JoinAPlayer_pressed()
  {
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");

    _serilogger.Debug($"Server.cs: Join button pressed for UUID: {textField.Text}");

    // don't do anything if this UUID already exists
    if (playerObjects.ContainsKey(textField.Text))
    {
      _serilogger.Debug($"Server.cs: UUID already exists, doing nothing: {textField.Text}");
      return;
    }

    _serilogger.Debug($"Server.cs: Sending join with UUID: {textField.Text}");

    // construct a join message from the text in the debug field
    //SecurityCommandBuffer scb = new SecurityCommandBuffer();
    Security scb = new Security();

    scb.Uuid = textField.Text;
    scb.security_type = Security.SecurityType.SecurityTypeJoin;

    MessageInterface.SendSecurityDebug(scb);

    //CommandBuffer cb = new CommandBuffer();
    //cb.Type = CommandBuffer.CommandBufferType.Security;
    //cb.securityCommandBuffer = scb;
    //MessageInterface.SendCommand(cb);
  }

  void _on_DeleteAPlayer_pressed()
  {
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");

    _serilogger.Debug($"Server.cs: Delete button pressed for UUID: {textField.Text}");

    // check if the playerobject dictionary has an entry for the uuid in the textfield
    Node2D selectedPlayerNode2D;
    if (playerObjects.TryGetValue(textField.Text, out selectedPlayerNode2D))
    {
      // it does, so remove that player
      _serilogger.Debug($"Server.cs: Removing player with UUID: {textField.Text}");
      RemovePlayer(textField.Text);
    }
  }

  public override void _UnhandledInput(InputEvent @event)
  {

    // hop out if we don't have a player to zoom in on
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");
    if (!playerObjects.ContainsKey(textField.Text)) { return; }

    // grab the camera and zoom it by zoom factor
    Node2D playerForCamera = playerObjects[textField.Text];
    Camera2D playerCamera = playerForCamera.GetNode<Camera2D>("PlayerShip/Camera2D");

    if (@event.IsActionPressed("zoom_in"))
    {
      _serilogger.Debug("Server.cs: zoom viewport in!");
      float zoomN = CameraCurrentZoom.x - CameraZoomStepSize;
      zoomN = Mathf.Clamp(zoomN, CameraMaxZoom, CameraMinZoom);
      CameraCurrentZoom.x = zoomN;
      CameraCurrentZoom.y = zoomN;
      playerCamera.Zoom = CameraCurrentZoom;
      _serilogger.Debug($"Server.cs: Zoom Level: {CameraCurrentZoom.x}, {CameraCurrentZoom.y}");
    }

    if (@event.IsActionPressed("zoom_out"))
    {
      _serilogger.Debug("Server.cs zoom viewport out!");
      float zoomN = CameraCurrentZoom.x + CameraZoomStepSize;
      zoomN = Mathf.Clamp(zoomN, CameraMaxZoom, CameraMinZoom);
      CameraCurrentZoom.x = zoomN;
      CameraCurrentZoom.y = zoomN;
      playerCamera.Zoom = CameraCurrentZoom;
      _serilogger.Debug($"Server.cs: Zoom Level: {CameraCurrentZoom.x}, {CameraCurrentZoom.y}");
    }
  }

}
