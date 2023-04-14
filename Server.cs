using Godot;
using System;
using System.Collections.Generic;
using redhatgamedev.srt.v1;
using Serilog;

public partial class Server : Node
{
  int DebugUIRefreshTime = 1; // 1000ms = 1sec
  double DebugUIRefreshTimer = 0.0f;

  Random rnd = new Random();

  //levelSwitch = new LoggingLevelSwitch();
  Serilog.Core.LoggingLevelSwitch levelSwitch = new Serilog.Core.LoggingLevelSwitch();
  public Serilog.Core.Logger _serilogger;

  AMQPserver MessageInterface;

  //[Export]
  //3to4
  Dictionary<String, Node2D> playerObjects = new Dictionary<string, Node2D>();

  // the "width" of a hex is 2 * size
  [Export]
  public Int32 SectorSize = 1600;

  public Layout HexLayout;

  int HexRatio;

  // starting ring radius is zero - just one sector
  [Export]
  public int RingRadius = 0;

  // the sector map will only store the number of players in each sector
  // it only gets updated when a new player joins
  //[Export]
  //3to4
  Dictionary<String, int> sectorMap = new Dictionary<string, int>();

  //[Export]
  //3to4
  Dictionary<String, Node2D> sectorNodes = new Dictionary<String, Node2D>();

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
  PackedScene PlayerPackedScene = (PackedScene)ResourceLoader.Load("res://Player.tscn");
  PackedScene SectorPackedScene = (PackedScene)ResourceLoader.Load("res://Sector.tscn");

  // END SCENE PRELOADS

  Label RingSize;
  Label StarFieldRadiusSize;
  Node SectorMap;
  Node Players;
  StarFieldRadius StarFieldRing;
  VBoxContainer theCanvas;
  LineEdit playerID;
  Tree uiPlayerTree;
  Camera2D rtsCamera;
  Camera2D currentCamera;

  void SendGameUpdates()
  {
	_serilogger.Verbose("Server.cs: Sending updates about game state to clients");

	Godot.Collections.Array<Node> players = _GetNodesFromGroup("player_ships");
	foreach (PlayerShip player in players)
	{
	  _serilogger.Verbose($"Server.cs: Sending update for player: {player.uuid}");
	  // create the buffer for the specific player
	  GameEvent gameEvent = player.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeUpdate);

	  // send the event for the player
	  MessageInterface.SendGameEvent(gameEvent);
	}

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
	playerObjects.Remove(UUID);
	thePlayerToRemove.QueueFree();

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
	_serilogger.Debug($"Server.cs: Traversing the sectors");

	// based around the function from
	// https://www.redblobgames.com/grids/hexagons/#rings

	// need to iterate over all the rings
	for (int currentRing = 1; currentRing <= RingRadius; currentRing++)
	{
	  _serilogger.Debug($"Server.cs: Traversing ring {currentRing}");
	  // pick the 0th sector in a ring
	  Hex theSector = theCenter.Add(Hex.directions[4].Scale(currentRing));
	  _serilogger.Debug($"Server.cs: starting with sector {theSector.q},{theSector.r}");

	  // traverse the ring
	  for (int i = 0; i < 6; i++)
	  {
		for (int j = 0; j < currentRing; j++)
		{
		  string theKey = $"{theSector.q},{theSector.r}";
		  _serilogger.Debug($"Server.cs: Checking sector {theKey}");
		  if (sectorMap.ContainsKey(theKey))
		  {
			// the sector map has the sector we're looking at, so verify how many
			// players are in it. if there are less than two players, use it.
			if (sectorMap[theKey] < 2) 
			{ 
			  _serilogger.Debug($"Server.cs: Less than two players ({sectorMap[theKey]}) in {theKey}, so choosing this one");
			  return theSector; 
			}
		  }
		  else
		  {
			// the sector map doesn't have the key for the sector we're looking
			// at. this means the sector definitely has zero players in it.
			_serilogger.Debug($"Server.cs: {theKey} not found in sectorMap, so choosing this one");
			return theSector;
		  }

		  // if we get here, it means that the current sector is full, so move
		  // to the next neighbor
		  _serilogger.Debug($"Server.cs: {theKey} is full with {sectorMap[theKey]} players, moving on");
		  theSector = theSector.Neighbor(i);
		}
	  }
	}

