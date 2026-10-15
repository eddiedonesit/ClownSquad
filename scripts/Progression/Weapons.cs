namespace ClownSquad.Progression;

using System;
using System.Collections.Generic;
using ClownSquad.Combat;

/// <summary>The three things a clown can hit people with.</summary>
public enum WeaponKind { Mallet, Hammer, Chicken }

/// <summary>What a fully held charge does while standing still.</summary>
public enum ChargeMove { Throw, Quake }

/// <summary>
/// One node of a weapon's skill tree. Trees are three columns of three; a
/// column is learned top to bottom, and a node costs its tier in skill points.
/// </summary>
public sealed record Skill(string Id, string Name, string Blurb, int Column, int Tier)
{
    public int Cost => Tier;
}

/// <summary>A weapon: its combo, its charge move, its look and its skill tree.</summary>
public sealed record WeaponDef(
    WeaponKind Kind, string Name, string Blurb,
    AttackStep[] Combo, AttackStep Extra,
    ChargeMove Charge, string SpinSheet, string ThrowSound,
    string[] Columns, Skill[] Tree);

/// <summary>
/// Everything the learned skills add up to, read by the fighter and by the
/// levelling rules. Multipliers start at one, bonuses at zero, so a fresh
/// character plays the weapon exactly as designed.
/// </summary>
public sealed class WeaponPerks
{
    public float Damage = 1f, Finisher = 1f, Launch = 1f, Knockback = 1f, SwingSpeed = 1f;
    public int ExtraSteps;
    public float ThrowDamage = 1f, ThrowRange = 1f;
    public bool ThrowLaunches, ThrowExplodes;
    public float SpinTime = 1f, SpinDamage = 1f;
    public float QuakeRadius = 1f, QuakeDamage = 1f;
    public bool Aftershock;
    public float Crit, CritDamage, Loot, HealOnKill, MaxHealth = 1f, DamageTaken = 1f, DodgeFrames = 1f;
    public bool Unstoppable, SecondWind, LastStand, SquawkDodge;

    public static readonly WeaponPerks None = new();
}

/// <summary>
/// The weapon table and the skill trees (GDD 5). Skill trees belong to a
/// weapon, not to a character: each weapon remembers its own learned skills,
/// and the shared pool of skill points is spent into whichever tree is in hand.
/// The weapon itself is only changed between stages, on the world map.
/// </summary>
public static class Weapons
{
    // Mallet: the all-rounder. Three swings, a boomerang throw.
    private static readonly WeaponDef MalletDef = new(
        WeaponKind.Mallet, "SQUEAKY MALLET", "The classic. Quick, fair, and it comes back when thrown.",
        new AttackStep[]
        {
            new(0.08f, 0.06f, 0.16f, 10f, 2.0f, 0.050f, 1.9f, 0f),
            new(0.08f, 0.06f, 0.18f, 12f, 2.5f, 0.060f, 1.9f, 0f),
            new(0.13f, 0.08f, 0.32f, 22f, 8.5f, 0.110f, 2.3f, 6.5f),
        },
        new(0.08f, 0.06f, 0.16f, 11f, 2.2f, 0.055f, 1.9f, 0f),
        ChargeMove.Throw, "mallet_spin_hd", "fwee",
        new[] { "POWER", "SPEED", "SHOWMAN" },
        new Skill[]
        {
            new("heavy_head", "HEAVY HEAD", "+15% combo damage.", 0, 1),
            new("big_finish", "BIG FINISH", "The last swing hits 40% harder and launches higher.", 0, 2),
            new("boomerang", "BOOMERANG II", "The thrown mallet hits 50% harder and flies 40% farther.", 0, 3),
            new("quick_wrists", "QUICK WRISTS", "Swings are 15% faster.", 1, 1),
            new("fourth_swing", "FOURTH SWING", "The combo gains an extra swing before the finisher.", 1, 2),
            new("spin_doctor", "SPIN DOCTOR", "Spins last 50% longer and hit 50% harder.", 1, 3),
            new("crowd_pleaser", "CROWD PLEASER", "+5% crit chance.", 2, 1),
            new("encore", "ENCORE", "Each knockout heals 4% of max health.", 2, 2),
            new("jugglers_luck", "JUGGLER'S LUCK", "+30% crit damage and better loot.", 2, 3),
        });

