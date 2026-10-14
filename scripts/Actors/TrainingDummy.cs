namespace ClownSquad.Actors;

using Godot;

/// <summary>
/// Stationary target that recovers, so combat feel can be tested in a loop. It
/// is light enough to launch, which makes it the thing to practise juggles and
/// wall bounces on.
/// </summary>
public partial class TrainingDummy : Fighter
{
    // Hostile so players can still hit it once friendly fire is disabled.
    public override bool Hostile => true;

    private float _recoverDelay = 1.5f;

    public override void _Ready()
    {
        DisplayName = "PRACTICE DUMMY";
        MaxHealth = 200f;
        BaseTint = new Color(0.65f, 0.8f, 1.0f);
        base._Ready();
    }

    protected override void Tick(float dt)
    {
        ApplyGroundMovement(Vector3.Zero, dt);
        if (!InHitAnim) PlayClip("idle");
    }

    /// <summary>Runs even while floored or dead, which is when recovery matters.</summary>
    protected override void TickAlways(float dt)
    {
        if (!IsDead)
        {
            _recoverDelay = 1.5f;
            return;
        }

        _recoverDelay -= dt;
        if (_recoverDelay <= 0f) Revive(GlobalPosition with { Y = 0f });
    }
}
