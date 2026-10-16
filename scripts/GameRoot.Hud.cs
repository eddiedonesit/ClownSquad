namespace ClownSquad;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// The debug HUD and banner text (the circus HUD itself is ClownHud).
/// </summary>
public partial class GameRoot
{
    // ----------------------------------------------------------------- hud

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        _hud = MakeLabel(15, 5);
        _hud.Position = new Vector2(16, 12);
        layer.AddChild(_hud);

        _banner = MakeLabel(44, 10);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _banner.OffsetTop = 120;
        layer.AddChild(_banner);

        // The old text readout survives as a debug overlay on F3; the circus HUD
        // is what players see.
        _hud.Visible = false;
        _banner.Visible = false;
        _clownHud = new ClownSquad.UI.ClownHud(() => _players, () => _director,
            () => _endedAt >= 0 && GameClock.Seconds - _endedAt > 1.5)
        {
            BagText = BagText,
            StageLabel = _rush ? "BOSS RUSH  " : _daily ? "DAILY CIRCUS  " : _stage is StageDef s ? (s.Kind == StageKind.Boss ? $"{s.World}-BOSS  " : $"{s.Id}  ") : "",
            ReplayText = _stage is not null ? "" : "press ATTACK to play again",
        };
        AddChild(_clownHud);

        _tent = new StatTent(() =>
        {
            var seats = new List<Seat>();
            foreach (PlayerFighter p in _players)
                if (p.Record is CharacterRecord r && p.Source is LocalDeviceInput d)
                    seats.Add(new Seat(r, d.Device, () => p.ApplyProgression(r)));
            return seats;
        });
        AddChild(_tent);
    }

    private static Label MakeLabel(int size, int outline)
    {
        var label = new Label { Theme = new Theme() };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    private void UpdateHud()
    {
        if (_hud is null || _banner is null) return;

        var sb = new StringBuilder();
        if (_director is WaveDirector d)
        {
            sb.AppendLine($"WAVE {d.WaveNumber}/{d.WaveCount}    ENEMIES {d.EnemiesAlive}    LIVES {d.Lives}");
            _banner.Text = d.Banner;
            if (_endedAt >= 0 && GameClock.Seconds - _endedAt > 1.5)
                _banner.Text += "\npress attack to play again";
        }
        else
        {
            sb.AppendLine("SANDBOX");
            _banner.Text = "";
        }

        sb.AppendLine();
        foreach (PlayerFighter p in _players)
            sb.AppendLine($"P{p.PlayerIndex + 1} [{p.Source.Describe()}]  hp {p.Health:F0}  {p.DebugState}");
        for (int i = 0; i < _dummies.Count; i++)
            sb.AppendLine($"Dummy  hp {_dummies[i].Health:F0} / {_dummies[i].MaxHealth:F0}");
        foreach (BossClown b in _bosses)
            sb.AppendLine($"BOSS  hp {b.Health:F0} / {b.MaxHealth:F0}  {b.DebugState}");

        if (_comboHits > 0 && GameClock.Seconds > _comboExpires) _comboHits = 0;
        if (_clownHud is not null) _clownHud.Combo = _comboHits;
        bool f3 = Godot.Input.IsKeyPressed(Key.F3);
        if (f3 && !_f3Down) _hud.Visible = !_hud.Visible;
        _f3Down = f3;
        if (_comboHits > 1)
        {
            sb.AppendLine();
            sb.AppendLine($">>> {_comboHits} HITS <<<");
        }

        sb.AppendLine();
        sb.AppendLine("WASD move | J attack | Space jump | K dodge      pad: stick | X | A | B");
        sb.AppendLine("hold attack: the weapon's charge move (throw, quake, pie lob, pin fan, clap, big cork, lunge) | hold attack + left/right: spin | in the air: attack flips, jump again double-jumps, dodge air-dashes, down + attack slams");
        _hud.Text = sb.ToString();
    }
}
