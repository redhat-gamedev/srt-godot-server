using Godot;
using System;
using System.Collections;

public class Player : KinematicBody2D
{
  [Export]
  public float Thrust = 1f; // effective acceleration

  [Export]
  public float MaxSpeed = 5;
  
  [Export]
  public float CurrentVelocity = 0;

  [Export]
  public float RotationThrust = 1.5f;

  [Export]
  public float CurrentRotation = 0;

  [Export]
  public int HitPoints = 100;

  public Queue MovementQueue = new Queue();

  public String uuid;

  CSLogger cslogger;

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    // initialize the logging configuration
    Node gdlogger = GetNode<Node>("/root/GDLogger");
    gdlogger.Call("load_config", "res://logger.cfg");
    cslogger = GetNode<CSLogger>("/root/CSLogger");

    Label playerIDLabel = (Label)GetNode("IDLabel");

    // TODO: deal with really long UUIDs
    playerIDLabel.Text = uuid;
  }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _PhysicsProcess(float delta)
  {
    Label angularVelocityLabel = (Label)GetNode("AngularVelocity");
    Label linearVelocityLabel = (Label)GetNode("LinearVelocity");

    angularVelocityLabel.Text = $"Ang: {CurrentRotation}";
    linearVelocityLabel.Text = $"Lin: {CurrentVelocity}";

    float rotation_dir = 0; // in case we need it

    //cslogger.Debug($"{uuid}: handling physics");
    if (MovementQueue.Count > 0)
    {
      Vector2 thisMovement = (Vector2)MovementQueue.Dequeue();
      cslogger.Debug($"UUID: {uuid} X: {thisMovement.x} Y: {thisMovement.y}");

      if (thisMovement.y > 0)
      {
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, MaxSpeed, Thrust * delta);
      }

      if (thisMovement.y < 0)
      {
        // TODO: when the velocity gets sufficiently low, just peg it to zero
        // otherwise you approach zero very slowly
        CurrentVelocity = Mathf.Lerp(CurrentVelocity, 0, Thrust * delta);

        if (CurrentVelocity < 0.5) { CurrentVelocity = 0; }
      }

      if (thisMovement.x != 0)
      {
        rotation_dir = thisMovement.x;
      }

      cslogger.Debug($"UUID: {uuid} Velocity: {CurrentVelocity}");

    }
    Vector2 velocity =  -(Transform.y * CurrentVelocity);
    //cslogger.Debug($"UUID: {uuid} Vector X: {velocity.x} Y: {velocity.y} ");
    Rotation += rotation_dir * RotationThrust * delta;
    MoveAndCollide(velocity);
  }

}
