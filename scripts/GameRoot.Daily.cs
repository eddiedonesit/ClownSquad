namespace ClownSquad;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Progression;
using ClownSquad.Net;
using ClownSquad.Services;
using ClownSquad.Visuals;
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
            if (_act?.Act == DailyCircus.Act.OneHitWonders)
            {
                p.MaxHealth *= 0.3f;
                p.Heal(p.MaxHealth);
            }
            if (_act?.Act == DailyCircus.Act.LightsOut)
                p.AddChild(new OmniLight3D
                {
                    LightColor = new Color(1f, 0.92f, 0.75f), LightEnergy = 2.6f, OmniRange = 4.6f, OmniAttenuation = 1.1f,
                    Position = new Vector3(0f, 1.8f, 0.9f), LightVolumetricFogEnergy = 0f,
                });
        }
        // DOUBLE TIME: the whole show runs fast.
        if (_act?.Act == DailyCircus.Act.DoubleTime) Engine.TimeScale = GameClock.Speed * 1.3f;
        // LIGHTS OUT: the stage goes dark; each clown carries a light of their own.
        if (_act?.Act == DailyCircus.Act.LightsOut)
        {
            if (GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment is Godot.Environment env)
            {
                env.AmbientLightEnergy *= 0.12f;
                env.BackgroundEnergyMultiplier = 0.25f;
            }
            if (GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") is DirectionalLight3D sun) sun.LightEnergy *= 0.1f;
            // whatever lamps the stage has, the screen is dark but for a pool round each clown
            AddChild(new LightsOut(_players));
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
