namespace ClownSquad.Services;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// What the game asks of the store it runs on: achievements, stats,
/// leaderboards, rich presence (what friends see you doing) and the overlay.
/// The game talks only to this, never to a store SDK. Today the game runs on
/// <see cref="OfflinePlatform"/>; the Steam version is one more class, written
/// once the Steamworks SDK and an App ID are in place (docs/STEAM.md).
/// </summary>
public interface IPlatform
{
    string Name { get; }
    /// <summary>The player's name on the platform, if it has one.</summary>
    string? PlayerName { get; }

    void Unlock(string achievement);
    bool IsUnlocked(string achievement);

    void AddStat(string stat, int amount);
    int GetStat(string stat);

    /// <summary>A time on a leaderboard where lower is better (the boss rush). Steam boards take it in milliseconds.</summary>
    void SubmitTime(string board, double seconds);

    /// <summary>What friends see this player doing ("Fighting stage 3-B").</summary>
    void SetPresence(string text);

    /// <summary>Called every frame: a store SDK pumps its callbacks here.</summary>
    void Tick();

    /// <summary>The store's overlay opened (true) or closed: the game pauses for it.</summary>
    event Action<bool>? OverlayToggled;
}

/// <summary>
/// No store: achievements, stats and best times kept in user://platform.cfg,
/// so they show in the game's own ACHIEVEMENTS list and can be handed to Steam
/// the first time the Steam build runs (anything earned offline unlocks there).
/// </summary>
public sealed class OfflinePlatform : IPlatform
{
    private readonly string FilePath;
    private readonly ConfigFile _cfg = new();

    /// <param name="file">Where to keep it; tests use their own file, started empty.</param>
    public OfflinePlatform(string file = "user://platform.cfg", bool fresh = false)
    {
        FilePath = file;
        if (!fresh) _cfg.Load(FilePath);
    }

    /// <summary>Everything earned, for test reports.</summary>
    public string[] Unlocked => _cfg.HasSection("achievements") ? _cfg.GetSectionKeys("achievements") : System.Array.Empty<string>();

    public string Name => "Offline";
    public string? PlayerName => null;
    public event Action<bool>? OverlayToggled { add { } remove { } }

    public void Unlock(string achievement)
    {
        if (IsUnlocked(achievement)) return;
        _cfg.SetValue("achievements", achievement, Time.GetDatetimeStringFromSystem());
        _cfg.Save(FilePath);
    }

    public bool IsUnlocked(string achievement) => _cfg.HasSectionKey("achievements", achievement);

    /// <summary>When an achievement was earned, for the in-game list.</summary>
    public string? UnlockedAt(string achievement) =>
        IsUnlocked(achievement) ? (string)_cfg.GetValue("achievements", achievement) : null;

    public void AddStat(string stat, int amount)
    {
        _cfg.SetValue("stats", stat, GetStat(stat) + amount);
        _cfg.Save(FilePath);
    }

    public int GetStat(string stat) => _cfg.HasSectionKey("stats", stat) ? (int)_cfg.GetValue("stats", stat) : 0;

    public void SubmitTime(string board, double seconds)
    {
        double best = _cfg.HasSectionKey("boards", board) ? (double)_cfg.GetValue("boards", board) : double.MaxValue;
        if (seconds >= best) return;
        _cfg.SetValue("boards", board, seconds);
        _cfg.Save(FilePath);
    }

    public void SetPresence(string text) { }

    public void Tick() { }
}

/// <summary>The platform the game is running on, and the rules for using it.</summary>
public static class Platform
{
    private static IPlatform? _current;

    /// <summary>The store layer. Offline until a store build installs its own at start-up.</summary>
    public static IPlatform Current
    {
        get => _current ??= new OfflinePlatform();
        set => _current = value;
    }

    /// <summary>Practice and automated tests never earn anything: they would be free achievements.</summary>
    public static bool Counts => (Session.Mode is PlayMode.Campaign or PlayMode.BossRush && !ClownSquad.Progression.GameSettings.God) || CountInTests;

    /// <summary>A test checking achievements turns them on, against a scratch platform file.</summary>
    public static bool CountInTests { get; set; }

    public static void AddStat(string stat, int amount = 1)
    {
        if (Counts) Current.AddStat(stat, amount);
    }

    public static void SetPresence(string text) => Current.SetPresence(text);
}
