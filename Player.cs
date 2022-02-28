using Godot;
using System;

public class Player : RigidBody2D
{
    [Export]
    public int Thrust = 400; // How fast the player will move (pixels/sec).

    [Export]
    public int RotationThrust = 100;

    [Export]
    public int HitPoints = 100;

    public Vector2 ScreenSize; // Size of the game window.

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