    // Big Bertha: slow and enormous. Hits like a truck; the charge is a ground pound.
    private static readonly WeaponDef HammerDef = new(
        WeaponKind.Hammer, "BIG BERTHA", "A circus sledgehammer. Slow swings, huge hits, and a held charge pounds the ground.",
        new AttackStep[]
        {
            new(0.14f, 0.08f, 0.24f, 16f, 3.5f, 0.080f, 2.1f, 0f),
            new(0.15f, 0.08f, 0.26f, 19f, 4.0f, 0.090f, 2.1f, 0f),
            new(0.22f, 0.10f, 0.42f, 34f, 10f, 0.140f, 2.5f, 7.5f),
        },
        new(0.15f, 0.08f, 0.26f, 20f, 4.0f, 0.090f, 2.1f, 0f),
        ChargeMove.Quake, "hammer_spin_hd", "thump",
        new[] { "QUAKE", "IRON", "GRIT" },
        new Skill[]
        {
            new("iron_head", "IRON HEAD", "+15% combo damage.", 0, 1),
            new("earthshaker", "EARTHSHAKER", "The ground pound reaches 40% wider and hits 30% harder.", 0, 2),
            new("aftershock", "AFTERSHOCK", "The ground pound rumbles a second time.", 0, 3),
            new("thick_skin", "THICK SKIN", "Take 15% less damage.", 1, 1),
            new("unstoppable", "UNSTOPPABLE", "Hits no longer interrupt your swings.", 1, 2),
            new("counterweight", "COUNTERWEIGHT", "+50% knockback; the finisher launches higher.", 1, 3),
            new("hefty_grip", "HEFTY GRIP", "+15% max health.", 2, 1),
            new("second_wind", "SECOND WIND", "Once a stage, dropping below a quarter health heals 30%.", 2, 2),
            new("last_stand", "LAST STAND", "+30% damage while below 30% health.", 2, 3),
        });

    // The rubber chicken: light, long and fast. Four pecks; thrown, it flies far, squawking.
    private static readonly WeaponDef ChickenDef = new(
        WeaponKind.Chicken, "RUBBER CHICKEN", "Light and long. Four quick slaps, and thrown it flies the length of the ring.",
        new AttackStep[]
        {
            new(0.06f, 0.05f, 0.12f, 7f, 1.6f, 0.040f, 2.2f, 0f),
            new(0.06f, 0.05f, 0.12f, 7f, 1.6f, 0.040f, 2.2f, 0f),
            new(0.06f, 0.05f, 0.14f, 8f, 1.8f, 0.045f, 2.2f, 0f),
            new(0.10f, 0.07f, 0.26f, 15f, 7.0f, 0.090f, 2.5f, 6f),
        },
        new(0.06f, 0.05f, 0.12f, 7f, 1.6f, 0.040f, 2.2f, 0f),
        ChargeMove.Throw, "chicken_spin_hd", "squawk",
        new[] { "PECK", "FLIGHT", "LUCK" },
        new Skill[]
        {
            new("feather_duster", "FEATHER DUSTER", "+15% combo damage.", 0, 1),
            new("pecking_order", "PECKING ORDER", "The combo gains a fifth slap.", 0, 2),
            new("frenzy", "FRENZY", "Slaps are 20% faster.", 0, 3),
            new("fowl_ball", "FOWL BALL", "The thrown chicken hits 50% harder.", 1, 1),
            new("eggsplosive", "EGGSPLOSIVE", "The thrown chicken explodes where it turns.", 1, 2),
            new("rubber_bounce", "RUBBER BOUNCE", "The thrown chicken launches what it hits.", 1, 3),
            new("slippery", "SLIPPERY", "Dodges stay untouchable 50% longer.", 2, 1),
            new("squawk", "SQUAWK", "Dodging squawks, stunning anyone you slide past.", 2, 2),
            new("lucky_feather", "LUCKY FEATHER", "+6% crit chance and better loot.", 2, 3),
        });

    public static readonly WeaponDef[] All = { MalletDef, HammerDef, ChickenDef };

    public static WeaponDef Get(WeaponKind kind) => kind switch
    {
        WeaponKind.Hammer => HammerDef,
        WeaponKind.Chicken => ChickenDef,
        _ => MalletDef,
    };

    /// <summary>Sprite-sheet suffix: the mallet sheets are the plain ones.</summary>
    public static string SheetSuffix(WeaponKind kind) => kind == WeaponKind.Mallet ? "" : "_" + kind.ToString().ToLowerInvariant();

    // ---------------------------------------------------------- learning

