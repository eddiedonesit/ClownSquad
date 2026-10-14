namespace ClownSquad.Map;

using System;
using System.Collections.Generic;
using Godot;

public enum StageKind { Normal, Boss, Bonus, Shop, Gate }

/// <summary>One wave of a stage: how many grunts, and whether the boss comes with them.</summary>
public readonly record struct WaveDef(int Grunts, bool Boss);

/// <summary>
/// One node on the world map. Normal, boss and bonus nodes are stages you
/// fight; a shop is visited; a gate leads to the next world. Story scenes will
/// hang off nodes by id (see ADR 0007), so ids never change once shipped.
/// </summary>
public sealed record StageDef(
    string Id,              // "1-1", "1-B" (boss), "1-S" (shop), "1-*" (bonus), "1-G" (gate)
    int World,
    string Title,
    StageKind Kind,
    Vector2 MapPos,         // in map pixels, 0..576 x 0..324
    string[] Requires,      // cleared ids that unlock this node (any one of them)
    WaveDef[] Waves,
    int EnemyLevel,         // the floor; enemies take the host's level if it is higher
    float RankBoost,        // multiplies the Rare/Epic/Legendary spawn weights
    int GruntCooldownMs,    // lower is more aggressive
    float BonusSeconds);    // bonus stages only: how long the party lasts

/// <summary>
/// The whole campaign: ten worlds, each a short road of five stages, a boss, a
/// shop partway along, a bonus stage on a side branch, and a gate onward.
/// Built from rules rather than typed out, so difficulty rises evenly from
/// 1-1 to the end of world 10 and the numbers can be retuned in one place.
/// </summary>
public static class Campaign
{
    public const int Worlds = 10;
    public const int StagesPerWorld = 5;

    public static readonly string[] WorldNames =
    {
        "BIG TOP MEADOW", "COTTON CANDY COAST", "FUNHOUSE FOREST", "JUGGLER'S JUNCTION", "HALL OF MIRRORS",
        "TIGHTROPE PEAKS", "POPCORN DESERT", "CALLIOPE CAVERNS", "RINGMASTER'S FORTRESS", "THE GRAND FINALE",
    };

    private static readonly Dictionary<string, StageDef> ById = new();
    private static readonly List<StageDef> All = new();

    /// <summary>The stage the fight scene should run next. Set by the world map.</summary>
    public static StageDef? Pending { get; set; }

    /// <summary>
    /// Controllers in the squad, in player order. The keyboard is always player
    /// one; pads join on the map (or mid-fight) and stay joined between stages.
    /// </summary>
    public static List<int> SquadDevices { get; } = new() { ClownSquad.Input.LocalDeviceInput.Keyboard };

    public const string MapScene = "res://scenes/WorldMap.tscn";
    public const string FightScene = "res://scenes/Main.tscn";
    public const string TitleScene = "res://scenes/Title.tscn";

    static Campaign()
    {
        for (int w = 1; w <= Worlds; w++) BuildWorld(w);
    }

    public static IReadOnlyList<StageDef> Stages => All;
    public static StageDef? Get(string id) => ById.TryGetValue(id, out StageDef? s) ? s : null;
    public static List<StageDef> InWorld(int world) => All.FindAll(s => s.World == world);

    /// <summary>The recommended level for a stage: rises five levels a world.</summary>
    public static int StageLevel(int world, int stage) => 1 + (world - 1) * 5 + (stage - 1);

