namespace ClownSquad;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Progression;
using ClownSquad.Net;
using ClownSquad.Services;
using ClownSquad.World;

/// <summary>The Daily Circus in the fight scene: today's act on the squad, and the score at the end.</summary>
public partial class GameRoot
{
    private void SetUpDaily()
    {
        foreach (PlayerFighter p in _players)
        {
            ApplyLevel(p, DailyCircus.Level);
            if (_act?.Act == DailyCircus.Act.GlassCannons)
            {
                p.ExtraDamageMultiplier = 2f;
                p.MaxHealth *= 0.6f;
                p.Heal(p.MaxHealth);
            }
        }
        if (_act is not null)
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                if (!IsInsideTree()) return;
                Fx.Pop(this, new Vector3(_camX, 3.2f, 0f), _act.Name, new Color(1f, 0.85f, 0.3f), 1.5f, 1.6f);
                Fx.Pop(this, new Vector3(_camX, 2.5f, 0f), _act.Blurb, new Color(0.95f, 0.92f, 1f), 0.7f, 1.6f);
                Fx.Sound("fanfare", 1.1f, -6f);
            };
        GD.Print($"Daily Circus {DailyCircus.Today}: stage {_stage?.Id}, act {_act?.Name}, everyone at level {DailyCircus.Level}.");
    }

    private void ScoreDaily(bool won)
    {
        _dailyScore = DailyCircus.Score(won, _kos, _bestCombo, GameClock.Seconds, _stage?.Waves.Length ?? 1, !_anyoneFell);
        // Tests score but keep no best.
        if (_outcomeProbe.Length == 0 && !Session.IsTest)
        {
            _dailyBest = DailyCircus.Submit(_dailyScore);
            if (Platform.Counts) Platform.Current.SubmitScore(DailyCircus.Board(DailyCircus.Today), _dailyScore);
        }
        GD.Print($"Daily Circus score: {_dailyScore} ({(won ? "won" : "lost")}, {_kos} KOs, best combo {_bestCombo}, {GameClock.Seconds:F0}s{(_anyoneFell ? "" : ", nobody fell")}).");
    }
}
