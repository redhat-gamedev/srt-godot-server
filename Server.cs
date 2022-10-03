using Godot;
using System;
using System.Collections.Generic;
using redhatgamedev.srt;
using Serilog;

public class Server : Node
{
  int DebugUIRefreshTime = 1; // 1000ms = 1sec
  float DebugUIRefreshTimer = 0;

  Random rnd = new Random();

  public Serilog.Core.Logger _serilogger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();

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
  Vector2 StarFieldCenter = new Vector2(0,0);
  
  [Export]
  float CameraMinZoom = 4f;

  [Export]
  float CameraMaxZoom = 0.1f;

  [Export]
  float CameraZoomStepSize = 0.1f;

  Vector2 CameraCurrentZoom = new Vector2(1,1);

  Queue<SecurityCommandBuffer> PlayerJoinQueue = new Queue<SecurityCommandBuffer>();

  /* PLAYER DEFAULTS AND CONFIG */

  float PlayerDefaultThrust = 1f;
  float PlayerDefaultMaxSpeed = 5;
  float PlayerDefaultRotationThrust = 1.5f;
  int PlayerDefaultHitPoints = 100;
  int PlayerDefaultMissileSpeed = 300;
  float PlayerDefaultMissileLife = 4;
  int PlayerDefaultMissileDamage = 25;

  /* END PLAYER DEFAULTS AND CONFIG */

  // SCENE PRELOADS
  PackedScene PlayerShipThing = (PackedScene)ResourceLoader.Load("res://Player.tscn");

  // END SCENE PRELOADS

  void SendGameUpdates()
  {
    _serilogger.Verbose("Server.cs: Sending updates about game state to clients");

    foreach(KeyValuePair<String, Node2D> entry in playerObjects)
    {
      _serilogger.Verbose($"Server.cs: Sending update for player: {entry.Key}");

      // find the PlayerShip
      PlayerShip thePlayer = entry.Value.GetNode<PlayerShip>("PlayerShip");

      // create the buffer for the specific player and send it
      EntityGameEventBuffer egeb = thePlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Update);

      // send the player create event message
      MessageInterface.SendGameEvent(egeb);
    }

    // TODO: we never send a create message for the missile
    foreach(SpaceMissile missile in GetTree().GetNodesInGroup("missiles"))
    {
      _serilogger.Verbose($"Server.cs: Processing missile: {missile.uuid}");
      // create the buffer for the missile
      EntityGameEventBuffer egeb = missile.CreateMissileGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Update, missile.MyPlayer.uuid);

