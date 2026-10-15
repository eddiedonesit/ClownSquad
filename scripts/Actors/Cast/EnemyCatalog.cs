namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Input;

/// <summary>
/// Who fights where. Each world adds one new kind of enemy to the mix, so a
/// stage always has something new to learn on top of what came before; and
/// each world ends with its own boss act:
///
///   1 Big Top Meadow      grunts                      BELLYFLOP BERTIE
///   2 Cotton Candy Coast  + jugglers                  BOBO THE BALANCER
///   3 Funhouse Forest     + unicyclists               MADAME STRIPES and RAJAH
///   4 Juggler's Junction  + balloon artists           THE JALOPY
///   5 Hall of Mirrors     + mimes                     STRETCH
///   6 Tightrope Peaks     + stilt-walkers             THE FLYING FRATELLI
///   7 Popcorn Desert      + weightlifters             PYRO PETE
///   8 Calliope Caverns    the lot                     THE TOTEM TOTS
///   9 Ringmaster's Fort   the lot, more of the hard   IRON IVAN
///  10 The Grand Finale    the lot                     THE RINGMASTER
/// </summary>
public static class EnemyCatalog
{
    private static readonly string[] Order = { "grunt", "juggler", "unicyclist", "balloonist", "mime", "stiltwalker", "weightlifter" };

    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["juggler"] = new[] { "JOJO", "PINWHEEL", "TOSSER", "CASCADE", "FLIP" },
        ["unicyclist"] = new[] { "WHEELIE", "SPOKES", "ONE-WHEEL WALLY", "PEDALS" },
        ["balloonist"] = new[] { "TWISTY", "SQUEAKERS", "LOOPY LOU", "PUFF" },
        ["mime"] = new[] { "MARCEL", "SILENT SAM", "...", "PIERROT" },
        ["stiltwalker"] = new[] { "LANKY", "STILTSKIN", "TALL PAUL", "HIGHPOCKETS" },
        ["weightlifter"] = new[] { "BRUNO", "DUMBBELL DAN", "HEFTY", "BRICK" },
    };

    /// <summary>The enemy mix of a world, as kinds with spawn weights.</summary>
    public static (string kind, int weight)[] Pool(int world)
    {
        var pool = new List<(string, int)>();
        int known = Mathf.Clamp(world, 1, Order.Length);
        for (int i = 0; i < known; i++)
        {
            int weight = i == 0 ? 6 : 2;
            if (i == known - 1 && world <= Order.Length && i > 0) weight = 3;     // the world's new arrival shows up more
            if (world >= 9 && i >= 4) weight = 3;                                  // the tough ones, more often, late on
            pool.Add((Order[i], weight));
        }
        return pool.ToArray();
    }

    public static PlayerFighter Create(string kind) => kind switch
    {
        "juggler" => new JugglerEnemy(),
        "unicyclist" => new UnicyclistEnemy(),
        "weightlifter" => new WeightlifterEnemy(),
        "balloonist" => new BalloonistEnemy(),
        "stiltwalker" => new StiltWalkerEnemy(),
        "mime" => new MimeEnemy(),
        "balloondog" => new BalloonDog(),
        _ => new GruntClown(),
    };

    public static string NameFor(string kind, RandomNumberGenerator rng, string[] gruntNames)
    {
        string[] list = Names.TryGetValue(kind, out string[]? n) ? n : gruntNames;
        return list[rng.RandiRange(0, list.Length - 1)];
    }

    /// <summary>The input that drives a freshly made enemy.</summary>
    public static IInputSource Brain(PlayerFighter enemy, int gruntCooldownMs) => enemy is CastFighter cf
        ? new CastInput(cf, cf.MakeAi(gruntCooldownMs))
        : new BrawlerAi(enemy, attackRange: 1.35f, backoffRange: 0.7f, attackCooldownMs: (ulong)gruntCooldownMs);

    /// <summary>A world's boss act, main boss first. Some acts come in pairs.</summary>
    public static List<PlayerFighter> Boss(int world)
    {
        var list = new List<PlayerFighter>();
        switch (Mathf.Clamp(world, 1, 10))
        {
            case 2: list.Add(new BallRiderBoss { World = world }); break;
            case 3:
                var trainer = new TrainerBoss { World = world };
                var tiger = new TigerFighter();
                trainer.Tiger = tiger;
                list.Add(trainer);
                list.Add(tiger);
                break;
            case 4: list.Add(new ClownCarBoss { World = world }); break;
            case 5: list.Add(new ElasticBoss { World = world }); break;
            case 6:
                var a = new TrapezeArtist { World = world, Sheet = "trapeze_a", DisplayName = "FLAVIO", Primary = true, Bar = 0 };
                var b = new TrapezeArtist { World = world, Sheet = "trapeze_b", DisplayName = "FIORELLA", Bar = 1 };
                a.Partner = b;
                b.Partner = a;
                list.Add(a);
                list.Add(b);
                break;
            case 7: list.Add(new FireEaterBoss { World = world }); break;
            case 8: list.Add(new TotStackBoss { World = world }); break;
            case 9: list.Add(new StrongmanBoss { World = world }); break;
            case 10: list.Add(new RingmasterBoss { World = world }); break;
            default: list.Add(new BossClown()); break;
        }
        return list;
    }

    /// <summary>The boss of each world, for the map's labels.</summary>
    public static string BossName(int world) => Mathf.Clamp(world, 1, 10) switch
    {
        2 => "BOBO THE BALANCER", 3 => "MADAME STRIPES", 4 => "THE JALOPY", 5 => "STRETCH",
        6 => "THE FLYING FRATELLI", 7 => "PYRO PETE", 8 => "THE TOTEM TOTS", 9 => "IRON IVAN",
        10 => "THE RINGMASTER", _ => "BELLYFLOP BERTIE",
    };
}
