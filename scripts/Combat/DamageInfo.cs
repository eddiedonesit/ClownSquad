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
    /// <summary>Upward speed imparted. Above zero the target is launched and can be juggled.</summary>
    public readonly float Launch;
    /// <summary>Seconds the target cannot act. This is what lets a combo be a combo.</summary>
    public readonly float Stun;

    public DamageInfo(float amount, Vector3 knockbackDirection, float knockbackForce,
                      float hitstop, Node3D? source, float launch = 0f, float stun = 0.26f)
    {
        Amount = amount;
        KnockbackDirection = knockbackDirection;
        KnockbackForce = knockbackForce;
        Hitstop = hitstop;
        Source = source;
        Launch = launch;
        Stun = stun;
    }
}
