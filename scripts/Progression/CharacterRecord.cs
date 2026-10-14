namespace ClownSquad.Progression;

using System;

/// <summary>The four stats a level-up's points go into.</summary>
public enum Stat { Pow, Guts, Zip, Luck }

/// <summary>
/// Everything about a character that outlives a session (ADR 0006). Plain data:
/// it is what gets written to the save file, so it holds numbers, never nodes.
/// Gear, skills and captures join it as those systems arrive.
/// </summary>
public sealed class CharacterRecord
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    /// <summary>XP earned toward the next level, not in total.</summary>
    public int Xp { get; set; }
    public long TotalXp { get; set; }
    /// <summary>Unspent stat points.</summary>
    public int StatPoints { get; set; }
    /// <summary>Banked for the skill trees, which do not exist yet (GDD 5).</summary>
    public int SkillPoints { get; set; }

    public int Pow { get; set; }
    public int Guts { get; set; }
    public int Zip { get; set; }
    public int Luck { get; set; }

    public int Get(Stat s) => s switch
    {
        Stat.Pow => Pow,
        Stat.Guts => Guts,
        Stat.Zip => Zip,
        Stat.Luck => Luck,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public void Set(Stat s, int value)
    {
        switch (s)
        {
            case Stat.Pow: Pow = value; break;
            case Stat.Guts: Guts = value; break;
            case Stat.Zip: Zip = value; break;
            case Stat.Luck: Luck = value; break;
        }
    }
}

/// <summary>
/// The levelling rules in one place: the XP curve, what a level grants, and what
/// each stat point is worth. Numbers are starting proposals, not balance.
/// </summary>
public static class Leveling
{
    public const int MaxLevel = 50;
    public const int StatPointsPerLevel = 3;
    public const int SkillPointsPerLevel = 1;

    public const float CritMultiplier = 1.75f;

    // Per point. Zip and Luck are capped so neither can be stacked into nonsense.
    private const float HealthPerGuts = 8f;
    private const float HealthPerLevel = 2f;
    private const float DamagePerPow = 0.04f;
    private const float SpeedPerZip = 0.02f;
    private const float MaxSpeedBonus = 0.40f;
    private const float BaseCrit = 0.03f;
    private const float CritPerLuck = 0.015f;
    private const float MaxCrit = 0.50f;

    /// <summary>XP needed to go from this level to the next. 40 at level 1, about 7,900 at 49.</summary>
    public static int XpToNext(int level) =>
        level >= MaxLevel ? 0 : (int)Math.Round(40.0 * Math.Pow(level, 1.35));

    /// <summary>Add XP, levelling as many times as it pays for. Returns levels gained.</summary>
    public static int Grant(CharacterRecord r, int xp)
    {
        if (xp <= 0 || r.Level >= MaxLevel) return 0;
        r.TotalXp += xp;
        r.Xp += xp;
        int gained = 0;
        while (r.Level < MaxLevel && r.Xp >= XpToNext(r.Level))
        {
            r.Xp -= XpToNext(r.Level);
            r.Level++;
            r.StatPoints += StatPointsPerLevel;
            r.SkillPoints += SkillPointsPerLevel;
            gained++;
        }
        if (r.Level >= MaxLevel) r.Xp = 0;
        return gained;
    }

    public static float MaxHealth(CharacterRecord r) =>
        100f + HealthPerGuts * r.Guts + HealthPerLevel * (r.Level - 1);

    public static float DamageMultiplier(CharacterRecord r) => 1f + DamagePerPow * r.Pow;

    public static float SpeedMultiplier(CharacterRecord r) => 1f + Math.Min(SpeedPerZip * r.Zip, MaxSpeedBonus);

    public static float CritChance(CharacterRecord r) => Math.Min(BaseCrit + CritPerLuck * r.Luck, MaxCrit);

    /// <summary>Name, one-line pitch, and the current effect, for the stat menu.</summary>
    public static (string name, string blurb, string effect) Describe(Stat s, CharacterRecord r) => s switch
    {
        Stat.Pow => ("POW", "hit harder", $"+{(DamageMultiplier(r) - 1f) * 100f:F0}% damage"),
        Stat.Guts => ("GUTS", "take more punishment", $"{MaxHealth(r):F0} max health"),
        Stat.Zip => ("ZIP", "move faster", $"+{(SpeedMultiplier(r) - 1f) * 100f:F0}% speed" + (SpeedMultiplier(r) - 1f >= MaxSpeedBonus ? " (max)" : "")),
        Stat.Luck => ("LUCK", "critical hits", $"{CritChance(r) * 100f:F1}% crit chance" + (CritChance(r) >= MaxCrit ? " (max)" : "")),
        _ => ("?", "", ""),
    };
}
