using Godot;
using System;

public class StarFieldRadius : Node2D
{

  public Int32 radius;

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {

  }

  public override void _Draw()
  {
    DrawArc(new Vector2(0,0), (float)radius, 0, Mathf.Tau, 50, Colors.AliceBlue);
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
