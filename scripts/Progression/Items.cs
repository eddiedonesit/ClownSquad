namespace ClownSquad.Progression;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public enum ItemSlot { Mallet, Hat, Ruff, Shoes }

/// <summary>Item rarity bands, GDD 6.1.</summary>
public enum ItemRarity { Common, Magic, Rare, Epic, Legendary }

/// <summary>Enemy rarity tiers, GDD 3. Bosses are scripted, not rolled.</summary>
public enum EnemyRank { Common, Rare, Epic, Legendary }

/// <summary>What an affix changes. The first group are stats; the rest are abilities.</summary>
public enum AffixKind
{
    Damage,         // percent
    Health,         // flat
    Speed,          // percent
    Crit,           // percent chance
    CritDamage,     // percent extra on a crit
    XpGain,         // percent
    LifeOnKill,     // flat health per kill
    // ability affixes: only on Epic and Legendary items
    Kaboom,         // the combo finisher explodes
    Bouncy,         // launches go higher
    Lucky,          // better drops
}

/// <summary>One rolled line on an item. Plain data, for the save file.</summary>
public sealed class AffixData
{
    public AffixKind Kind { get; set; }
    public float Value { get; set; }
}

/// <summary>A rolled item (ADR 0006: persists with the character). Plain data, for the save file.</summary>
public sealed class ItemData
{
    public string Id { get; set; } = "";
    public ItemSlot Slot { get; set; }
    public string Name { get; set; } = "";
    public ItemRarity Rarity { get; set; }
    public int Level { get; set; } = 1;
    public float Quality { get; set; } = 1f;
    public List<AffixData> Affixes { get; set; } = new();

    public float Sum(AffixKind kind)
    {
        float total = 0f;
        foreach (AffixData a in Affixes) if (a.Kind == kind) total += a.Value;
        return total;
    }
}

/// <summary>
/// Item generation, GDD 6.1: base item, then a quality roll, then a rarity roll
/// that decides the affix count, then the affixes, each with its own roll and
/// scaled by item level. Rolls come from a generator the caller owns, so the
/// host decides every drop (ADR 0003) and a seed replays the same loot.
/// </summary>
public static class Loot
{
    public static Color RarityColor(ItemRarity r) => r switch
    {
        ItemRarity.Common => new Color(0.92f, 0.92f, 0.92f),
        ItemRarity.Magic => new Color(0.45f, 0.7f, 1f),
        ItemRarity.Rare => new Color(1f, 0.85f, 0.3f),
        ItemRarity.Epic => new Color(0.8f, 0.45f, 1f),
        _ => new Color(1f, 0.55f, 0.15f),
    };

    private sealed record Base(ItemSlot Slot, string Name, AffixKind Stat, float PerLevel, float Flat);

    // Every slot's base carries one stat; affixes add the rest.
    private static readonly Base[] Bases =
    {
        new(ItemSlot.Mallet, "Squeaky Mallet", AffixKind.Damage, 0.6f, 4f),
        new(ItemSlot.Mallet, "Rubber Bonker", AffixKind.Damage, 0.7f, 3f),
        new(ItemSlot.Mallet, "Giant Gavel", AffixKind.Damage, 0.8f, 2f),
        new(ItemSlot.Hat, "Party Hat", AffixKind.Health, 3f, 6f),
        new(ItemSlot.Hat, "Top Hat", AffixKind.Health, 3.5f, 4f),
        new(ItemSlot.Hat, "Jester Cap", AffixKind.Crit, 0.15f, 1.5f),
        new(ItemSlot.Hat, "Bowler Hat", AffixKind.Health, 3.2f, 5f),
        new(ItemSlot.Hat, "Propeller Beanie", AffixKind.Speed, 0.2f, 2f),
        new(ItemSlot.Hat, "Paper Crown", AffixKind.XpGain, 0.3f, 4f),
        new(ItemSlot.Hat, "Fez", AffixKind.CritDamage, 1.0f, 5f),
        new(ItemSlot.Ruff, "Frilly Ruff", AffixKind.Health, 4f, 8f),
        new(ItemSlot.Ruff, "Bow Tie", AffixKind.CritDamage, 1.2f, 6f),
        new(ItemSlot.Ruff, "Polka Scarf", AffixKind.Health, 3.6f, 7f),
        new(ItemSlot.Ruff, "Big Necktie", AffixKind.Damage, 0.5f, 3f),
        new(ItemSlot.Ruff, "Hero's Medal", AffixKind.LifeOnKill, 0.3f, 2f),
        new(ItemSlot.Shoes, "Floppy Shoes", AffixKind.Speed, 0.25f, 3f),
        new(ItemSlot.Shoes, "Squeaky Sneakers", AffixKind.Speed, 0.3f, 2f),
        new(ItemSlot.Shoes, "Roller Skates", AffixKind.Speed, 0.35f, 1.5f),
        new(ItemSlot.Shoes, "Stripy Boots", AffixKind.Health, 3f, 6f),
        new(ItemSlot.Shoes, "Bunny Slippers", AffixKind.Crit, 0.12f, 1.5f),
    };

