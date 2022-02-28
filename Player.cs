using Godot;
using System;

public class Player : RigidBody2D
{
  [Export]
  public int Thrust = 10000;

  [Export]
  public int RotationThrust = 100;

  [Export]
  public int HitPoints = 100;

  public Vector2 ScreenSize; // Size of the game window.

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
  }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {
    float angularVelocity = this.AngularVelocity;
    Vector2 linearVelocity = this.LinearVelocity;

    Label angularVelocityLabel = (Label)GetNode("AngularVelocity");
    Label linearVelocityLabel = (Label)GetNode("LinearVelocity");

    angularVelocityLabel.Text = $"{angularVelocity}";
    linearVelocityLabel.Text = $"{linearVelocity.x} : {linearVelocity.y}";
  }
}
