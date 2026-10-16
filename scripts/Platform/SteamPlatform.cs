namespace ClownSquad.Services;

using System;
using Godot;
using Steamworks;
using Steamworks.Data;

/// <summary>
/// The game on Steam (docs/STEAM.md), through Facepunch.Steamworks: achievements, stats,
/// the boss-rush leaderboard, rich presence and the overlay. Everything is
/// also kept by the <see cref="OfflinePlatform"/> underneath, so the game's own
/// ACHIEVEMENTS list works the same, and anything earned before Steam was
/// running (or before this build) is unlocked on Steam at start-up.
///
/// Until the game has its own App ID it runs as Valve's test app 480
/// ("Spacewar"): achievements and stats sent there are not ours and do not
/// show, but initialising, presence, the overlay and the friends list all work.
/// </summary>
public sealed class SteamPlatform : IPlatform
{
    /// <summary>Valve's test app until Clown Squad's own App ID exists: change this one number.</summary>
    public const uint AppId = 480;

    private readonly OfflinePlatform _local;
    private bool _statsDirty;
    private double _storeAt;

    public event Action<bool>? OverlayToggled;

    private SteamPlatform(OfflinePlatform local)
    {
        _local = local;
        SteamFriends.OnGameOverlayActivated += open => OverlayToggled?.Invoke(open);
    }

    /// <summary>Steam, if it is running and lets the game in; otherwise null (the game stays offline).</summary>
    public static SteamPlatform? TryStart(OfflinePlatform local)
    {
        try
        {
            SteamClient.Init(AppId, asyncCallbacks: false);
        }
        catch (Exception e)
        {
            // No Steam running, no steam_api64.dll, or not allowed: play offline.
            GD.Print($"Steam: not available ({e.GetType().Name}: {e.Message}). Playing offline.");
            return null;
        }
        if (!SteamClient.IsValid) return null;
        var steam = new SteamPlatform(local);
        GD.Print($"Steam: signed in as {SteamClient.Name} (app {AppId}).");
        ClownSquad.Net.SteamInvites.Hook();
        steam.CatchUp();
        return steam;
    }

    /// <summary>Unlock on Steam anything earned while offline.</summary>
    private void CatchUp()
    {
        foreach (string id in _local.Unlocked)
            try { new Steamworks.Data.Achievement(id).Trigger(apply: false); }
            catch (Exception) { /* not defined on this app (the test app has none of ours) */ }
        SteamUserStats.StoreStats();
    }

    public string Name => "Steam";
    public string? PlayerName => SteamClient.IsValid ? SteamClient.Name : null;

    /// <summary>The offline record underneath, which the in-game lists read.</summary>
    public OfflinePlatform Local => _local;

    public void Unlock(string achievement)
    {
        _local.Unlock(achievement);
        try
        {
            if (new Steamworks.Data.Achievement(achievement).Trigger()) GD.Print($"Steam: achievement {achievement}.");
        }
        catch (Exception) { }
    }

    public bool IsUnlocked(string achievement) => _local.IsUnlocked(achievement);

    public void AddStat(string stat, int amount)
    {
        _local.AddStat(stat, amount);
        SteamUserStats.AddStat(stat, amount);
        _statsDirty = true;
    }

    public int GetStat(string stat) => _local.GetStat(stat);

    public void SubmitTime(string board, double seconds)
    {
        _local.SubmitTime(board, seconds);
        int ms = (int)Math.Round(seconds * 1000.0);
        _ = Submit(board, ms);
    }

    private static async System.Threading.Tasks.Task Submit(string board, int ms)
    {
        try
        {
            Leaderboard? lb = await SteamUserStats.FindOrCreateLeaderboardAsync(board, LeaderboardSort.Ascending, LeaderboardDisplay.TimeMilliSeconds);
            if (lb is Leaderboard found) await found.SubmitScoreAsync(ms);
        }
        catch (Exception e) { GD.Print($"Steam: leaderboard {board} failed ({e.Message})."); }
    }

    public void SubmitScore(string board, int score)
    {
        _local.SubmitScore(board, score);
        _ = SubmitBest(board, score);
    }

    private static async System.Threading.Tasks.Task SubmitBest(string board, int score)
    {
        try
        {
            Leaderboard? lb = await SteamUserStats.FindOrCreateLeaderboardAsync(board, LeaderboardSort.Descending, LeaderboardDisplay.Numeric);
            if (lb is Leaderboard found) await found.SubmitScoreAsync(score);
        }
        catch (Exception e) { GD.Print($"Steam: leaderboard {board} failed ({e.Message})."); }
    }

    public void SetPresence(string text)
    {
        // Shown to friends through the "#Status" token (docs/STEAM.md §2.5).
        SteamFriends.SetRichPresence("status", text);
        SteamFriends.SetRichPresence("steam_display", "#Status");
    }

    public void Tick()
    {
        SteamClient.RunCallbacks();
        // Stats are sent in batches, not on every knockout.
        double now = Time.GetTicksMsec() / 1000.0;
        if (_statsDirty && now >= _storeAt)
        {
            SteamUserStats.StoreStats();
            _statsDirty = false;
            _storeAt = now + 30.0;
        }
    }

    public void Shutdown()
    {
        if (_statsDirty) SteamUserStats.StoreStats();
        SteamClient.Shutdown();
    }
}
