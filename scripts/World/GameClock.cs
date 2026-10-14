namespace ClownSquad.World;

/// <summary>
/// Game time, advanced once per physics frame. Cooldowns and leases read this
/// rather than the wall clock, so they respect slow motion, pausing, and tests
/// that run the simulation faster than real time.
/// </summary>
public static class GameClock
{
    /// <summary>Overall game pace. 0.9 runs everything 10% slower than authored.</summary>
    public const double Speed = 0.9;

    public static double Seconds { get; private set; }
    public static ulong Ms => (ulong)(Seconds * 1000.0);

    public static void Advance(double delta) => Seconds += delta;
    public static void Reset() => Seconds = 0.0;
}
