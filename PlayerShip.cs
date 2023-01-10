using Godot;
using System;
using System.Collections;
using redhatgamedev.srt.v1;

public class PlayerShip : KinematicBody2D
{
  public Serilog.Core.Logger _serilogger;

  public float Thrust = 1f; // effective acceleration

  public float MaxSpeed = 5;

  public float StopThreshold = 10f;

  public float GoThreshold = 90f;
  
  public float CurrentVelocity = 0;

  public float RotationThrust = 1.5f;

  public float CurrentRotation = 0;

  public int HitPoints = 100;

  public Queue MovementQueue = new Queue();

  public String uuid;

  // for the server we're interfaced with
  Server MyServer;

  // for now only one missile at a time
  SpaceMissile MyMissile = null;

  public int MissileSpeed = 300;
  
  public float MissileLife = 2;

  // the reload time is the minimum time between missile firings
  // relevant when two players are very close to one another and
  // prevents missile spamming
  float MissileReloadTime = 2;
  float MissileReloadCountdown;
  bool MissileReady = true;

  public int MissileDamage = 25;

  Node2D shipThing = null;

  PackedScene MissileScene = (PackedScene)ResourceLoader.Load("res://SpaceMissile.tscn");

  public GameEvent CreatePlayerGameEventBuffer(GameEvent.GameEventType eventType)
  {
    //EntityGameEventBuffer egeb = new EntityGameEventBuffer();
    GameEvent gameEvent = new GameEvent();

    //egeb.Type = BufferType;
    gameEvent.game_event_type = eventType;

    //egeb.objectType = EntityGameEventBuffer.EntityGameEventBufferObjectType.Player;
    gameEvent.game_object_type = GameEvent.GameObjectType.GameObjectTypePlayer;

    //egeb.Uuid = uuid;
    gameEvent.Uuid = uuid;

    //Box2d.PbBody body = new Box2d.PbBody();
    //body.Type = Box2d.PbBodyType.Kinematic; // not sure if this should maybe be static

    // need to use the GlobalPosition because the ship node ends up being offset
    // from the parent Node2D
    //body.Position = new Box2d.PbVec2 
    //  { 
    //    X = GlobalPosition.x,
    //    Y = GlobalPosition.y
    //  };
    gameEvent.PositionX = (int)GlobalPosition.x;
    gameEvent.PositionY = (int)GlobalPosition.y;

    //body.Angle = RotationDegrees;
    gameEvent.Angle = RotationDegrees;

    //body.AbsoluteVelocity = CurrentVelocity;
    gameEvent.AbsoluteVelocity = CurrentVelocity;
    
    gameEvent.HitPoints = HitPoints;

    return gameEvent;
  }

  public void ExpireMissile() 
  { 
    _serilogger.Verbose($"PlayerShip.cs: removing missile {MyMissile.uuid} belongs to {MyMissile.MyPlayer.uuid}");
    MyServer.RemoveMissile(MyMissile);
    MyMissile = null;
  }

  public void FireMissile(string missileUUID = null)
  {
    // only one missile allowed for now
    if (MyMissile != null) 
    { 
      _serilogger.Debug($"PlayerShip.cs: Missile for player {uuid} exists - skipping");
      return; 
    }

    // check if reload complete
    if (MissileReady == false)
    {
      _serilogger.Debug($"PlayerShip.cs: player {uuid} not done with reload - skipping");
      return;
    }

    MyMissile = (SpaceMissile)MissileScene.Instance();

    // TODO: need to check for UUID collision
    _serilogger.Debug($"PlayerShip.cs: Supplied UUID is {missileUUID}");
    if (missileUUID != null)
      // use the suggested UUID
      { MyMissile.uuid = missileUUID; }
    else
      { MyMissile.uuid = Guid.NewGuid().ToString(); }

    _serilogger.Debug($"PlayerShip.cs: Missile UUID is {MyMissile.uuid}");

    // missile should point in the same direction as the ship
    MyMissile.Rotation = Rotation;
    
    // TODO: need to offset this to the front of the ship
    // start at our position
    MyMissile.Position = GlobalPosition;

    // negative direction is "up"
    Vector2 offset = new Vector2(0, -60);

    // rotate the offset to match the current ship heading
    offset = offset.Rotated(Rotation);
    MyMissile.Position = MyMissile.Position + offset;

    // set missile's parameters based on current modifiers
    MyMissile.MissileSpeed = MissileSpeed;
    MyMissile.MissileLife = MissileLife;
    MyMissile.MissileDamage = MissileDamage;

    // this is a poop way to do this
    MyMissile.MyPlayer = this;

    // send the missile creation message
    _serilogger.Debug($"PlayerShip.cs: creating missile {MyMissile.uuid} belongs to {MyMissile.MyPlayer.uuid}");
    Node rootNode = GetNode<Node>("/root");
    rootNode.AddChild(MyMissile);
    MyServer.InstantiateMissile(MyMissile);

    // set the reload countdown
    MissileReloadCountdown = MissileReloadTime;
    MissileReady = false;
  }

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    // initialize the logging configuration
    MyServer = GetNode<Server>("/root/Server");
    _serilogger = MyServer._serilogger;