    private sealed record AffixDef(AffixKind Kind, int Weight, float Min, float Max, float PerLevel, string Prefix, string Suffix);

    private static readonly AffixDef[] Stats =
    {
        new(AffixKind.Damage, 100, 2f, 5f, 0.35f, "Bonking", "of Bonking"),
        new(AffixKind.Health, 100, 6f, 14f, 2.5f, "Sturdy", "of Guts"),
        new(AffixKind.Speed, 70, 2f, 4f, 0.1f, "Zippy", "of Zoom"),
        new(AffixKind.Crit, 70, 1f, 3f, 0.08f, "Lucky", "of Chance"),
        new(AffixKind.CritDamage, 60, 6f, 14f, 0.6f, "Wicked", "of Wallop"),
        new(AffixKind.XpGain, 50, 4f, 9f, 0.2f, "Studious", "of Lessons"),
        new(AffixKind.LifeOnKill, 50, 2f, 4f, 0.3f, "Hungry", "of Snacks"),
    };

    private static readonly AffixDef[] Abilities =
    {
        new(AffixKind.Kaboom, 1, 14f, 22f, 1f, "Explosive", "of Kaboom"),
        new(AffixKind.Bouncy, 1, 25f, 40f, 0f, "Bouncy", "of Boing"),
        new(AffixKind.Lucky, 1, 20f, 40f, 0f, "Charmed", "of Fortune"),
    };

    private static readonly string[] LegendNames =
    {
        "the Big Top", "Grandpa Bozo", "the Last Laugh", "the Ringmaster", "the Pie in the Sky", "Mr. Giggles",
    };

    /// <summary>
    /// Roll an item. <paramref name="rarityBoost"/> shifts the rarity weights
    /// toward the rare end: enemy rank, bosses and LUCK all feed it.
    /// </summary>
    public static ItemData Roll(RandomNumberGenerator rng, int level, float rarityBoost)
    {
        Base b = Bases[rng.RandiRange(0, Bases.Length - 1)];
        ItemRarity rarity = RollRarity(rng, rarityBoost);
        float quality = rng.RandfRange(0.85f, 1.15f);
        var item = new ItemData
        {
            Id = $"{Time.GetUnixTimeFromSystem():F0}-{rng.Randi():x8}",
            Slot = b.Slot,
            Rarity = rarity,
            Level = Math.Max(1, level),
            Quality = quality,
        };
        item.Affixes.Add(new AffixData { Kind = b.Stat, Value = Round((b.Flat + b.PerLevel * level) * quality) });

        (int min, int max) count = rarity switch
        {
            ItemRarity.Common => (0, 0),
            ItemRarity.Magic => (1, 2),
            ItemRarity.Rare => (3, 4),
            ItemRarity.Epic => (4, 5),
            _ => (5, 6),
        };
        int n = rng.RandiRange(count.min, count.max);
        var picked = new List<AffixDef>();
        bool wantsAbility = rarity == ItemRarity.Legendary || (rarity == ItemRarity.Epic && rng.Randf() < 0.5f);
        if (wantsAbility)
        {
            picked.Add(Abilities[rng.RandiRange(0, Abilities.Length - 1)]);
            n--;
        }
        for (int i = 0; i < n; i++) picked.Add(Weighted(rng, Stats));

        foreach (AffixDef a in picked)
            item.Affixes.Add(new AffixData { Kind = a.Kind, Value = Round(rng.RandfRange(a.Min, a.Max) + a.PerLevel * level) });

        item.Name = NameFor(rng, item, b, picked);
        return item;
    }

