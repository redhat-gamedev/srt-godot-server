using Godot;
using System;

public class SpaceMissile : Area2D
{
  CSLogger cslogger;

  public float MissileLife;

  public int MissileSpeed;

  public int MissileDamage;

  public Player MyPlayer;

  [Signal]
  public delegate void Hit(Player HitPlayer);

  // Called when the node enters the scene tree for the first time.
  public override void _Ready() 
  {  
    // initialize the logging configuration
    Node gdlogger = GetNode<Node>("/root/GDLogger");
    gdlogger.Call("load_config", "res://logger.cfg");
    cslogger = GetNode<CSLogger>("/root/CSLogger");

    // connect the hit signal to handling the hit
    Connect(nameof(Hit), this, "_HandleHit");
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
    cslogger.Debug("Body entered!");
    EmitSignal("Hit", (Player)body);
    // Must be deferred as we can't change physics properties on a physics callback.
    GetNode<CollisionShape2D>("CollisionShape2D").SetDeferred("disabled", true);
  }

  void _HandleHit(Player HitPlayer)
  {
    cslogger.Debug("Evaluating hit!");
    QueueFree();
    MyPlayer.ExpireMissile();
    HitPlayer.TakeDamage(MissileDamage);
  }
}
