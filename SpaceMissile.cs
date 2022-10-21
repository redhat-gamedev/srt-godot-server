using Godot;
using System;
using redhatgamedev.srt.v1;

public class SpaceMissile : Area2D
{
  // for the server we're interfaced with
  Server MyServer;

  public Serilog.Core.Logger _serilogger;

  public float MissileLife;

  public int MissileSpeed;

  public int MissileDamage;

  public PlayerShip MyPlayer;

  public String uuid;

  [Signal]
  public delegate void Hit(PlayerShip HitPlayer);

  public GameEvent CreateMissileGameEventBuffer(GameEvent.GameEventType BufferType, String OwnerUUID)
  {
    GameEvent gameEvent = new GameEvent();
    //egeb.Type = BufferType;
    gameEvent.game_event_type = BufferType;

    //egeb.objectType = EntityGameEventBuffer.EntityGameEventBufferObjectType.Missile;
    gameEvent.game_object_type = GameEvent.GameObjectType.GameObjectTypeMissile;

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
    gameEvent.AbsoluteVelocity = MissileSpeed;

    gameEvent.OwnerUuid = OwnerUUID;

    return gameEvent;
  }
  
  // Called when the node enters the scene tree for the first time.
  public override void _Ready() 
  {  
    // initialize the logging configuration
    MyServer = GetNode<Server>("/root/Server");
    _serilogger = MyServer._serilogger;

    // connect the hit signal to handling the hit
    Connect(nameof(Hit), this, "_HandleHit");

    // add the missile to the missiles group so that we can iterate over
    // the entire group and send updates later
    AddToGroup("missiles");
  }

  public override void _PhysicsProcess(float delta)
  {
    // TODO disable the collision shape until the missile is "away" from the ship

    // create a new vector and rotate it by the current heading of the missile
    // then move the missile in the direction of that vector
    Vector2 velocity = new Vector2(0, -1);
    velocity = velocity.Rotated(Rotation);
    velocity = velocity * MissileSpeed * delta;
    Position += velocity;

    // once the life reaches zero, remove the missile and don't forget
    // to expire it from the parent's perspective
    MissileLife -= delta;
    if (MissileLife <= 0) { 
      QueueFree(); 

      // there's got to be a better way
      MyPlayer.ExpireMissile();
    }
  }

  void _onSpaceMissileBodyEntered(Node body)
  {
    _serilogger.Debug("SpaceMissile.cs: Body entered!");

    if (body.GetType().Name != "PlayerShip")
    {
      // We didn't hit another player, so remove ourselves, expire the missile, and return
      // TODO: may want to decide to do something fancy here
      QueueFree();
      MyPlayer.ExpireMissile();
      return;
    }

    // We hit another Player, so proceed
    EmitSignal("Hit", (PlayerShip)body);

    // Must be deferred as we can't change physics properties on a physics callback.
    GetNode<CollisionShape2D>("CollisionShape2D").SetDeferred("disabled", true);
  }

  void _HandleHit(PlayerShip HitPlayer)
  {
    _serilogger.Debug("SpaceMissile.cs: Evaluating hit!");
    QueueFree();
    MyPlayer.ExpireMissile();
    HitPlayer.TakeDamage(MissileDamage);
  }
}