    private static ItemRarity RollRarity(RandomNumberGenerator rng, float boost)
    {
        // GDD weights 1000 / 300 / 60 / 12 / 1, with the rare end scaled up by the boost.
        float[] w = { 1000f, 300f * (1f + boost), 60f * (1f + boost * 2f), 12f * (1f + boost * 3f), 1f * (1f + boost * 5f) };
        float roll = rng.Randf() * w.Sum();
        for (int i = 0; i < w.Length; i++)
        {
            roll -= w[i];
            if (roll <= 0f) return (ItemRarity)i;
        }
        return ItemRarity.Common;
    }

    private static AffixDef Weighted(RandomNumberGenerator rng, AffixDef[] pool)
    {
        int roll = rng.RandiRange(1, pool.Sum(a => a.Weight));
        foreach (AffixDef a in pool)
        {
            roll -= a.Weight;
            if (roll <= 0) return a;
        }
        return pool[0];
    }

    private static string NameFor(RandomNumberGenerator rng, ItemData item, Base b, List<AffixDef> affixes)
    {
        if (item.Rarity == ItemRarity.Legendary)
            return $"{b.Name} of {LegendNames[rng.RandiRange(0, LegendNames.Length - 1)]}";
        if (affixes.Count == 0) return b.Name;
        string prefix = affixes[0].Prefix;
        return affixes.Count > 1 ? $"{prefix} {b.Name} {affixes[^1].Suffix}" : $"{prefix} {b.Name}";
    }

    private static float Round(float v) => Mathf.Round(v * 10f) / 10f;

    /// <summary>One line per affix, for menus and pickups.</summary>
    public static string Describe(AffixData a) => a.Kind switch
    {
        AffixKind.Damage => $"+{a.Value:0.#}% damage",
        AffixKind.Health => $"+{a.Value:0.#} max health",
        AffixKind.Speed => $"+{a.Value:0.#}% speed",
        AffixKind.Crit => $"+{a.Value:0.#}% crit chance",
        AffixKind.CritDamage => $"+{a.Value:0.#}% crit damage",
        AffixKind.XpGain => $"+{a.Value:0.#}% XP",
        AffixKind.LifeOnKill => $"+{a.Value:0.#} health per KO",
        AffixKind.Kaboom => $"KABOOM: finisher explodes for {a.Value:0}",
        AffixKind.Bouncy => $"BOING: launches {a.Value:0}% higher",
        AffixKind.Lucky => $"FORTUNE: {a.Value:0}% better drops",
        _ => a.Kind.ToString(),
    };

    /// <summary>A rough worth, for picking which of two items to wear by default.</summary>
    public static float Score(ItemData item) =>
        item.Sum(AffixKind.Damage) * 2.5f + item.Sum(AffixKind.Health) * 0.8f + item.Sum(AffixKind.Speed) * 2f
        + item.Sum(AffixKind.Crit) * 3f + item.Sum(AffixKind.CritDamage) * 0.6f + item.Sum(AffixKind.XpGain)
        + item.Sum(AffixKind.LifeOnKill) * 2f + (item.Sum(AffixKind.Kaboom) + item.Sum(AffixKind.Bouncy) + item.Sum(AffixKind.Lucky)) * 1.5f
        + (int)item.Rarity * 4f;
}
