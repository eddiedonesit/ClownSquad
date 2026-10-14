namespace ClownSquad.Actors;

using Godot;

/// <summary>Stationary target that recovers, so combat feel can be tested in a loop.</summary>
public partial class TrainingDummy : Fighter
{
    // Hostile so players can still hit it once friendly fire is disabled.
    public override bool Hostile => true;

    private float _recoverDelay;

    public override void _Ready()
    {
        MaxHealth = 200f;
        BaseTint = new Color(0.65f, 0.8f, 1.0f);
        base._Ready();
    }

    protected override void Tick(float dt)
    {
        ApplyGroundMovement(Vector3.Zero, dt);
        if (!InHitAnim) PlayClip("idle");

        if (IsDead)
        {
            _recoverDelay -= dt;
            if (_recoverDelay <= 0f)
            {
                Heal(MaxHealth);
                _recoverDelay = 0f;
            }
        }
        else
        {
            _recoverDelay = 1.5f;
        }
    }
}
