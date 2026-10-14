namespace ClownSquad.Actors;

/// <summary>
/// Rank-and-file enemy: small, weak, bare-handed, and light enough to launch,
/// juggle and bounce off the screen edge. Exists to be fought in numbers.
/// </summary>
public partial class GruntClown : PlayerFighter
{
    protected override string SheetPath => "res://assets/sprites/grunt_clown.png";
    protected override string FramesPath => "res://assets/sprites/grunt_clown.frames.json";
    protected override float SpritePixelSize => 0.0205f;

    public override bool Hostile => true;
    public override float BodyHeight => 1.35f;

    protected override bool HasMalletMoves => false;
    // A punch reaches less far than a mallet.
    protected override float ReachScale => 0.8f;

    public override void _Ready()
    {
        MaxHealth = 45f;
        MoveSpeed = 3.3f;
        DamageMultiplier = 0.6f;
        base._Ready();
    }
}
