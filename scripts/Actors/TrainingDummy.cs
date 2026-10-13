namespace ClownSquad.Actors;

using Godot;

/// <summary>Stationary target that recovers, so combat feel can be tested in a loop.</summary>
public partial class TrainingDummy : Fighter
{
    private float _recoverDelay;

    public override void _Ready()
    {
        MaxHealth = 200f;
        base._Ready();
        Sprite.Modulate = new Color(0.65f, 0.8f, 1.0f);
    }

    protected override void Tick(float dt)
    {
        ApplyGroundMovement(Vector3.Zero, dt);

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