    public static List<string> Learned(CharacterRecord r, WeaponKind kind)
    {
        string key = kind.ToString();
        if (!r.Skills.TryGetValue(key, out List<string>? list)) r.Skills[key] = list = new List<string>();
        return list;
    }

    public static bool Has(CharacterRecord r, string id) => Learned(r, r.Weapon).Contains(id);

    /// <summary>Whether a node can be learned now: not yet known, the one above it known, and affordable.</summary>
    public static bool CanLearn(CharacterRecord r, Skill s, out string why)
    {
        List<string> known = Learned(r, r.Weapon);
        why = "";
        if (known.Contains(s.Id)) { why = "LEARNED"; return false; }
        Skill? above = Above(r.Weapon, s);
        if (above is not null && !known.Contains(above.Id)) { why = $"LEARN {above.Name} FIRST"; return false; }
        if (r.SkillPoints < s.Cost) { why = $"NEEDS {s.Cost} SKILL POINT{(s.Cost > 1 ? "S" : "")}"; return false; }
        return true;
    }

    public static bool Learn(CharacterRecord r, Skill s)
    {
        if (!CanLearn(r, s, out _)) return false;
        r.SkillPoints -= s.Cost;
        Learned(r, r.Weapon).Add(s.Id);
        return true;
    }

    /// <summary>Give back a node's points, if nothing below it depends on it.</summary>
    public static bool Unlearn(CharacterRecord r, Skill s)
    {
        List<string> known = Learned(r, r.Weapon);
        if (!known.Contains(s.Id)) return false;
        Skill? below = Below(r.Weapon, s);
        if (below is not null && known.Contains(below.Id)) return false;
        known.Remove(s.Id);
        r.SkillPoints += s.Cost;
        return true;
    }

    private static Skill? Above(WeaponKind kind, Skill s) =>
        Array.Find(Get(kind).Tree, o => o.Column == s.Column && o.Tier == s.Tier - 1);

    private static Skill? Below(WeaponKind kind, Skill s) =>
        Array.Find(Get(kind).Tree, o => o.Column == s.Column && o.Tier == s.Tier + 1);

    // ------------------------------------------------------------- perks

    /// <summary>What the learned skills of the weapon in hand add up to.</summary>
    public static WeaponPerks Perks(CharacterRecord r)
    {
        var p = new WeaponPerks();
        if (!r.Skills.TryGetValue(r.Weapon.ToString(), out List<string>? known)) return p;
        foreach (string id in known)
        {
            switch (id)
            {
                case "heavy_head": case "iron_head": case "feather_duster": p.Damage *= 1.15f; break;
                case "big_finish": p.Finisher *= 1.4f; p.Launch *= 1.2f; break;
                case "boomerang": p.ThrowDamage *= 1.5f; p.ThrowRange *= 1.4f; break;
                case "quick_wrists": p.SwingSpeed *= 1.15f; break;
                case "fourth_swing": case "pecking_order": p.ExtraSteps++; break;
                case "spin_doctor": p.SpinTime *= 1.5f; p.SpinDamage *= 1.5f; break;
                case "crowd_pleaser": p.Crit += 0.05f; break;
                case "encore": p.HealOnKill += 0.04f; break;
                case "jugglers_luck": p.CritDamage += 0.3f; p.Loot += 0.2f; break;
                case "earthshaker": p.QuakeRadius *= 1.4f; p.QuakeDamage *= 1.3f; break;
                case "aftershock": p.Aftershock = true; break;
                case "thick_skin": p.DamageTaken *= 0.85f; break;
                case "unstoppable": p.Unstoppable = true; break;
                case "counterweight": p.Knockback *= 1.5f; p.Launch *= 1.25f; break;
                case "hefty_grip": p.MaxHealth *= 1.15f; break;
                case "second_wind": p.SecondWind = true; break;
                case "last_stand": p.LastStand = true; break;
                case "frenzy": p.SwingSpeed *= 1.2f; break;
                case "fowl_ball": p.ThrowDamage *= 1.5f; break;
                case "eggsplosive": p.ThrowExplodes = true; break;
                case "rubber_bounce": p.ThrowLaunches = true; break;
                case "slippery": p.DodgeFrames *= 1.5f; break;
                case "squawk": p.SquawkDodge = true; break;
                case "lucky_feather": p.Crit += 0.06f; p.Loot += 0.3f; break;
            }
        }
        return p;
    }
}