    Node2D shipThing = (Node2D)GetParent();
    Label playerIDLabel = (Label)shipThing.GetNode("Stat/IDLabel");

    // TODO: deal with really long UUIDs
    playerIDLabel.Text = uuid;
  }

  public void TakeDamage(int Damage)
  {
    _serilogger.Debug($"PlayerShip.cs: {uuid}: Taking damage: {Damage}");
    HitPoints -= Damage;
    _serilogger.Debug($"PlayerShip.cs: {uuid}: Hitpoints: {HitPoints}");
  }

  void RemovePlayer()
  {
    _serilogger.Verbose($"PlayerShip.cs: removing player: {uuid}");
    MyServer.RemovePlayer(uuid);
  }

  void CheckMissileReload(float delta)
  {
    // nothing to check if we are already reloaded
    if (MissileReady == true) { return; }

    MissileReloadCountdown -= delta;
    if (MissileReloadCountdown <= 0)
    { 
      _serilogger.Debug($"PlayerShip.cs: player {uuid} missile reload countdown complete");
      MissileReady = true;
    }
  }

  public override void _Process(float delta)
  {

    if (shipThing == null) shipThing = (Node2D)GetParent();

    // TODO: we are doing instant rotation so probably should rename this
    Label angularVelocityLabel = (Label)shipThing.GetNode("Stat/AngularVelocity");
    Label linearVelocityLabel = (Label)shipThing.GetNode("Stat/LinearVelocity");
    Label hitPointsLabel = (Label)shipThing.GetNode("Stat/HitPoints");
    Label positionLabel = (Label)shipThing.GetNode("Stat/Position");
    Label hexLabel = (Label)shipThing.GetNode("Stat/Hex");

    // figure out the hex from the pixel position
    Layout theLayout = MyServer.HexLayout;
    FractionalHex theHex = theLayout.PixelToHex(new Point(GlobalPosition.x, GlobalPosition.y));
    hexLabel.Text = $"q: {theHex.HexRound().q}, r: {theHex.HexRound().r}, s: {theHex.HexRound().s}";

    angularVelocityLabel.Text = $"Rot: {RotationDegrees}";
    linearVelocityLabel.Text = $"Vel: {CurrentVelocity}";
    hitPointsLabel.Text = $"HP: {HitPoints}";
    positionLabel.Text = $"X: {GlobalPosition.x} Y: {GlobalPosition.y}";
    if (HitPoints <= 0)
    {
      _serilogger.Debug("Hitpoints zeroed! Remove the player!");
      RemovePlayer();
    }

    CheckMissileReload(delta);
  }
  public override void _PhysicsProcess(float delta)
  {
    if (shipThing == null) shipThing = (Node2D)GetParent();

    // somewhat based on: https://kidscancode.org/godot_recipes/2d/topdown_movement/
    // "rotate and move" / asteroids-style-ish

    float rotation_dir = 0; // in case we need it

    _serilogger.Verbose($"{uuid}: handling physics");
    if (MovementQueue.Count > 0)
    {
      Vector2 thisMovement = (Vector2)MovementQueue.Dequeue();
      _serilogger.Verbose($"UUID: {uuid} X: {thisMovement.x} Y: {thisMovement.y}");

      if (thisMovement.y > 0)
      {
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, MaxSpeed, Thrust * delta);

        // max out speed when velocity gets above threshold for same reason
        if (CurrentVelocity > MaxSpeed * (GoThreshold/100)) { CurrentVelocity = MaxSpeed; }
      }

      if (thisMovement.y < 0)
      {
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, 0, Thrust * delta);

        // cut speed when velocity gets below threshold, otherwise LERPing
        // results in never actually stopping. 
        if (CurrentVelocity < MaxSpeed * (StopThreshold/100)) { CurrentVelocity = 0; }
      }

      if (thisMovement.x != 0)
      {
        rotation_dir = thisMovement.x;
      }

      _serilogger.Verbose($"UUID: {uuid} Velocity: {CurrentVelocity}");

    }
    Vector2 velocity =  -(Transform.y * CurrentVelocity);
    _serilogger.Verbose($"UUID: {uuid} Vector X: {velocity.x} Y: {velocity.y} ");
    Rotation += rotation_dir * RotationThrust * delta;

    // TODO: implement collision mechanics
    MoveAndCollide(velocity);

    // TODO: need to adust the clamp when the starfield is lopsided in the early
    // game

    // clamp the player to the starfield radius
    Int32 starFieldRadiusPixels = MyServer.StarFieldRadiusPixels;
    Vector2 currentGlobalPosition = GlobalPosition;
    if (currentGlobalPosition.Length() > starFieldRadiusPixels)
    {
      Vector2 newPosition = starFieldRadiusPixels * currentGlobalPosition.Normalized();
      GlobalPosition = newPosition;
    }
  }

}
