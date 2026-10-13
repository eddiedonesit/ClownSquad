namespace ClownSquad.Combat;

using Godot;

/// <summary>One instance of damage, carrying the feel parameters with it.</summary>
public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly Vector3 KnockbackDirection;
    public readonly float KnockbackForce;
    /// <summary>Seconds both parties freeze on connect. The main feel lever.</summary>
    public readonly float Hitstop;
    public readonly Node3D? Source;

    public DamageInfo(float amount, Vector3 knockbackDirection, float knockbackForce,
                      float hitstop, Node3D? source)
    {
        Amount = amount;
        KnockbackDirection = knockbackDirection;
        KnockbackForce = knockbackForce;
        Hitstop = hitstop;
        Source = source;
    }
}