	// we got to the end of the rings without finding a sector, so make a new
	// ring and return that ring's first sector

	_serilogger.Debug($"Server.cs: Completed traversing ring {RingRadius}, incrementing and returning 0th sector of new ring");
	RingRadius++;
	_serilogger.Debug($"Server.cs: New ringradius is {RingRadius}");
	Hex hexToReturn = theCenter.Add(Hex.directions[4].Scale(RingRadius));
	_serilogger.Debug($"Server.cs: 0th sector of new ring is {hexToReturn.q},{hexToReturn.r}");
	return hexToReturn;
  }

  void UpdateSectorMap()
  {
	// re-initilize the sector map
	// clear the dictionary for rebuild
	sectorMap.Clear();
	_serilogger.Debug($"Server.cs: Updating sector map");

	// delete all of the drawn sectors unless there's no players
	Godot.Collections.Array<Node> players = _GetNodesFromGroup("player_ships");
	if (players.Count != 0) {
	  _serilogger.Debug($"Server.cs: There are players in the player_ships group, so clearing the sector nodes");
	  sectorNodes.Clear();
	  foreach (Node sector in _GetNodesFromGroup("sectors"))
	  {
		sector.QueueFree();
	  }
	}

	_serilogger.Debug($"Server.cs: Currently {players.Count} players in 'player_ships' group");
	foreach (PlayerShip player in players)
	{
	  _serilogger.Debug($"Server.cs: Checking where {player.uuid} is in sector map");
	  FractionalHex theHex = HexLayout.PixelToHex(new Point(player.GlobalPosition.X, player.GlobalPosition.Y));
	  Hex theRoundedHex = theHex.HexRound();

	  // the key can be axial coordinates
	  String theSectorKey = $"{theRoundedHex.q},{theRoundedHex.r}";
	  _serilogger.Debug($"Server.cs: player {player.uuid} sector is {theSectorKey}");

	  // check if the key exists in the dict
	  if (sectorMap.ContainsKey(theSectorKey))
	  {
		// increment it if it does
		sectorMap[theSectorKey] += 1;
		_serilogger.Debug($"Server.cs: {theSectorKey}: {sectorMap[theSectorKey]}");
	  }
	  else
	  {
		// initialize it to 1 if it doesn't
		_serilogger.Debug($"Server.cs: {theSectorKey} didn't exist, initializing to 1");
		sectorMap[theSectorKey] = 1;
		_serilogger.Debug($"Server.cs: {theSectorKey}: {sectorMap[theSectorKey]}");
	  }
	}

  }

  void DrawSectorMap()
  {
	// recreate the sector nodes for all the present keys
	_serilogger.Debug($"Server.cs: Iterating over sectormap's keys to place sector nodes");
	foreach (string sector in sectorMap.Keys)
	{
	  _serilogger.Debug($"Server.cs: Current sector is {sector}");
	  String[] sector_parts = sector.Split(',');

	  // q + r + s must equal zero
	  int q = sector_parts[0].ToInt();
	  int r = sector_parts[1].ToInt();
	  int s = 0 - q - r;
	  _CreateSector(new Hex(q,r,s));
	}

  }

  void DrawStarFieldRing()
  {
	StarFieldRing.radius = StarFieldRadiusPixels;
	//StarFieldRing.Update();
	//3to4 ?
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
	  _serilogger.Debug($"Server.cs: Instantiating player {UUID}");
	  UpdateSectorMap();

	  // start with the center
	  Hex theSector = new Hex(0, 0, 0);

	  //Area2D playerShipThingInstance = (Area2D)PlayerShipThing.Instance();
	  //3to4
	  // C# has no preload, so you have to always use ResourceLoader.Load<PackedScene>().
	  // var player = ResourceLoader.Load<PackedScene>("res://Player.tscn").Instantiate();
	  Node shipThingsNode = PlayerPackedScene.Instantiate();
	  PlayerShip newPlayer = shipThingsNode.GetNode<PlayerShip>("PlayerShip");

	  newPlayer.uuid = UUID;

  	  // assign the configured values
	  newPlayer.Thrust = PlayerDefaultThrust;
	  newPlayer.MaxSpeed = PlayerDefaultMaxSpeed;
	  newPlayer.RotationThrust = PlayerDefaultRotationThrust;
	  newPlayer.HitPoints = PlayerDefaultHitPoints;
	  newPlayer.MissileSpeed = PlayerDefaultMissileSpeed;
	  newPlayer.MissileLife = PlayerDefaultMissileLife;
	  newPlayer.MissileDamage = PlayerDefaultMissileDamage;

	  _serilogger.Debug($"Server.cs: Adding {UUID} to playerObjects");
	  // playerObjects.Add(UUID, playerShipThingInstance);
	  //3to4
	  playerObjects.Add(UUID, newPlayer);
	  newPlayer.AddToGroup("player_ships");
	
	  _serilogger.Debug($"Server.cs: Current count of playerObjects: {playerObjects.Count}");
	
	  // if there are more than two players, it means we are now at the point
	  // where we have to start calculating ring things
	  if (playerObjects.Count > 2)
	  {
	
		  // if the ring radius is zero, and we have more than two players, we need
		  // to increase it, otherwise things will already blow up
		  if (RingRadius == 0)
		  {
			  _serilogger.Debug($"Server.cs: playerObjects count > 2 and ringradius == 0 so incrementing");
			  RingRadius++;
		  }
	
		  // it's possible that we have insufficient players in sector 0,0,0, so
		  // check that first for funzos
	
		  int qty;
		  if (sectorMap.TryGetValue("0,0", out qty))
		  {
			  if (qty < 2)
			  {
				  // do nothing since we already assigned the sector to use to 0,0,0
				  _serilogger.Debug($"Server.cs: Insufficient players in sector 0 so will add player there");
			  }
			  else
			  {
				  theSector = TraverseSectors();
			  }
		  }
	  }
	
	_serilogger.Debug($"Server.cs: Selected sector for player {UUID} is {theSector.q},{theSector.r}");
	
	// increment whatever sector this new player is going into
	string sector_key = theSector.q + "," + theSector.r;
	
	  if (sectorMap.ContainsKey(sector_key))
	  {
		  sectorMap[sector_key] += 1;
	  }
	  else
	  {
		  sectorMap[sector_key] = 1;
	  }
	
	  _serilogger.Debug($"Server.cs: Sector {theSector.q},{theSector.r} now has {sectorMap[sector_key]}");
	
	  // reset the starfield radius - should also move the center
	  _CalcStarFieldRadius();
	  DrawStarFieldRing();
	
	  // now that the sector to insert the player has been selected, find its
	  // pixel center
	  Point theSectorCenter = HexLayout.HexToPixel(theSector);
	  _serilogger.Debug($"Server.cs: Center of selected sector: {theSectorCenter.x},{theSectorCenter.y}");
	
	// TODO: need to ensure players are not on top of one another for real.  we
	// will spawn two players into a sector to start, so we should check if
	// there's already a player in the sector first. if there is, we should
	// place the new player equidistant from the already present player
	
	// badly randomize start position
	int theMin = (int)(SectorSize * 0.3);
	int xOffset = rnd.Next(-1 * theMin, theMin);
	int yOffset = rnd.Next(-1 * theMin, theMin);
	int finalX = (Int32)theSectorCenter.x + xOffset;
	int finalY = (Int32)theSectorCenter.y + yOffset;
	_serilogger.Debug($"Server.cs: Placing player at {finalX},{finalY}");
	
	//   // playerShipThingInstance.GlobalPosition =
	//   //3to4
	newPlayer.GlobalPosition = new Vector2(x: (Int32)theSectorCenter.x + xOffset, y: (Int32)theSectorCenter.y + yOffset);
	
	// connect the ship thing input signal so that we can catch when a ship was clicked in the debug UI
	//3to4
	newPlayer.Connect("input_event", new Callable(this, "_on_ShipThings_input_event"), 0); // no GodotObject.ConnectFlags?
	
	// Players.AddChild(playerShipThingInstance);
	//3to4
	Players.AddChild(shipThingsNode);
	_serilogger.Information($"Server.cs: Added player {UUID} instance!");
	
	// create the protobuf for the player joining
	GameEvent egeb = newPlayer.CreatePlayerGameEventBuffer(GameEvent.GameEventType.GameEventTypeCreate);
	
	// send the player create event message
	MessageInterface.SendGameEvent(egeb);
	DrawSectorMap();
  }

  void ProcessMoveCommand(Command cb)
  {
	//GD.Print("Server::ProcessMoveCommand for cb.Uuid " + cb.Uuid);
	_serilogger.Verbose("Server.cs: Processing move command!");

	String uuid = cb.Uuid;
	Node2D playerRoot;
	if (false == playerObjects.TryGetValue(uuid, out playerRoot))
	{
		//GD.Print("Server::ProcessMoveCommand failed to get playerRoot from playerObjects!");
		return;
	}
	//3to4
	// PlayerShip movePlayer = playerRoot.GetNode<PlayerShip>("PlayerShip");
	PlayerShip movePlayer = (PlayerShip)playerRoot;
	// process thrust and rotation
	Vector2 thrust = new Vector2(cb.InputX, cb.InputY);
	// push the thrust input onto the player's array
	movePlayer.MovementQueue.Enqueue(thrust);
	//GD.Print("Server::ProcessMoveCommand movePlayer.MovementQueue.Count is " + movePlayer.MovementQueue.Count);
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
		//GD.Print("Server::ProcessGameEvents::GameEventQueue.Count > 0");
	  Command commandBuffer = GameEventQueue.Dequeue();
	  switch (commandBuffer.command_type)
	  {
		case Command.CommandType.CommandTypeMove:
			//GD.Print("Server::ProcessGameEvents::CommandTypeMove");
		  _serilogger.Verbose("Server.cs: Move command received");
		  ProcessMoveCommand(commandBuffer);
		  break;
		case Command.CommandType.CommandTypeShoot:
			//GD.Print("Server::ProcessGameEvents::CommandTypeShoot");
		  _serilogger.Verbose("Server.cs: Shoot command received");
		  ProcessShootCommand(commandBuffer);
		  break;
		case Command.CommandType.CommandTypeUnspecified:
			//GD.Print("Server::ProcessGameEvents::CommandTypeUnspecified");
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
	  //GD.Print("Server::ProcessInputEvent");
	// if there is no player in the dictionary, do nothing
	// this catches accidental keyboard hits
	if (!playerObjects.ContainsKey(playerID.Text)) { return; }

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
		//GD.Print("Server::ProcessInputEvent velocity.Length() > 0");
	  _serilogger.Verbose("Server.cs: velocity length is greater than zero - move");
	  Command cb = new Command();
	  cb.command_type = Command.CommandType.CommandTypeMove;
	  cb.Uuid = playerID.Text;

	  cb.InputX = (int)velocity.X;
	  cb.InputY = (int)velocity.Y;
	  MessageInterface.SendCommand(cb);
	}

	if (shoot.Length() > 0)
	{
		//GD.Print("Server::ProcessInputEvent shoot.Length() > 0");
	  _serilogger.Verbose("Server.cs: shoot length is greater than zero - shoot");
	  Command cb = new Command();
	  cb.command_type = Command.CommandType.CommandTypeShoot;
	  cb.Uuid = playerID.Text;

	  cb.InputX = (int)shoot.X;
	  cb.InputY = (int)shoot.Y;
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
	// clear the list and then iterate over all players to rebuild it
	uiPlayerTree.Clear();
	TreeItem UIPlayerTreeRoot = uiPlayerTree.CreateItem();

	Godot.Collections.Array<Node> players = _GetNodesFromGroup("player_ships");
	foreach (PlayerShip player in players)
	{
	  TreeItem playerTreeItem = uiPlayerTree.CreateItem(UIPlayerTreeRoot);
	  playerTreeItem.SetText(0, player.uuid);
	}

	RingSize.Text = RingRadius.ToString();
	StarFieldRadiusSize.Text = StarFieldRadiusPixels.ToString();
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
	MessageInterface.Name = "AMQP_Server";
	AddChild(MessageInterface);

	_serilogger.Information("Server.cs: Beginning game server");

	LoadConfig();

	// initialize the starfield size to the initial sector size
	// the play area is clamped 
	StarFieldRadiusPixels = SectorSize;

	// initialize the hexboard layout
	HexLayout = new Layout(Layout.flat, new Point(SectorSize, SectorSize), new Point(0, 0));

	// grab needed elements for later
	rtsCamera = GetNode<Camera2D>("RTS-Camera2D");
	theCanvas = GetNode<VBoxContainer>("DebugUI/DebugStack");
	playerID = theCanvas.GetNode<LineEdit>("PlayerID");
	uiPlayerTree = theCanvas.GetNode<Tree>("CurrentPlayerTree");
	RingSize = theCanvas.GetNode<Label>("RingSizeContainer/RingSize");
	StarFieldRadiusSize = theCanvas.GetNode<Label>("StarfieldSizeContainer/StarfieldRadiusSize");
	SectorMap = GetNode("SectorMap");
	Players = GetNode("Players");
	StarFieldRing = GetNode<StarFieldRadius>("StarFieldRadius");
	DrawStarFieldRing();
  }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(double delta)
  {
	// //GD.Print("Server_Process()");
	ProcessSecurityEvents();
	ProcessGameEvents();
	ProcessPlayerRemoval();

	// look for any inputs, subsequently sent a control message
	var velocity = Vector2.Zero; // The player's movement direction.
	var shoot = Vector2.Zero; // the player's shoot status

	if (Input.IsActionPressed("rotate_right"))
	{
		//GD.Print("rotate_right");
	  velocity.X += 1;
	}

	if (Input.IsActionPressed("rotate_left"))
	{
		//GD.Print("rotate_left");
	  velocity.X -= 1;
	}

	if (Input.IsActionPressed("thrust_forward"))
	{
		//GD.Print("thrust_forward");
	  velocity.Y += 1;
	}

	if (Input.IsActionPressed("thrust_reverse"))
	{
		//GD.Print("thrust_reverse");
	  velocity.Y -= 1;
	}

	if (Input.IsActionPressed("fire"))
	{
		//GD.Print("fire");
	  shoot.Y = 1;
	}

	if ((velocity.Length() > 0) || (shoot.Length() > 0))
	{
	  // TODO: this should probably be putting things on the queue instead
	  //GD.Print("Server::_Process velocity.Length() > 0 || shoot.Length() > 0");
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
  void _on_FocusAPlayer_pressed()
  {
	// loosely based on: https://godotengine.org/qa/116981/object-follow-mouse-in-radius
	// get the UUID of the text box and set that ship's camera to active
	if (playerObjects.ContainsKey(playerID.Text))
	{
	  Node2D playerForCamera = playerObjects[playerID.Text];
	  Camera2D playerCamera = playerForCamera.GetNode<Camera2D>("PlayerShip/Camera2D");
	  if (!playerCamera.IsCurrent()) 
	  { 
		// clear the RTS camera's current
		//3to4
		// rtsCamera.ClearCurrent();
		playerCamera.MakeCurrent(); 
		playerCamera.GetParent<PlayerShip>().isFocused = true;
		currentCamera = playerCamera;
	  }
	}
  }
  
  void _on_UnFocusAPlayer_pressed()
  {
	//currentCamera.ClearCurrent();
	//3to4
	currentCamera.GetParent<PlayerShip>().isFocused = false;
	currentCamera = null;
	rtsCamera.MakeCurrent();
  }

  // TODO: improve via signal connection passing in the tree as an arg
  void _on_CurrentPlayerTree_item_selected()
  {
	// figure out which tree item was selected
	_serilogger.Debug($"Server.cs: Handling debug UI tree clicked");
	TreeItem selected = uiPlayerTree.GetSelected();
	_serilogger.Debug($"Server.cs: Tree item selected: {selected.GetText(0)}");

	// update the debug UI text field to match the selected item
	// this causes the debug UI to re-focus on the selected item
	LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");
	textField.Text = selected.GetText(0);
  }

  // TODO: should move debug to its own scene that's optionally loaded
  void _on_JoinAPlayer_pressed()
  {
	_serilogger.Debug($"Server.cs: Join button pressed for UUID: {playerID.Text}");

	// don't do anything if this UUID already exists
	if (playerObjects.ContainsKey(playerID.Text))
	{
	  _serilogger.Debug($"Server.cs: UUID already exists, doing nothing: {playerID.Text}");
	  return;
	}

	_serilogger.Debug($"Server.cs: Sending join with UUID: {playerID.Text}");

	// construct a join message from the text in the debug field
	//SecurityCommandBuffer scb = new SecurityCommandBuffer();
	Security scb = new Security();

	scb.Uuid = playerID.Text;
	scb.security_type = Security.SecurityType.SecurityTypeJoin;

	MessageInterface.SendSecurityDebug(scb);
  }

  void _on_DeleteAPlayer_pressed()
  {
	_serilogger.Debug($"Server.cs: Delete button pressed for UUID: {playerID.Text}");

	// check if the playerobject dictionary has an entry for the uuid in the textfield
	Node2D selectedPlayerNode2D;
	if (playerObjects.TryGetValue(playerID.Text, out selectedPlayerNode2D))
	{
	  // it does, so remove that player
	  _serilogger.Debug($"Server.cs: Removing player with UUID: {playerID.Text}");
	  RemovePlayer(playerID.Text);
	}
  }

  void _on_AddRandomPlayer_pressed()
  {
	_serilogger.Debug($"Server.cs: Add random button pressed");
	string newPlayerUUID = System.Guid.NewGuid().ToString().Remove(0, 29);
	_serilogger.Information($"Server.cs: Add random player {newPlayerUUID}");
	Security scb = new Security();

	scb.Uuid = newPlayerUUID;
	scb.security_type = Security.SecurityType.SecurityTypeJoin;

	MessageInterface.SendSecurityDebug(scb);
  }

  void _on_ShipThings_input_event(Node viewport, InputEvent theEvent, int shape_idx, PlayerShip theClickedPlayer)
  {
	if (theEvent.IsActionPressed("left_click"))
	{
	  _serilogger.Debug($"Server.cs: player {theClickedPlayer.uuid} clicked - making current");
	  Camera2D playerCamera = theClickedPlayer.GetNode<Camera2D>("Camera2D");
	  if (playerCamera.IsCurrent())
	  {
		_serilogger.Debug($"Server.cs: player {theClickedPlayer.uuid} camera already current, skipping");
		return;
	  }
	  else
	  {
		// remove the current rts camera
		//rtsCamera.ClearCurrent();
		//3to4

		// make the player's camera current
		playerCamera.MakeCurrent();

		// store which the current camera is
		currentCamera = playerCamera;

		// set the text field to the player's ID so that we can use the unfocus button later
		playerID.Text = theClickedPlayer.uuid;

		// set the player as focused
		playerCamera.GetParent<PlayerShip>().isFocused = true;
	  }
	}
  }

  public override void _UnhandledInput(InputEvent @event)
  {

	//// hop out if we don't have a player to zoom in on
	//CanvasLayer theCanvas = GetNode<CanvasLayer>("DebugUI");
	//LineEdit textField = theCanvas.GetNode<LineEdit>("PlayerID");
	//if (!playerObjects.ContainsKey(textField.Text)) { return; }

	//// grab the camera and zoom it by zoom factor
	//Node2D playerForCamera = playerObjects[textField.Text];
	//Camera2D playerCamera = playerForCamera.GetNode<Camera2D>("PlayerShip/Camera2D");

	//if (@event.IsActionPressed("zoom_in"))
	//{
	//  _serilogger.Verbose("Server.cs: zoom viewport in!");
	//  float zoomN = CameraCurrentZoom.x - CameraZoomStepSize;
	//  zoomN = Mathf.Clamp(zoomN, CameraMaxZoom, CameraMinZoom);
	//  CameraCurrentZoom.x = zoomN;
	//  CameraCurrentZoom.y = zoomN;
	//  playerCamera.Zoom = CameraCurrentZoom;
	//  _serilogger.Verbose($"Server.cs: Zoom Level: {CameraCurrentZoom.x}, {CameraCurrentZoom.y}");
	//}

	//if (@event.IsActionPressed("zoom_out"))
	//{
	//  _serilogger.Verbose("Server.cs zoom viewport out!");
	//  float zoomN = CameraCurrentZoom.x + CameraZoomStepSize;
	//  zoomN = Mathf.Clamp(zoomN, CameraMaxZoom, CameraMinZoom);
	//  CameraCurrentZoom.x = zoomN;
	//  CameraCurrentZoom.y = zoomN;
	//  playerCamera.Zoom = CameraCurrentZoom;
	//  _serilogger.Verbose($"Server.cs: Zoom Level: {CameraCurrentZoom.x}, {CameraCurrentZoom.y}");
	//}
  }

  // helper functions
  Godot.Collections.Array<Node> _GetNodesFromGroup(string groupName)
  {
	  //return GetTree().GetNodesInGroup(groupName);
	  //3to4
	  Godot.Collections.Array<Node> gca = GetTree().GetNodesInGroup(groupName);
	  return gca;
  }

  void _CreateSector(Hex theSectorHex)
  {
	_serilogger.Debug($"Server.cs: Creating sector node at {theSectorHex.q},{theSectorHex.r}");

	// Sector newSector = aSector.Instance<Sector>();
	//3to4
	Node sectorNode = SectorPackedScene.Instantiate();
	Polygon2D newSector = sectorNode.GetNode<Polygon2D>("SectorPolygon");

	// scale by the sector size given the width of the hex polygon is 50px
	// HexRatio = (SectorSize / 50) * 2;
	// newSector.ApplyScale(new Vector2(HexRatio, HexRatio));
	// newSector.SectorLabel = theSectorHex.q.ToString() + "," + theSectorHex.r.ToString();
	// Point sector_center = HexLayout.HexToPixel(theSectorHex);
	// newSector.Position = new Vector2((float)sector_center.x, (float)sector_center.y);
	// newSector.AddToGroup("sectors");
	// SectorMap.AddChild(newSector);
	
	HexRatio = (SectorSize / 50) * 2;
	newSector.ApplyScale(new Vector2(HexRatio, HexRatio));
	//3to4
	// newSector.SectorLabel = theSectorHex.q.ToString() + "," + theSectorHex.r.ToString();
	Label sectorLabel = sectorNode.GetNode<Label>("SectorLabel");
	var sectorLabelText = theSectorHex.q.ToString() + "," + theSectorHex.r.ToString();
	// //GD.Print("sectorLabelText is ", sectorLabelText);
	sectorLabel.Text = sectorLabelText;//theSectorHex.q.ToString() + "," + theSectorHex.r.ToString();
	Point sector_center = HexLayout.HexToPixel(theSectorHex);
	newSector.Position = new Vector2((float)sector_center.x, (float)sector_center.y);
	newSector.AddToGroup("sectors");
	//3to4
	// SectorMap.AddChild(newSector);
	SectorMap.AddChild(sectorNode);
  }

  void _CalcStarFieldRadius()
  {
	// if the ring radius is odd, we need to multiply by 3 instead of 2
	// because of the size of hexes
	int offsetPixels = RingRadius % 2;
	if (RingRadius == 0)
	{
	  StarFieldRadiusPixels = SectorSize;
	}
	else
	{
	  StarFieldRadiusPixels = (Int32) ((SectorSize / 2) * Mathf.Sqrt(3) * (RingRadius * 2 + 1));
	}
	_serilogger.Debug($"Server.cs: New starfield radius is: {StarFieldRadiusPixels}");
  }

}
