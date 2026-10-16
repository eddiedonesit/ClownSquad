namespace ClownSquad;

using Godot;
using ClownSquad.Map;
using ClownSquad.Progression;

/// <summary>What kind of play is going on.</summary>
public enum PlayMode
{
    /// <summary>The campaign: the world map and its stages, on the chosen save slot.</summary>
    Campaign,
    /// <summary>All ten bosses back to back, level-30 clowns on a save of its own.</summary>
    BossRush,
    /// <summary>The practice ring: every weapon on dummies, using copies of your clowns; nothing is kept.</summary>
    Practice,
    /// <summary>An automated probe or screenshot run, on a throwaway save, always on Normal.</summary>
    Test,
    /// <summary>A guest in someone else's online game: nothing here is saved (the host plays the stage).</summary>
    OnlineGuest,
    /// <summary>The Daily Circus: today's stage, everyone at the same level, on a save of its own.</summary>
    Daily,
}

/// <summary>
/// The one place for state that has to outlive a scene change: which mode is
/// being played, which save it writes to, whether difficulty is forced. Each
/// mode is begun whole, so nothing a previous mode set can leak into the next.
/// (Before this, a boss rush left on the pause menu carried its rush flag and
/// its save path onto the world map; see the 2026-10-15 review, F1 and R3.)
///
/// Scenes read the mode; only the Begin methods change it.
/// </summary>
public static class Session
{
    /// <summary>
    /// A throwaway save for this test run alone: probes run side by side, and a
    /// shared file had them tripping over each other's writes.
    /// </summary>
    public static readonly string TestSave = $"user://probe_save_{OS.GetProcessId()}.json";
    public const string RushSave = "user://bossrush_save.json";
    public const string PracticeSave = "user://practice_save.json";

    public static PlayMode Mode { get; private set; } = PlayMode.Campaign;

    public static bool IsTest => Mode == PlayMode.Test;

    /// <summary>The campaign on the active slot. The title screen and the map begin this.</summary>
    public static void BeginCampaign()
    {
        bool fromElsewhere = Mode != PlayMode.Campaign;
        Reset(PlayMode.Campaign);
        SaveSystem.UseActiveSlot();
        // Coming back from practice, a rush or a test, what is in memory is that
        // mode's copy: read the slot again so it can never be saved over it.
        if (fromElsewhere) SaveSystem.Load();
    }

    /// <summary>A fresh boss rush: its own save, wiped each run.</summary>
    public static void BeginBossRush()
    {
        Reset(PlayMode.BossRush);
        SaveSystem.Path = RushSave;
        SaveSystem.DeleteForTests();
    }

    /// <summary>
    /// Practice with your own clowns: the active slot is loaded, then saves are
    /// pointed at a scratch file, so nothing done in practice reaches the slot.
    /// </summary>
    public static void BeginPractice()
    {
        Reset(PlayMode.Practice);
        SaveSystem.UseActiveSlot();
        SaveSystem.Load();
        SaveSystem.Path = PracticeSave;
    }

    /// <summary>A probe: throwaway save, Normal difficulty so runs compare.</summary>
    public static void BeginTest()
    {
        Reset(PlayMode.Test);
        // Tests compare runs: the standard field and no cheats, whatever is saved.
        GameSettings.Load();
        GameSettings.ForceNormal = true;
        GameSettings.Field = FieldSize.Normal;
        GameSettings.GodMode = false;
        SaveSystem.Path = TestSave;
        SaveSystem.DeleteForTests();
        ClearOldTestSaves();
    }

    public const string GuestSave = "user://online_guest_save.json";
    public const string DailySave = "user://daily_save.json";

    /// <summary>
    /// The Daily Circus with your clowns as they look and fight (name, clown
    /// and weapon), but fresh: every one at the daily's level with no gear,
    /// so everyone's score is earned on the same footing. Nothing reaches the slot.
    /// </summary>
    public static void BeginDaily()
    {
        Reset(PlayMode.Daily);
        SaveSystem.UseActiveSlot();
        SaveSystem.Load();
        SaveSystem.Path = DailySave;
        var chars = SaveSystem.Data.Characters;
        for (int i = 0; i < chars.Count; i++)
            chars[i] = new CharacterRecord { Name = chars[i].Name, Look = chars[i].Look, Weapon = chars[i].Weapon };
    }

    /// <summary>Watching and playing in a host's stage: saves go to a scratch file, so the guest's own slot is never touched.</summary>
    public static void BeginOnlineGuest()
    {
        Reset(PlayMode.OnlineGuest);
        SaveSystem.Path = GuestSave;
    }

    /// <summary>Tidy away scratch saves left by earlier test runs (an hour old or more).</summary>
    private static void ClearOldTestSaves()
    {
        string dir = ProjectSettings.GlobalizePath("user://");
        try
        {
            foreach (string f in System.IO.Directory.GetFiles(dir, "probe_save_*.json*"))
                if (System.DateTime.Now - System.IO.File.GetLastWriteTime(f) > System.TimeSpan.FromHours(1)) System.IO.File.Delete(f);
            string old = System.IO.Path.Combine(dir, "probe_save.json");
            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
        }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { }
    }

    private static void Reset(PlayMode mode)
    {
        Mode = mode;
        Campaign.Pending = null;
        GameSettings.ForceNormal = false;
    }
}