    private static void BuildWorld(int w)
    {
        // A winding road left to right across the island, stages on the bends.
        Vector2 Road(float t) => new(70f + t * 436f, 170f + 70f * Mathf.Sin(t * 5.2f + w * 1.3f) * (0.6f + 0.4f * Mathf.Cos(t * 2f)));

        float grow = (w - 1) / (float)(Worlds - 1);           // 0 in world 1, 1 in world 10
        int cooldown = (int)Mathf.Lerp(1300f, 650f, grow);
        float rankBoost = 1f + 1.5f * grow;
        string prev = w == 1 ? "" : $"{w - 1}-G";

        for (int s = 1; s <= StagesPerWorld; s++)
        {
            int waves = 2 + (s >= 3 ? 1 : 0) + (w >= 4 ? 1 : 0) + (w >= 8 ? 1 : 0);
            var list = new WaveDef[waves];
            for (int i = 0; i < waves; i++)
                list[i] = new WaveDef(3 + i + s / 2 + w / 2, false);
            Add(new StageDef($"{w}-{s}", w, $"{w}-{s}", StageKind.Normal, Road((s - 1) / 6f),
                prev.Length == 0 ? Array.Empty<string>() : new[] { prev },
                list, StageLevel(w, s), rankBoost, cooldown, 0f));

            // The shop sits just off the road after stage 2, the bonus branch after stage 3.
            if (s == 2)
                Add(new StageDef($"{w}-S", w, "SHOP", StageKind.Shop, Road(1.5f / 6f) + new Vector2(0f, -62f),
                    new[] { $"{w}-2" }, Array.Empty<WaveDef>(), StageLevel(w, 2), 0f, 0, 0f));
            if (s == 3)
                Add(new StageDef($"{w}-*", w, "BONUS: PINATA PARTY", StageKind.Bonus, Road(2.5f / 6f) + new Vector2(0f, 64f),
                    new[] { $"{w}-3" }, Array.Empty<WaveDef>(), StageLevel(w, 3), 0f, 999999, 40f));
            prev = $"{w}-{s}";
        }

        // The world's boss: the last waves, then Bertie, tougher every world.
        var bossWaves = new[] { new WaveDef(4 + w / 2, false), new WaveDef(3 + w / 2, false), new WaveDef(1 + w / 3, true) };
        Add(new StageDef($"{w}-B", w, $"{WorldNames[w - 1]}: BOSS", StageKind.Boss, Road(5f / 6f),
            new[] { $"{w}-{StagesPerWorld}" }, bossWaves, StageLevel(w, StagesPerWorld) + 1, rankBoost, cooldown, 0f));
        Add(new StageDef($"{w}-G", w, w < Worlds ? $"TO WORLD {w + 1}" : "THE END?", StageKind.Gate, Road(1f),
            new[] { $"{w}-B" }, Array.Empty<WaveDef>(), 0, 0f, 0, 0f));
    }

    private static void Add(StageDef s)
    {
        All.Add(s);
        ById[s.Id] = s;
    }

    // ------------------------------------------------------------ progress

    /// <summary>Whether a node can be entered: the first stage always, others once a requirement is cleared.</summary>
    public static bool Unlocked(StageDef s, CampaignState state)
    {
        if (s.Requires.Length == 0) return true;
        foreach (string r in s.Requires)
            if (state.Cleared.Contains(r)) return true;
        return false;
    }

    /// <summary>Nodes joined to this one by a path on the map, in either direction.</summary>
    public static IEnumerable<StageDef> Neighbours(StageDef s)
    {
        foreach (StageDef other in All)
            if (Array.IndexOf(other.Requires, s.Id) >= 0 || Array.IndexOf(s.Requires, other.Id) >= 0)
                yield return other;
    }

    /// <summary>Shops are visited and gates passed through; everything else is fought.</summary>
    public static bool IsFight(StageDef s) => s.Kind is StageKind.Normal or StageKind.Boss or StageKind.Bonus;

    /// <summary>Visiting a shop or walking through a gate counts as clearing it, so the road beyond opens.</summary>
    public static void MarkCleared(CampaignState state, StageDef s)
    {
        if (!state.Cleared.Contains(s.Id)) state.Cleared.Add(s.Id);
        string key = s.World.ToString();
        state.ShopRestocks[key] = state.ShopRestocks.TryGetValue(key, out int n) ? n + 1 : 1;
    }
}

/// <summary>Campaign progress in the save file (ADR 0007).</summary>
public sealed class CampaignState
{
    public List<string> Cleared { get; set; } = new();
    /// <summary>Where the squad stands on the map.</summary>
    public string At { get; set; } = "1-1";
    /// <summary>Clears per world, which reseeds that world's shop stock.</summary>
    public Dictionary<string, int> ShopRestocks { get; set; } = new();
}
