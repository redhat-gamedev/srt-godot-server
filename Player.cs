using Godot;
using System;

public class Player : Area2D
{
    [Export]
    public int Speed = 400; // How fast the player will move (pixels/sec).

    [Export]
    public int HitPoints = 100;

    public Vector2 ScreenSize; // Size of the game window.

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
      ScreenSize = GetViewportRect().Size;

      // badly randomize start position
      int minX = (int)(ScreenSize.x / 2 * 0.3);
      int minY = (int)(ScreenSize.y / 2 * 0.3);

      Random rnd = new Random();
      int xOffset = rnd.Next(0, minX * 2);
      int yOffset = rnd.Next(0, minY * 2);

      this.Position = new Vector2(x: minX + xOffset,
                                  y: minY + yOffset);
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
