namespace ClownSquad.Actors;

using Godot;

/// <summary>
/// The first enemy: a big, fat, grinning clown. Reuses the input-driven fighter
/// wholesale and simply swaps the sheet, scale, stats and faction, so AI-driven
/// and player-driven fighters share one combat implementation.
/// </summary>
public partial class BossClown : PlayerFighter
{
    protected override string SheetPath => "res://assets/sprites/boss_clown.png";
    protected override string FramesPath => "res://assets/sprites/boss_clown.frames.json";

    // Authored at the same 64px height as the player but rendered larger, so it
    // towers over them without needing a taller canvas.
    protected override float SpritePixelSize => 0.034f;

    public override bool Hostile => true;

    public override void _Ready()
    {
        MaxHealth = 320f;
        MoveSpeed = 2.9f;      // slow and inevitable rather than quick
        Acceleration = 22f;
        Friction = 26f;
        base._Ready();
    }
}
