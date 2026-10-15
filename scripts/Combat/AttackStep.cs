namespace ClownSquad.Combat;

/// <summary>One swing in a combo. Timings are in seconds.</summary>
public readonly record struct AttackStep(
    float Startup, float Active, float Recovery,
    float Damage, float Knockback, float Hitstop,
    float Range, float Launch);
