namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Caps how many enemies may be attacking at once. Without it a crowd dogpiles
/// and the player has no readable opening; with it some enemies engage while
/// the rest circle and threaten, which is the rhythm the genre runs on.
///
/// Tokens are time-leased rather than explicitly released, so an enemy that is
/// stunned, killed or freed mid-attack can never strand one.
/// </summary>
public static class AttackTokens
{
    /// <summary>Simultaneous attackers allowed. Raising this is a difficulty lever
    /// that needs no change to enemy stats.</summary>
    public static int Limit { get; set; } = 2;

    private static readonly Dictionary<ulong, ulong> Leases = new();   // instance id -> expiry ms
    private static readonly List<ulong> Expired = new();

    public static bool TryAcquire(GodotObject holder, ulong leaseMs)
    {
        ulong now = Time.GetTicksMsec();

        Expired.Clear();
        foreach (KeyValuePair<ulong, ulong> lease in Leases)
            if (lease.Value <= now) Expired.Add(lease.Key);
        foreach (ulong id in Expired) Leases.Remove(id);

        ulong me = holder.GetInstanceId();
        if (!Leases.ContainsKey(me) && Leases.Count >= Limit) return false;

        Leases[me] = now + leaseMs;
        return true;
    }
}