      // send the buffer for the missile
      MessageInterface.SendGameEvent(egeb);
    }
  }

  // called from the player model
  // should this be handled IN the player model itself?
  public void RemovePlayer(String UUID)
  {
    _serilogger.Debug($"Server.cs: Removing player: {UUID}");
    Node2D thePlayerToRemove = playerObjects[UUID];
    PlayerShip thePlayer = thePlayerToRemove.GetNode<PlayerShip>("PlayerShip");

    // create the buffer for the specific player and send it
    EntityGameEventBuffer egeb = thePlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Destroy);

    // TODO: should this get wrapped with a try or something?
    thePlayerToRemove.QueueFree();
    playerObjects.Remove(UUID);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
  }

  public void RemoveMissile(SpaceMissile missile)
  {
    _serilogger.Debug($"Server.cs: Removing missile: {missile.uuid}");

    // TODO: should this get wrapped with a try or something?
    missile.QueueFree();

    // create the buffer for the specific player and send it
    EntityGameEventBuffer egeb = missile.CreateMissileGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Destroy, missile.MyPlayer.uuid);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
  }

  Hex TraverseSectors()
  {
    Hex theCenter = new Hex(0,0,0);

    // based around the function from
    // https://www.redblobgames.com/grids/hexagons/#rings

    // need to iterate over all the rings
    for (int x = 1; x <= RingRadius; x++)
    {
      // pick the 0th sector in a ring
      Hex theSector = theCenter.Add( Hex.directions[4].Scale(x) );

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
    return theCenter.Add( Hex.directions[4].Scale(RingRadius) );
  }

  void UpdateSectorMap()
  {
    // re-initilize the sector map
    sectorMap.Clear();

    foreach(KeyValuePair<String, Node2D> entry in playerObjects)
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
    EntityGameEventBuffer egeb = missile.CreateMissileGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Create, missile.MyPlayer.uuid);

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
    Hex theSector = new Hex(0,0,0);

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
    StarFieldRadiusPixels = (RingRadius+1) * SectorSize * 2;

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
    EntityGameEventBuffer egeb = newPlayer.CreatePlayerGameEventBuffer(EntityGameEventBuffer.EntityGameEventBufferType.Create);

    // send the player create event message
    MessageInterface.SendGameEvent(egeb);
  }

  void ProcessMoveCommand(CommandBuffer cb)
  {
    _serilogger.Verbose("Server.cs: Processing move command!");
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
    _serilogger.Debug("Server.cs: Processing shoot command!");
    DualStickRawInputCommandBuffer dsricb = cb.rawInputCommandBuffer.dualStickRawInputCommandBuffer;

    String playerUUID = cb.rawInputCommandBuffer.Uuid;
    String missileUUID = dsricb.missileUUID;
    Node2D playerRoot = playerObjects[playerUUID];

    // find the PlayerShip
    PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");

    movePlayer.FireMissile(missileUUID);
  }

  void ProcessSecurityGameEvent(SecurityCommandBuffer securityCommandBuffer)
  {
    _serilogger.Verbose("Server.cs: Processing security command buffer!");
    switch (securityCommandBuffer.Type)
    {
      case SecurityCommandBuffer.SecurityCommandBufferType.Join:
        _serilogger.Information($"Server.cs: Join UUID: {securityCommandBuffer.Uuid}");
        // TODO: buffer this because sometimes it collides with sending game
        // updates and an exception is fired because the player collection is
        // modified during looping over it
        PlayerJoinQueue.Enqueue(securityCommandBuffer);
        break;
      case SecurityCommandBuffer.SecurityCommandBufferType.Leave:
        _serilogger.Information($"Server.cs: Leave UUID: {securityCommandBuffer.Uuid}");
        ProcessPlayerLeave(securityCommandBuffer);
        break;
    }
  }

  void ProcessPlayerLeave(SecurityCommandBuffer securityCommandBuffer)
  {
    // find the player object
    Node2D playerShip;
    if (playerObjects.TryGetValue(securityCommandBuffer.Uuid, out playerShip))
    {
      // we were able to find an object, so do the leave
      _serilogger.Debug($"Server.cs: Leaving player with UUID: {securityCommandBuffer.Uuid}");
      RemovePlayer(securityCommandBuffer.Uuid);
    }

  }

  void ProcessPlayerJoins()
  {

    while (PlayerJoinQueue.Count > 0)
    {
      SecurityCommandBuffer scb = PlayerJoinQueue.Dequeue();
      InstantiatePlayer(scb.Uuid);
    }

  }

  public void ProcessGameEvent(CommandBuffer CommandBuffer)
  {
    switch (CommandBuffer.Type)
    {
      case CommandBuffer.CommandBufferType.Security:
        _serilogger.Verbose("Server.cs: Security event!");
        ProcessSecurityGameEvent(CommandBuffer.securityCommandBuffer);
        break;
      case CommandBuffer.CommandBufferType.Rawinput:
        _serilogger.Verbose("Server.cs: Raw input event!");

        if (CommandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Move != null)
        { ProcessMoveCommand(CommandBuffer); }

        if (CommandBuffer.rawInputCommandBuffer.dualStickRawInputCommandBuffer.pbv2Shoot != null)
        { ProcessShootCommand(CommandBuffer); }
        break;
    }
  }

  public void LoadConfig()
  {
    _serilogger.Information("Server.cs: Configuring");

    var serverConfig = new ConfigFile();
    // save the config file load status to err to check which value to use (config or env) later
    Error err = serverConfig.Load("Config/server.cfg");

    // if the file was loaded successfully, read the vars
    if (err == Error.Ok) 
    {
      SectorSize = (Int32) serverConfig.GetValue("game","sector_size");
      // player settings
      // https://stackoverflow.com/questions/24447387/cast-object-containing-int-to-float-results-in-invalidcastexception
      PlayerDefaultThrust = Convert.ToSingle(serverConfig.GetValue("player","thrust"));
      PlayerDefaultMaxSpeed = Convert.ToSingle(serverConfig.GetValue("player","max_speed"));
      PlayerDefaultRotationThrust = Convert.ToSingle(serverConfig.GetValue("player","rotation_thrust"));
      PlayerDefaultHitPoints = (int) serverConfig.GetValue("player","hit_points");
      PlayerDefaultMissileSpeed = (int) serverConfig.GetValue("player","missile_speed");
      PlayerDefaultMissileLife = Convert.ToSingle(serverConfig.GetValue("player","missile_life"));
      PlayerDefaultMissileDamage = (int) serverConfig.GetValue("player","missile_damage");
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

    // override any loaded config with env
    if (envSectorSize != null) SectorSize = Int32.Parse(envSectorSize);
    if (envPlayerThrust != null) PlayerDefaultThrust = float.Parse(envPlayerThrust);
    if (envPlayerSpeed != null) PlayerDefaultMaxSpeed = float.Parse(envPlayerSpeed);
    if (envPlayerRotation != null) PlayerDefaultRotationThrust = float.Parse(envPlayerSpeed);
    if (envPlayerHealth != null) PlayerDefaultHitPoints = int.Parse(envPlayerHealth);
    if (envMissileSpeed != null) PlayerDefaultMissileSpeed = int.Parse(envMissileSpeed);
    if (envMissileLife != null) PlayerDefaultMissileLife = float.Parse(envMissileLife);
    if (envMissileDamage != null) PlayerDefaultMissileDamage = int.Parse(envMissileDamage);

    // output the config state
    _serilogger.Information($"Server.cs: Sector Size:      {SectorSize}");
    _serilogger.Information($"Server.cs: Player Thrust:    {PlayerDefaultThrust}");
    _serilogger.Information($"Server.cs: Player Speed:     {PlayerDefaultMaxSpeed}");
    _serilogger.Information($"Server.cs: Player Rotation:  {PlayerDefaultRotationThrust}");
    _serilogger.Information($"Server.cs: Player HP:        {PlayerDefaultHitPoints}");
    _serilogger.Information($"Server.cs: Missile Speed:    {PlayerDefaultMissileSpeed}");
    _serilogger.Information($"Server.cs: Missile Life:     {PlayerDefaultMissileLife}");
    _serilogger.Information($"Server.cs: Missile Damage:   {PlayerDefaultMissileDamage}");
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    _serilogger.Information("Server.cs: Space Ring Things (SRT) Game Server");

    MessageInterface = GetNode<AMQPserver>("/root/AMQPserver");

    _serilogger.Information("Server.cs: Beginning game server");

    LoadConfig();

    // initialize the starfield size to the initial sector size
    // the play area is clamped 
    StarFieldRadiusPixels = SectorSize;

    // initialize the hexboard layout
    HexLayout = new Layout(Layout.flat, new Point(SectorSize,SectorSize), new Point(0,0));

    //  GetTree().Quit();
  }


  // ****** THINGS RELATED TO DEBUG ******

  void UpdateDebugUI()
  { 
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    Tree UIPlayerTree = theCanvas.GetNode<Tree>("CurrentPlayerTree");

    // clear the list and then iterate over all players to rebuild it
    UIPlayerTree.Clear();
    TreeItem UIPlayerTreeRoot = UIPlayerTree.CreateItem();

    foreach(KeyValuePair<String, Node2D> entry in playerObjects)
    {
      TreeItem player = UIPlayerTree.CreateItem(UIPlayerTreeRoot);
      player.SetText(0, entry.Key);
    }
  }

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

  void ProcessInputEvent(Vector2 velocity, Vector2 shoot)
  {
    // fetch the UUID from the text field to use in the message
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");

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
    CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
    LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");

    _serilogger.Debug($"Server.cs: Join button pressed for UUID: {textField.Text}");

    // don't do anything if this UUID already exists
    if (playerObjects.ContainsKey(textField.Text)) { return; }

    _serilogger.Debug($"Server.cs: Sending join with UUID: {textField.Text}");

    // construct a join message from the text in the debug field
    SecurityCommandBuffer scb = new SecurityCommandBuffer();
    scb.Uuid = textField.Text;
    scb.Type = SecurityCommandBuffer.SecurityCommandBufferType.Join;

    CommandBuffer cb = new CommandBuffer();
    cb.Type = CommandBuffer.CommandBufferType.Security;
    cb.securityCommandBuffer = scb;
    MessageInterface.SendCommand(cb);
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

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {

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

    if ( (velocity.Length() > 0) || (shoot.Length() > 0) )
    {
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
}
