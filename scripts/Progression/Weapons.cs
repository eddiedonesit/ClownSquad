namespace ClownSquad.Progression;

using System;
using System.Collections.Generic;
using ClownSquad.Combat;

/// <summary>The three things a clown can hit people with.</summary>
public enum WeaponKind { Mallet, Hammer, Chicken, Pie, Pins, Slapstick, PopGun, BalloonSword }

/// <summary>What a fully held charge does while standing still.</summary>
public enum ChargeMove
{
    /// <summary>Thrown, and it comes back (mallet, chicken).</summary>
    Throw,
    /// <summary>A ground pound all round (hammer).</summary>
    Quake,
    /// <summary>A pie lobbed onto a spot ahead, splatting all there.</summary>
    Lob,
    /// <summary>A fan of juggling pins thrown straight down the lane.</summary>
    Fan,
    /// <summary>One huge clap that stuns everything in front.</summary>
    Clap,
    /// <summary>A big cork that flies through everything in the lane.</summary>
    BigShot,
    /// <summary>A fencer's dash forward, hitting all in the way.</summary>
    Lunge,
}

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
    string[] Columns, Skill[] Tree)
{
    /// <summary>Each combo step fires a cork instead of swinging; Range is how far it flies.</summary>
    public bool Ranged { get; init; }
    /// <summary>What a connecting hit sounds like.</summary>
    public string HitSound { get; init; } = "bonk";
    public float HitPitch { get; init; } = 1f;
    /// <summary>How hard it strikes the ground on a swing: impact size and shake (0 = no ground impact).</summary>
    public float Heft { get; init; } = 1f;
    /// <summary>Multiplies the stun every hit inflicts.</summary>
    public float StunScale { get; init; } = 1f;
    /// <summary>Seconds the finisher leaves its target reeling (a pie in the face).</summary>
    public float FinisherStagger { get; init; }
    /// <summary>Juggles a stock of these above the head; each attack press throws one (the pins).</summary>
    public bool Juggles { get; init; }
    /// <summary>Hold attack and a direction for this instead of the spin (the balloon sword's thrust).</summary>
    public bool Thrusts { get; init; }
}

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
    // The newer weapons: pies, pins, slapstick, pop gun, balloon sword.
    public float SplatRadius = 1f, Blind = 1f, StunTime = 1f, Lunge = 1f;
    public int ExtraShots;
    public bool Pierce, PopFinisher;

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
            new("fourth_swing", "FOURTH SWING", "After the big third hit, one more swing to juggle them.", 1, 2),
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
        })
    { HitPitch = 0.7f, Heft = 1.45f };

    // The rubber chicken: light, long and fast. Quick slaps; thrown, it flies far, squawking.
    private static readonly WeaponDef ChickenDef = new(
        WeaponKind.Chicken, "RUBBER CHICKEN", "Light and long. Quick slaps, and thrown it flies the length of the ring.",
        new AttackStep[]
        {
            new(0.06f, 0.05f, 0.12f, 8f, 1.6f, 0.040f, 2.2f, 0f),
            new(0.06f, 0.05f, 0.13f, 9f, 1.8f, 0.045f, 2.2f, 0f),
            new(0.10f, 0.07f, 0.26f, 17f, 7.0f, 0.090f, 2.5f, 6f),
        },
        new(0.06f, 0.05f, 0.12f, 7f, 1.6f, 0.040f, 2.2f, 0f),
        ChargeMove.Throw, "chicken_spin_hd", "squawk",
        new[] { "PECK", "FLIGHT", "LUCK" },
        new Skill[]
        {
            new("feather_duster", "FEATHER DUSTER", "+15% combo damage.", 0, 1),
            new("pecking_order", "PECKING ORDER", "After the big third hit, one more slap to juggle them.", 0, 2),
            new("frenzy", "FRENZY", "Slaps are 20% faster.", 0, 3),
            new("fowl_ball", "FOWL BALL", "The thrown chicken hits 50% harder.", 1, 1),
            new("eggsplosive", "EGGSPLOSIVE", "The thrown chicken explodes where it turns.", 1, 2),
            new("rubber_bounce", "RUBBER BOUNCE", "The thrown chicken launches what it hits.", 1, 3),
            new("slippery", "SLIPPERY", "Dodges stay untouchable 50% longer.", 2, 1),
            new("squawk", "SQUAWK", "Dodging squawks, stunning anyone you slide past.", 2, 2),
            new("lucky_feather", "LUCKY FEATHER", "+6% crit chance and better loot.", 2, 3),
        })
    { HitSound = "squeak", HitPitch = 1.3f, Heft = 0.6f };

    // Cream pies: a short smush, a finisher that leaves them blinded, and a lobbed pie that splats a crowd.
    private static readonly WeaponDef PieDef = new(
        WeaponKind.Pie, "CREAM PIE", "Every press throws a pie in someone's face; every third is a big one. Held charge lobs a giant pie that leaves a slick of cream: enemies slip and fall.",
        new AttackStep[]
        {
            new(0.08f, 0.05f, 0.20f, 9f, 2.0f, 0.050f, 8f, 0f),
            new(0.08f, 0.05f, 0.20f, 10f, 2.2f, 0.050f, 8f, 0f),
            new(0.12f, 0.06f, 0.32f, 18f, 6.0f, 0.090f, 9f, 4.5f),
        },
        new(0.08f, 0.05f, 0.20f, 10f, 2.2f, 0.055f, 8f, 0f),
        ChargeMove.Lob, "mallet_spin_hd", "fwee",
        new[] { "CREAM", "BAKERY", "SWEETS" },
        new Skill[]
        {
            new("banana_cream", "BANANA CREAM", "Pie splats reach 40% wider.", 0, 1),
            new("seconds", "SECONDS", "Lob two pies at once.", 0, 2),
            new("pie_in_the_sky", "PIE IN THE SKY", "A third pie, and splats hit 30% harder.", 0, 3),
            new("meringue", "MERINGUE", "+15% combo damage.", 1, 1),
            new("faceful", "FACEFUL", "Blinded enemies stay blind 50% longer.", 1, 2),
            new("bakers_dozen", "BAKER'S DOZEN", "After the big third hit, one more smush to juggle them.", 1, 3),
            new("cherry_on_top", "CHERRY ON TOP", "+5% crit chance.", 2, 1),
            new("sweet_tooth", "SWEET TOOTH", "Each knockout heals 4% of max health.", 2, 2),
            new("custard_coat", "CUSTARD COAT", "Take 15% less damage.", 2, 3),
        })
    { Ranged = true, HitSound = "splat", HitPitch = 1.1f, Heft = 0f, FinisherStagger = 0.9f };

    // Juggling pins: quick clubs, and a held charge throws a fan of pins down the lane.
    private static readonly WeaponDef PinsDef = new(
        WeaponKind.Pins, "JUGGLING PINS", "Juggles up to five pins overhead. Each press throws one as fast as you can tap; they come back while you juggle. Held charge throws them all at once. Out of pins, it clubs.",
        new AttackStep[]
        {
            new(0.07f, 0.05f, 0.14f, 9f, 2.0f, 0.050f, 2.0f, 0f),
            new(0.07f, 0.05f, 0.15f, 9f, 2.0f, 0.050f, 2.0f, 0f),
            new(0.11f, 0.07f, 0.28f, 19f, 7.5f, 0.100f, 2.3f, 6f),
        },
        new(0.07f, 0.05f, 0.14f, 9f, 2.0f, 0.050f, 2.0f, 0f),
        ChargeMove.Fan, "mallet_spin_hd", "whirr",
        new[] { "JUGGLE", "BOWL", "SHOW" },
        new Skill[]
        {
            new("fourth_pin", "FOURTH PIN", "After the big third hit, one more swing to juggle them.", 0, 1),
            new("five_pin", "SEVEN PIN", "Juggle seven pins instead of five.", 0, 2),
            new("strike", "STRIKE!", "Thrown pins fly through everyone in the lane.", 0, 3),
            new("heavy_pins", "HEAVY PINS", "+15% combo damage.", 1, 1),
            new("spare", "SPARE", "Thrown pins hit 50% harder.", 1, 2),
            new("ten_pin", "TEN PIN", "Thrown pins launch what they hit.", 1, 3),
            new("crowd_pleaser", "CROWD PLEASER", "+5% crit chance.", 2, 1),
            new("quick_hands", "QUICK HANDS", "Swings are 15% faster.", 2, 2),
            new("jugglers_luck", "JUGGLER'S LUCK", "+30% crit damage and better loot.", 2, 3),
        })
    { HitSound = "clack", HitPitch = 1.1f, Heft = 0.6f, Juggles = true };

    // The slapstick: little damage, huge noise. Every hit stuns; a held charge claps the whole lane silly.
    private static readonly WeaponDef SlapstickDef = new(
        WeaponKind.Slapstick, "SLAPSTICK", "Light damage, loud slaps that stun. A held charge claps everything in front senseless.",
        new AttackStep[]
        {
            new(0.06f, 0.05f, 0.14f, 7f, 3.5f, 0.060f, 2.1f, 0f),
            new(0.06f, 0.05f, 0.14f, 7f, 3.5f, 0.060f, 2.1f, 0f),
            new(0.10f, 0.07f, 0.28f, 15f, 11f, 0.120f, 2.4f, 5.5f),
        },
        new(0.06f, 0.05f, 0.14f, 7f, 3.5f, 0.060f, 2.1f, 0f),
        ChargeMove.Clap, "mallet_spin_hd", "slap",
        new[] { "SLAP", "CLAP", "PRATFALL" },
        new Skill[]
        {
            new("sting", "STING", "+15% combo damage.", 0, 1),
            new("double_slap", "DOUBLE SLAP", "After the big third hit, one more slap to juggle them.", 0, 2),
            new("slap_happy", "SLAP HAPPY", "Slaps are 20% faster.", 0, 3),
            new("thunderclap", "THUNDERCLAP", "The clap reaches 40% farther and hits 30% harder.", 1, 1),
            new("stunner", "STUNNER", "Every stun lasts 50% longer.", 1, 2),
            new("encore", "ENCORE", "Each knockout heals 4% of max health.", 1, 3),
            new("pratfall", "PRATFALL", "+50% knockback; finishers launch higher.", 2, 1),
            new("thick_skin", "THICK SKIN", "Take 15% less damage.", 2, 2),
            new("unstoppable", "UNSTOPPABLE", "Hits no longer interrupt your swings.", 2, 3),
        })
    { HitSound = "slap", HitPitch = 0.9f, Heft = 0f, StunScale = 1.8f };

    // The pop gun: fights from range. Every press fires a cork; a held charge fires the big one.
    private static readonly WeaponDef PopGunDef = new(
        WeaponKind.PopGun, "POP GUN", "Fires corks down the lane. Weak up close, safe from afar. A held charge fires a huge cork through everything.",
        new AttackStep[]
        {
            new(0.06f, 0.04f, 0.20f, 8f, 2.5f, 0.030f, 7f, 0f),
            new(0.06f, 0.04f, 0.20f, 8f, 2.5f, 0.030f, 7f, 0f),
            new(0.10f, 0.05f, 0.32f, 14f, 7f, 0.060f, 8f, 4f),
        },
        new(0.06f, 0.04f, 0.20f, 8f, 2.5f, 0.030f, 7f, 0f),
        ChargeMove.BigShot, "mallet_spin_hd", "pop",
        new[] { "AMMO", "AIM", "LUCK" },
        new Skill[]
        {
            new("rapid_fire", "RAPID FIRE", "Shoots 20% faster.", 0, 1),
            new("double_barrel", "DOUBLE BARREL", "Every shot fires two corks (each a little weaker).", 0, 2),
            new("confetti_cannon", "CONFETTI CANNON", "The big cork bursts in a blast of confetti.", 0, 3),
            new("sharpshooter", "SHARPSHOOTER", "+15% damage.", 1, 1),
            new("trick_shot", "TRICK SHOT", "Corks fly through everyone in the lane.", 1, 2),
            new("long_barrel", "LONG BARREL", "Corks fly 50% farther; the big cork hits 50% harder.", 1, 3),
            new("bang_flag", "BANG!", "+5% crit chance.", 2, 1),
            new("slippery", "SLIPPERY", "Dodges stay untouchable 50% longer.", 2, 2),
            new("lucky_cork", "LUCKY CORK", "+6% crit chance and better loot.", 2, 3),
        })
    { Ranged = true, HitSound = "pop", HitPitch = 1.2f, Heft = 0f };

    // The balloon sword: the longest reach in the squad, quick and squeaky; a held charge lunges.
    private static readonly WeaponDef BalloonDef = new(
        WeaponKind.BalloonSword, "BALLOON SWORD", "The longest reach in the circus. Quick squeaky slashes; hold attack and a direction to thrust them into the wall; a held charge lunges like a fencer.",
        new AttackStep[]
        {
            new(0.07f, 0.05f, 0.14f, 8f, 2.0f, 0.040f, 2.7f, 0f),
            new(0.07f, 0.05f, 0.15f, 8f, 2.0f, 0.040f, 2.7f, 0f),
            new(0.11f, 0.07f, 0.28f, 17f, 7f, 0.100f, 2.9f, 6f),
        },
        new(0.07f, 0.05f, 0.14f, 8f, 2.0f, 0.040f, 2.7f, 0f),
        ChargeMove.Lunge, "mallet_spin_hd", "whoosh",
        new[] { "FENCE", "POP", "FLOAT" },
        new Skill[]
        {
            new("en_garde", "EN GARDE", "+15% combo damage.", 0, 1),
            new("riposte", "RIPOSTE", "After the big third hit, one more slash to juggle them.", 0, 2),
            new("fleche", "FLECHE", "The lunge goes 50% farther and hits 50% harder.", 0, 3),
            new("squeaky", "SQUEAKY", "Slashes are 15% faster.", 1, 1),
            new("pop_goes", "POP GOES THE...", "The finisher bursts the balloon in a bang that hits all around.", 1, 2),
            new("spin_doctor", "TWISTER", "The wall thrust hits 50% harder and shoves further.", 1, 3),
            new("helium", "HELIUM", "Dodges stay untouchable 50% longer.", 2, 1),
            new("crowd_pleaser", "CROWD PLEASER", "+5% crit chance.", 2, 2),
            new("last_stand", "LAST STAND", "+30% damage while below 30% health.", 2, 3),
        })
    { HitSound = "squeak", HitPitch = 1.4f, Heft = 0f, Thrusts = true };

    public static readonly WeaponDef[] All = { MalletDef, HammerDef, ChickenDef, PieDef, PinsDef, SlapstickDef, PopGunDef, BalloonDef };

    public static WeaponDef Get(WeaponKind kind) => kind switch
    {
        WeaponKind.Hammer => HammerDef,
        WeaponKind.Chicken => ChickenDef,
        WeaponKind.Pie => PieDef,
        WeaponKind.Pins => PinsDef,
        WeaponKind.Slapstick => SlapstickDef,
        WeaponKind.PopGun => PopGunDef,
        WeaponKind.BalloonSword => BalloonDef,
        _ => MalletDef,
    };

    /// <summary>Sprite-sheet suffix: the mallet sheets are the plain ones.</summary>
    public static string SheetSuffix(WeaponKind kind) => kind switch
    {
        WeaponKind.Mallet => "",
        WeaponKind.BalloonSword => "_balloon",
        _ => "_" + kind.ToString().ToLowerInvariant(),
    };

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
        if (Learned(r, r.Weapon).Count >= Get(r.Weapon).Tree.Length) ClownSquad.Services.Achievements.Award("full_tree");
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
                case "lucky_feather": case "lucky_cork": p.Crit += 0.06f; p.Loot += 0.3f; break;
                // pie
                case "banana_cream": p.SplatRadius *= 1.4f; break;
                case "seconds": p.ExtraShots++; break;
                case "pie_in_the_sky": p.ExtraShots++; p.ThrowDamage *= 1.3f; break;
                case "meringue": case "heavy_pins": case "sting": case "sharpshooter": case "en_garde": p.Damage *= 1.15f; break;
                case "faceful": p.Blind *= 1.5f; break;
                case "bakers_dozen": case "fourth_pin": case "double_slap": case "riposte": p.ExtraSteps++; break;
                case "cherry_on_top": case "bang_flag": p.Crit += 0.05f; break;
                case "sweet_tooth": p.HealOnKill += 0.04f; break;
                case "custard_coat": p.DamageTaken *= 0.85f; break;
                // pins
                case "five_pin": p.ExtraShots += 2; break;
                case "strike": case "trick_shot": p.Pierce = true; break;
                case "spare": p.ThrowDamage *= 1.5f; break;
                case "ten_pin": p.ThrowLaunches = true; break;
                case "quick_hands": case "squeaky": p.SwingSpeed *= 1.15f; break;
                // slapstick
                case "slap_happy": case "rapid_fire": p.SwingSpeed *= 1.2f; break;
                case "thunderclap": p.QuakeRadius *= 1.4f; p.QuakeDamage *= 1.3f; break;
                case "stunner": p.StunTime *= 1.5f; break;
                case "pratfall": p.Knockback *= 1.5f; p.Launch *= 1.25f; break;
                // pop gun
                case "double_barrel": p.ExtraShots++; break;
                case "confetti_cannon": p.ThrowExplodes = true; break;
                case "long_barrel": p.ThrowRange *= 1.5f; p.ThrowDamage *= 1.5f; break;
                // balloon sword
                case "fleche": p.Lunge *= 1.5f; break;
                case "pop_goes": p.PopFinisher = true; break;
                case "helium": p.DodgeFrames *= 1.5f; break;
            }
        }
        return p;
    }
}
