using Godot;
using System;

public partial class Sector : Node2D
{
  public string SectorLabel = "x,x";

  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    GetNode<Label>("SectorLabel").Text = SectorLabel;
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
