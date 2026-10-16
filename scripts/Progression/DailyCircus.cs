namespace ClownSquad.Progression;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Map;

/// <summary>
/// The Daily Circus: one stage a day, the same for everyone. The date picks
/// the seed; the seed picks a fight stage from somewhere in the campaign, the
/// waves it throws, and the night's act: a twist on the rules. Everyone plays
/// at the same level (<see cref="Level"/>) with copies of their clowns on a
/// save of its own, so scores compare. Play it as often as you like; the best
/// score of the day is kept (and goes on the day's Steam leaderboard).
/// </summary>
public static class DailyCircus
{
    public const int Level = 20;
    /// <summary>The acts' level, a little under the squad's: a fair fight on any day, wherever the stage is.</summary>
    public const int EnemyLevel = 15;

    public enum Act { AllStars, PieNight, MoonBounce, GlassCannons, BigTop, Stampede, DoubleTime, OneHitWonders, BouncyCastle, LightsOut }

    public sealed record ActInfo(Act Act, string Name, string Blurb);

    public static readonly ActInfo[] Acts =
    {
        new(Act.AllStars, "ALL-STAR NIGHT", "Every act is a star: rare clowns everywhere."),
        new(Act.PieNight, "PIE NIGHT", "Everyone throws pies tonight. Only pies."),
        new(Act.MoonBounce, "MOON BOUNCE", "Gravity took the night off. Jump!"),
        new(Act.GlassCannons, "GLASS CANNONS", "You hit twice as hard, with a little over half the health."),
        new(Act.BigTop, "THE BIG TOP", "The biggest ring of all, and the crowd fills it."),
        new(Act.Stampede, "STAMPEDE", "They just keep coming, half again as fast."),
        new(Act.DoubleTime, "DOUBLE TIME", "The band plays fast and so does everyone. Keep up!"),
        new(Act.OneHitWonders, "ONE-HIT WONDERS", "Every act goes down in a hit or two. So do you."),
        new(Act.BouncyCastle, "BOUNCY CASTLE", "Everything that gets hit goes flying. Everything."),
        new(Act.LightsOut, "LIGHTS OUT", "The power's out. Each clown brings their own spotlight."),
    };

    /// <summary>Today, by the calendar in UTC so the whole world shares a day: 2026-10-16.</summary>
    public static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>True while the daily is being played.</summary>
    public static bool Active => Session.Mode == PlayMode.Daily;

    /// <summary>The day's seed (stable for a date, different every day).</summary>
    public static ulong SeedFor(string day)
    {
        ulong h = 1469598103934665603UL;                      // FNV-1a: the same everywhere, unlike string.GetHashCode
        foreach (char ch in "clownsquad-daily-" + day) { h ^= ch; h *= 1099511628211UL; }
        return h;
    }

    public static ulong Seed => SeedFor(Today);

    public static ActInfo ActFor(string day) => Acts[(int)(SeedFor(day) % (ulong)Acts.Length)];
    public static ActInfo TodaysAct => ActFor(Today);

    /// <summary>The day's stage: any normal fight stage of up to four waves, chosen by the seed.</summary>
    public static StageDef StageFor(string day)
    {
        var fights = new List<StageDef>();
        foreach (StageDef s in Campaign.Stages)
            if (s.Kind == StageKind.Normal && s.Waves.Length is > 0 and <= 4) fights.Add(s);   // a one-sitting length
        return fights[(int)((SeedFor(day) >> 8) % (ulong)fights.Count)];
    }

    public static StageDef TodaysStage => StageFor(Today);

    /// <summary>The leaderboard for a day: one board per day, higher is better.</summary>
    public static string Board(string day) => "daily_" + day.Replace("-", "");

    /// <summary>
    /// A run's score: knockouts, the best combo, time to spare against a
    /// par of two minutes a wave, and a bonus for not losing a clown. Only a
    /// win scores the time and the bonus; a loss keeps what was knocked out.
    /// </summary>
    public static int Score(bool won, int kos, int bestCombo, double seconds, int waves, bool nobodyFell)
    {
        int score = kos * 100 + bestCombo * 25;
        if (!won) return score;
        double par = 120.0 * Math.Max(1, waves);
        score += 2000 + (int)Math.Max(0, (par - seconds) * 20.0);
        if (nobodyFell) score += 1500;
        return score;
    }

    // ---- the day's best, kept with the settings ----
    private const string BestFile = "user://daily.cfg";

    public static int BestToday
    {
        get
        {
            var cfg = new ConfigFile();
            cfg.Load(BestFile);
            return (int)cfg.GetValue("best", Today, 0);
        }
    }

    /// <summary>Record a score for today; true if it is a new best.</summary>
    public static bool Submit(int score)
    {
        var cfg = new ConfigFile();
        cfg.Load(BestFile);
        int best = (int)cfg.GetValue("best", Today, 0);
        if (score <= best) return false;
        cfg.SetValue("best", Today, score);
        cfg.Save(BestFile);
        return true;
    }

    public static void Start(SceneTree tree)
    {
        Session.BeginDaily();
        tree.ChangeSceneToFile(Campaign.FightScene);
    }

    /// <summary>Back to the title, off the daily save.</summary>
    public static void Leave(SceneTree tree)
    {
        Session.BeginCampaign();
        tree.ChangeSceneToFile(Campaign.TitleScene);
    }
}
