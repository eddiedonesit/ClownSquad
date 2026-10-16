namespace ClownSquad;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.Services;

/// <summary>What winning a stage earns beyond loot: achievements, stats and the boss-rush leaderboard.</summary>
public partial class GameRoot
{
    /// <summary>Whether any player took a hit this stage (for NOT A SCRATCH).</summary>
    private bool _anyoneHurt;

    private void OnWonForAchievements()
    {
        foreach (PlayerFighter p in _players) p.Celebrate();      // the victory pose
        if (_rush)
        {
            double secs = _rushTime;      // set just before, when the rush was won
            Achievements.Award("rush_clear");
            if (secs < 20 * 60) Achievements.Award("rush_fast");
            if (Platform.Counts) Platform.Current.SubmitTime("boss_rush", secs);
            return;
        }
        if (_daily) { Achievements.Award("daily_clear"); return; }
        if (_stage is not StageDef st) return;
        Achievements.Award("first_clear");
        Platform.AddStat("stages");
        if (st.Kind == StageKind.Bonus) { Achievements.Award("bonus_clear"); return; }
        if (!_anyoneHurt) Achievements.Award("flawless");
        if (st.Kind != StageKind.Boss) return;
        Platform.AddStat("bosses");
        Achievements.Award($"boss_{st.World}");
        if (GameSettings.Effective == Difficulty.Hard) Achievements.Award("hard_boss");
        foreach (PlayerFighter p in _players) Achievements.Award($"win_{p.Weapon.ToString().ToLowerInvariant()}");
    }
}
