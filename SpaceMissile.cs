using Godot;
using System;

public class SpaceMissile : Area2D
{

  [Export]
  public float MissileLife;

  [Export]
  public int MissileSpeed;

  public Player MyPlayer;

  // Called when the node enters the scene tree for the first time.
  public override void _Ready() {  }

  public override void _PhysicsProcess(float delta)
  {
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

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //public override void _PhysicsProcess(float delta)
  //{
  //}
}
