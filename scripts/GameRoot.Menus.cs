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
/// Menus over the fight: the Stat Tent, pause, retry and restart, and the results screen.
/// </summary>
public partial class GameRoot
{
    /// <summary>Tab or Back/Select opens the Stat Tent at any time; it also opens itself after a stage.</summary>
    private void PollStatTent()
    {
        if (_tent is null || _tent.IsOpen || _outcomeProbe.Length > 0) return;

        bool held = false;
        foreach (PlayerFighter p in _players)
            if (p.Source is LocalDeviceInput d && MenuPad.OpenHeld(d.Device)) held = true;
        bool pressed = held && !_tentHeld;
        _tentHeld = held;

        bool due = _tentDueAt >= 0 && GameClock.Seconds >= _tentDueAt;
        if (due) _tentDueAt = -1;
        if (pressed || due) _tent.Open();
    }

    /// <summary>Once the fight is over, any attack or start press plays it again.</summary>
    private void PollForRestart()
    {
        if (_endedAt < 0 || GameClock.Seconds - _endedAt < 1.5 || _outcomeProbe.Length > 0) return;

        bool pressed = Godot.Input.IsKeyPressed(Key.Enter) || Bindings.Held(LocalDeviceInput.Keyboard, GameAction.Attack);
        foreach (int pad in Godot.Input.GetConnectedJoypads())
            pressed |= Bindings.Held(pad, GameAction.Attack) || Bindings.Held(pad, GameAction.Pause);
        // A fresh press only, and not straight out of the Stat Tent, whose
        // buttons overlap: the press that spent a point must not restart.
        bool fresh = pressed && !_restartHeld;
        _restartHeld = pressed;
        if (_tent is not null && (_tent.IsOpen || _tent.MsSinceClosed < 400 || _tentDueAt >= 0)) return;
        if (!fresh) return;
        // A campaign stage returns to the map, won or lost; a boss rush to the
        // title; the free fight replays.
        if (_rush) BossRush.Leave(GetTree());
        else if (_stage is not null) GoToMap();
        else GetTree().ReloadCurrentScene();
    }

    /// <summary>After a campaign stage, DODGE (K / B) plays it again from the start.</summary>
    private void PollForRetry()
    {
        if ((_stage is null && !_rush) || _endedAt < 0 || GameClock.Seconds - _endedAt < 1.5 || _outcomeProbe.Length > 0) return;
        if (_tent is not null && (_tent.IsOpen || _tent.MsSinceClosed < 400 || _tentDueAt >= 0)) return;
        bool held = Bindings.Held(LocalDeviceInput.Keyboard, GameAction.Dodge);
        foreach (int pad in Godot.Input.GetConnectedJoypads()) held |= Bindings.Held(pad, GameAction.Dodge);
        bool fresh = held && !_retryHeld;
        _retryHeld = held;
        if (fresh) Retry();
    }

