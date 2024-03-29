using Godot;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using redhatgamedev.srt.v1;

public partial class PlayerShip : CharacterBody2D
{
  public Serilog.Core.Logger _serilogger;

  public double Thrust = 1f; // effective acceleration

  public double MaxSpeed = 5;

  public double StopThreshold = 10f;

  public double GoThreshold = 90f;
  
  public double CurrentVelocity = 0;

  public double RotationThrust = 1.5f;

  public double CurrentRotation = 0;

  public int HitPoints = 100;

  public ConcurrentQueue<Vector2> MovementQueue = new ConcurrentQueue<Vector2>();

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
  double MissileReloadCountdown;
  bool MissileReady = true;

  public int MissileDamage = 25;

  Node2D shipThing = null;

  CollisionShape2D clickBox = null;

  Layout theLayout;

  PackedScene MissilePackedScene = (PackedScene)ResourceLoader.Load("res://SpaceMissile.tscn");

  bool QueuedForRemoval = false; // used when this player is about to be removed from play

  bool cameraCurrent = false;
  Camera2D myCamera;

  Label angularVelocityLabel;
  Label linearVelocityLabel;
  Label hitPointsLabel;
  Label positionLabel;
  Label hexLabel;

  // used by the debug UI to show which player has the camera focus
  public bool isFocused = false;
  Sprite2D shipSprite;

  public GameEvent.GameObject CreatePlayerGameObjectBuffer()
  {
    GameEvent.GameObject gameObject = new GameEvent.GameObject();

    gameObject.GameObjectType = GameEvent.GameObjectType.GameObjectTypePlayer;

    gameObject.Uuid = uuid;

    // TODO: only send if changed?
    gameObject.PositionX = (int)GlobalPosition.X;
    gameObject.PositionY = (int)GlobalPosition.Y;

    // TODO: only send if changed?
    gameObject.Angle = RotationDegrees;

    // need to send the velocity because that's how the client shows the speedometer
    gameObject.AbsoluteVelocity = (float)CurrentVelocity;
    
    // TODO: only send this if it's a change from previous?
    gameObject.HitPoints = HitPoints;

    return gameObject;
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

    Node missileNode = MissilePackedScene.Instantiate();
    MyMissile = (SpaceMissile)missileNode;

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

    clickBox = shipThing.GetNode<CollisionShape2D>("ClickBox");

    // TODO: deal with really long UUIDs
    playerIDLabel.Text = uuid;

    myCamera = GetNode<Camera2D>("Camera2D");
    shipSprite = GetNode<Sprite2D>("Sprite2D");

    // TODO: we are doing instant rotation so probably should rename this
    angularVelocityLabel = (Label)shipThing.GetNode("Stat/AngularVelocity");
    linearVelocityLabel = (Label)shipThing.GetNode("Stat/LinearVelocity");
    hitPointsLabel = (Label)shipThing.GetNode("Stat/HitPoints");
    positionLabel = (Label)shipThing.GetNode("Stat/Position");
    hexLabel = (Label)shipThing.GetNode("Stat/Hex");

    theLayout = MyServer.HexLayout;
  }

  public void TakeDamage(int Damage)
  {
    _serilogger.Debug($"PlayerShip.cs: {uuid}: Taking damage: {Damage}");
    HitPoints -= Damage;
    _serilogger.Debug($"PlayerShip.cs: {uuid}: Hitpoints: {HitPoints}");

    if (HitPoints <= 0)
    {
      _serilogger.Debug($"PlayerShip.cs: Hitpoints zeroed for {uuid}! Remove the player!");
      QueuedForRemoval = true;
      RemovePlayer();
    }
  }

  void RemovePlayer()
  {
    _serilogger.Debug($"PlayerShip.cs: Enqueuing player removal: {uuid}");
    MyServer.PlayerRemoveQueue.Enqueue(uuid);
  }

  void CheckMissileReload(double delta)
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

  public void UpdateFocused()
  {
    if (isFocused) shipSprite.Modulate = new Color(4,4,4,1);
    else shipSprite.Modulate = new Color(1,1,1,1);
  }

  public override void _Process(double delta)
  {

    if (QueuedForRemoval) return;

    if (shipThing == null) shipThing = (Node2D)GetParent();

    // figure out the hex from the pixel position
    FractionalHex theHex = theLayout.PixelToHex(new Point(GlobalPosition.X, GlobalPosition.Y));
    hexLabel.Text = $"q: {theHex.HexRound().q}, r: {theHex.HexRound().r}, s: {theHex.HexRound().s}";

    angularVelocityLabel.Text = $"Rot: {RotationDegrees}";
    linearVelocityLabel.Text = $"Vel: {CurrentVelocity}";
    hitPointsLabel.Text = $"HP: {HitPoints}";
    positionLabel.Text = $"X: {GlobalPosition.X} Y: {GlobalPosition.Y}";

    // reposition the click box to be located where the ship thing is
    clickBox.Position = GlobalPosition;

    CheckMissileReload(delta);
    UpdateFocused();
  }
  public override void _PhysicsProcess(double delta)
  {
    if (shipThing == null) shipThing = (Node2D)GetParent();

    // somewhat based on: https://kidscancode.org/godot_recipes/2d/topdown_movement/
    // "rotate and move" / asteroids-style-ish

    double rotation_dir = 0; // in case we need it

    _serilogger.Verbose($"{uuid}: handling physics");
    Vector2 thisMovement = Vector2.Zero;
    if (MovementQueue.TryDequeue(out thisMovement))
    {
      _serilogger.Verbose($"UUID: {uuid} X: {thisMovement.X} Y: {thisMovement.Y}");

      if (thisMovement.Y > 0)
      {
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, MaxSpeed, Thrust * delta);

        // max out speed when velocity gets above threshold for same reason
        if (CurrentVelocity > MaxSpeed * (GoThreshold/100)) { CurrentVelocity = MaxSpeed; }
      }

      if (thisMovement.Y < 0)
      {
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, 0, Thrust * delta);

        // cut speed when velocity gets below threshold, otherwise LERPing
        // results in never actually stopping. 
        if (CurrentVelocity < MaxSpeed * (StopThreshold/100)) { CurrentVelocity = 0; }
      }

      if (thisMovement.X != 0)
      {
        rotation_dir = thisMovement.X;
      }

      _serilogger.Verbose($"UUID: {uuid} Velocity: {CurrentVelocity}");
    }
    
    Vector2 velocity =  -(Transform.Y * (float)CurrentVelocity);
    _serilogger.Verbose($"UUID: {uuid} Vector X: {velocity.X} Y: {velocity.Y} ");
    Rotation += (float)(rotation_dir * RotationThrust * delta);

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
