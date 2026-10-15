namespace ClownSquad.Progression;

using Godot;

public enum Difficulty { Easy, Normal, Hard }

/// <summary>
/// The player's settings, kept apart from the save slots (they belong to the
/// machine, not to a squad): music and sound volume on their own audio buses,
/// screen shake, fullscreen, and difficulty. Stored in user://settings.cfg.
///
/// Difficulty scales the enemies as they arrive (health and damage) and the
/// squad's spare lives; it never touches levels, loot or XP, so a squad can
/// move between difficulties freely. Tests always run on Normal.
/// </summary>
public static class GameSettings
{
    private const string FilePath = "user://settings.cfg";
    public const string MusicBus = "Music";
    public const string SfxBus = "SFX";

    public static float Music { get; set; } = 0.8f;
    public static float Sound { get; set; } = 0.9f;
    /// <summary>0 off, 0.5 low, 1 full.</summary>
    public static float Shake { get; set; } = 1f;
    public static bool Fullscreen { get; set; }
    public static Difficulty Difficulty { get; set; } = Difficulty.Normal;

    /// <summary>Set by the test runs: difficulty reads as Normal whatever is saved.</summary>
    public static bool ForceNormal { get; set; }

    private static bool _loaded;

    public static Difficulty Effective => ForceNormal ? Difficulty.Normal : Difficulty;

    /// <summary>Enemy health and damage multipliers, and extra spare lives, for the difficulty in force.</summary>
    public static (float health, float damage, int lives) Scale => Effective switch
    {
        Difficulty.Easy => (0.8f, 0.6f, 2),
        Difficulty.Hard => (1.25f, 1.35f, -1),
        _ => (1f, 1f, 0),
    };

    public static string Describe(Difficulty d) => d switch
    {
        Difficulty.Easy => "Gentler clowns, two extra lives. For a relaxed run.",
        Difficulty.Hard => "Tougher, harder-hitting clowns and one life fewer.",
        _ => "The circus as intended.",
    };

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) == Error.Ok)
        {
            Music = (float)cfg.GetValue("audio", "music", Music);
            Sound = (float)cfg.GetValue("audio", "sound", Sound);
            Shake = (float)cfg.GetValue("video", "shake", Shake);
            Fullscreen = (bool)cfg.GetValue("video", "fullscreen", Fullscreen);
            Difficulty = (Difficulty)(int)cfg.GetValue("game", "difficulty", (int)Difficulty);
        }
        Apply();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "music", Music);
        cfg.SetValue("audio", "sound", Sound);
        cfg.SetValue("video", "shake", Shake);
        cfg.SetValue("video", "fullscreen", Fullscreen);
        cfg.SetValue("game", "difficulty", (int)Difficulty);
        cfg.Save(FilePath);
    }

    /// <summary>Push volumes and the window mode to the engine.</summary>
    public static void Apply()
    {
        EnsureBuses();
        SetVolume(MusicBus, Music);
        SetVolume(SfxBus, Sound);
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }

    /// <summary>The music and sound buses, made on first use and routed to the master bus.</summary>
    public static void EnsureBuses()
    {
        foreach (string name in new[] { MusicBus, SfxBus })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            int i = AudioServer.BusCount;
            AudioServer.AddBus(i);
            AudioServer.SetBusName(i, name);
            AudioServer.SetBusSend(i, "Master");
        }
    }

    private static void SetVolume(string bus, float v)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, v <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(v, 0.001f)));
    }
}