    private void Retry()
    {
        SaveSystem.Save();
        Campaign.Pending = _stage;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>The squad as menu seats: character, controller, and refreshing the fighter after a change.</summary>
    private List<Seat> Seats()
    {
        var seats = new List<Seat>();
        foreach (PlayerFighter p in _players)
            if (p.Record is CharacterRecord r && p.Source is LocalDeviceInput d)
                seats.Add(new Seat(r, d.Device, () => p.ApplyProgression(r)));
        return seats;
    }

    /// <summary>Esc or Start pauses: resume, the Stat Tent, the inventory, retry, or the map.</summary>
    private void PollPause()
    {
        if (_pause is not null || _shop is not null || (_tent?.IsOpen ?? false) || _outcomeProbe.Length > 0) return;
        var devices = new List<int>();
        foreach (PlayerFighter p in _players) if (p.Source is LocalDeviceInput d) devices.Add(d.Device);
        bool held = PauseMenu.PauseHeld(devices);
        bool fresh = held && !_pauseHeld;
        _pauseHeld = held;
        if (!fresh) return;
        var items = new List<(PauseMenu.Choice, string)> { (PauseMenu.Choice.Resume, "RESUME"), (PauseMenu.Choice.Stats, "STAT TENT"), (PauseMenu.Choice.Inventory, "INVENTORY"), (PauseMenu.Choice.Skills, "SKILLS"), (PauseMenu.Choice.Settings, "SETTINGS") };
        if (_stage is not null) items.Add((PauseMenu.Choice.Retry, "RETRY STAGE"));
        // A boss rush is left for the title, never the map: the map would carry the rush on into the next stage.
        items.Add((PauseMenu.Choice.Map, _rush ? "QUIT BOSS RUSH" : "BACK TO THE MAP"));
        _pause = new PauseMenu(items, devices, choice =>
        {
            _pause = null;
            _pauseHeld = true;
            switch (choice)
            {
                case PauseMenu.Choice.Stats: _tent?.Open(); break;
                case PauseMenu.Choice.Inventory: AddChild(new InventoryMenu(Seats())); break;
                // Mid-stage: skills can be learned, but the weapon stays in hand until the stage is over.
                case PauseMenu.Choice.Skills: AddChild(new SkillMenu(Seats(), betweenStages: false)); break;
                case PauseMenu.Choice.Settings:
                    GetTree().Paused = true;
                    var settings = new SettingsMenu(devices);
                    settings.Closed += () => { GetTree().Paused = false; _pauseHeld = true; };
                    AddChild(settings);
                    break;
                case PauseMenu.Choice.Retry: Retry(); break;
                case PauseMenu.Choice.Map:
                    SaveSystem.Save();
                    if (_rush) BossRush.Leave(GetTree()); else GoToMap();
                    break;
            }
        });
        AddChild(_pause);
    }

    /// <summary>The stage's report card, with what was won (or lost) and what to do next.</summary>
    private void ShowResults(bool won)
    {
        if (_results is not null || !IsInsideTree()) return;
        double secs = GameClock.Seconds;
        var layer = new CanvasLayer { Layer = 8 };
        AddChild(layer);
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-210, -40);
        panel.CustomMinimumSize = new Vector2(420, 0);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.93f), BorderColor = won ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.6f, 0.7f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 14, ContentMarginBottom = 16,
        });
        layer.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);
        LabelSettings S(int size, Color c) => new()
        {
            Font = UiFont.Display, FontSize = size, FontColor = c, OutlineSize = 5, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        };
        void Row(string a, string b)
        {
            var h = new HBoxContainer();
            h.AddChild(new Label { Text = a, LabelSettings = S(18, new Color(0.85f, 0.8f, 0.95f)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            h.AddChild(new Label { Text = b, LabelSettings = S(18, Colors.White) });
            box.AddChild(h);
        }
        if (_rush)
        {
            Row("BOSSES BEATEN", $"{(won ? BossRush.Bosses : Mathf.Max(0, (_director?.WaveNumber ?? 1) - 1))} / {BossRush.Bosses}");
            Row("RUSH TIME", won ? BossRush.Clock(_rushTime) : "-");
            Row("BEST TIME", GameSettings.BossRushBest > 0 ? BossRush.Clock(GameSettings.BossRushBest) : "-");
            if (won && _rushRecord)
                box.AddChild(new Label { Text = "NEW RECORD!", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = S(22, new Color(1f, 0.85f, 0.3f)) });
            box.AddChild(new Label
            {
                Text = "ATTACK  back to the title      DODGE  run it again",
                HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = S(14, new Color(1f, 0.85f, 0.3f)),
            });
            _results = panel;
            panel.Scale = new Vector2(0.6f, 0.6f);
            panel.PivotOffset = new Vector2(210, 80);
            CreateTween().TweenProperty(panel, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            return;
        }
        Row("TIME", $"{(int)secs / 60}:{(int)secs % 60:00}");
        Row("KNOCKOUTS", _kos.ToString());
        Row("BEST COMBO", $"{_bestCombo} HITS");
        Row(won ? "GOLD BANKED" : "GOLD LOST", $"{_goldEarned} G");
        Row(won ? "LOOT BANKED" : "LOOT LOST", _itemsFound.ToString());
        if (won && _itemsFound > 0)
            box.AddChild(new Label { Text = "NEW GEAR IS IN YOUR BAG: PAUSE > INVENTORY", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = S(13, new Color(0.6f, 1f, 0.7f)) });
        box.AddChild(new Label
        {
            Text = won ? "ATTACK  back to the map      DODGE  play again" : "ATTACK  back to the map      DODGE  try again",
            HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = S(14, new Color(1f, 0.85f, 0.3f)),
        });
        _results = panel;
        panel.Scale = new Vector2(0.6f, 0.6f);
        panel.PivotOffset = new Vector2(210, 80);
        CreateTween().TweenProperty(panel, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (_outcomeProbe.Length > 0)
            GetTree().CreateTimer(0.5).Timeout += () => GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "results.png");
    }

    // Combo counter: consecutive hits by players, reset after a short lull.
    private const double ComboLull = 1.6;
    private int _comboHits;
    private double _comboExpires;

    private void OnHitLanded(Fighter? attacker, Fighter victim, float amount)
    {
        // Wall bounces arrive with no attacker and extend whatever is running.
        bool byPlayer = attacker is null ? victim.Hostile && _comboHits > 0 : !attacker.Hostile;
        if (!byPlayer) return;
        // Big hits lean the camera in: finishers and crits, harder for the biggest.
        if (amount >= 20f) CameraShake.RequestPunch(this, Mathf.Min(1f, amount / 45f));
        _comboHits++;
        _bestCombo = Mathf.Max(_bestCombo, _comboHits);
        _comboExpires = GameClock.Seconds + ComboLull;
        // The crowd loves a long combo.
        if (_comboHits % 5 == 0) CrowdCheer(1.4f + _comboHits * 0.04f, 2 + _comboHits / 5);
    }
}
