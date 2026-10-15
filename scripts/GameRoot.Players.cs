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
/// Players joining, dying and respawning, and the old sandbox room.
/// </summary>
public partial class GameRoot
{
    // ------------------------------------------------------------- players

    private void SpawnPlayers()
    {
        // The squad from the map: keyboard first, then any pads that joined
        // there. More pads can still join here on a button press rather than on
        // detection: one physical controller can appear as two devices, and
        // auto-binding both drives two fighters from one stick.
        for (int i = 0; i < Campaign.SquadDevices.Count; i++) AddPlayer(Campaign.SquadDevices[i], i);

        foreach (int pad in Godot.Input.GetConnectedJoypads())
            GD.Print($"  pad {pad}: name=\"{Godot.Input.GetJoyName(pad)}\" guid={Godot.Input.GetJoyGuid(pad)}");
        GD.Print("Press A / X on a controller to join.");
    }

    /// <summary>
    /// Let an unbound pad join on a fresh button press. One join per cooldown
    /// window, so two devices reporting the same physical press cannot both join.
    /// </summary>
    private void PollForJoins(float dt)
    {
        if (_joinCooldown > 0f) { _joinCooldown -= dt; return; }

        foreach (int pad in Godot.Input.GetConnectedJoypads())
        {
            if (_boundDevices.Contains(pad)) continue;

            bool held = Godot.Input.IsJoyButtonPressed(pad, JoyButton.A)
                     || Godot.Input.IsJoyButtonPressed(pad, JoyButton.X)
                     || Godot.Input.IsJoyButtonPressed(pad, JoyButton.Start);
            bool wasHeld = _joinHeld.Contains(pad);
            if (held) _joinHeld.Add(pad); else _joinHeld.Remove(pad);

            if (held && !wasHeld)
            {
                GD.Print($"Player joined on pad {pad} ({Godot.Input.GetJoyName(pad)}).");
                AddPlayer(pad, _players.Count);
                if (!Campaign.SquadDevices.Contains(pad)) Campaign.SquadDevices.Add(pad);
                _joinCooldown = 0.6f;
                return;
            }
        }
    }

    private void AddPlayer(int device, int index)
    {
        if (!_boundDevices.Add(device))
        {
            GD.Print($"Device {device} already bound; ignoring duplicate.");
            return;
        }

        var p = new PlayerFighter
        {
            Name = $"Player{index}",
            Source = new LocalDeviceInput(device),
            PlayerIndex = index,
            DisplayName = Roster.DefaultFor(index).Name,
        };
        // Each player slot plays its saved character, created on first use,
        // looking like the clown chosen for it on the character select screen.
        CharacterRecord record = SaveSystem.CharacterFor(index, p.DisplayName);
        p.DisplayName = record.Name;
        p.Look = record.Look;
        if (Session.IsTest) ApplyProbeWeapon(record);
        p.ApplyProgression(record);
        AddChild(p);
        Vector3 home = new(-1.5f + index * 1.5f, 0f, 1.5f);
        p.GlobalPosition = home;
        _homes[p] = home;
        p.Died += OnPlayerDied;
        _players.Add(p);
    }

    /// <summary>
    /// In the sandbox death is just a respawn. In the wave fight it spends one
    /// of the shared lives, and when a player falls with none left and nobody
    /// else is standing or on the way back, the fight is lost.
    /// </summary>
    private void OnPlayerDied(Fighter f)
    {
        // A clown taking a tumble: the crowd cannot help itself.
        ClownSquad.Audio.Sfx.Play("crowd_laugh");
        Vector3 home = f is PlayerFighter p && _homes.TryGetValue(p, out Vector3 h) ? h : Vector3.Zero;
        if (_scrolling) home = new Vector3(_camX - 2f, 0f, home.Z);

        if (_director is null) { ScheduleRespawn(f, home, 1.5); return; }
        if (_director.TryUseLife()) { ScheduleRespawn(f, home, 2.0); return; }

        GD.Print($"{f.Name} is out: no lives left.");
        bool anyoneLeft = _pendingRespawns.Count > 0;
        foreach (PlayerFighter other in _players)
            if (!other.IsDead) anyoneLeft = true;
        if (!anyoneLeft) _director.DeclareDefeat();
    }

    private void ScheduleRespawn(Fighter f, Vector3 at, double delay)
    {
        GD.Print($"{f.Name} down; respawning in {delay:F1}s.");
        _pendingRespawns.Add((f, at, GameClock.Seconds + delay));
    }

    private void TickRespawns()
    {
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            (Fighter f, Vector3 at, double due) = _pendingRespawns[i];
            if (GameClock.Seconds < due) continue;
            _pendingRespawns.RemoveAt(i);
            if (GodotObject.IsInstanceValid(f))
            {
                f.Revive(at);
                GD.Print($"{f.Name} respawned.");
            }
        }
    }

    // ------------------------------------------------------------- sandbox

    private void SpawnSandbox()
    {
        var d = new TrainingDummy { Name = "Dummy" };
        AddChild(d);
        d.GlobalPosition = new Vector3(-3.5f, 0f, -2.5f);
        _dummies.Add(d);

        Vector3 at = new(3.5f, 0f, -2f);
        var boss = new BossClown { Name = "BossClown" };
        boss.Source = new BrawlerAi(boss, attackRange: 2.2f, backoffRange: 1.2f,
                                    attackCooldownMs: 700,
                                    specialCooldownMs: 5000, specialRange: 6.0f);
        AddChild(boss);
        boss.GlobalPosition = at;
        boss.Died += f => ScheduleRespawn(f, at, 3.0);
        _bosses.Add(boss);
        GD.Print("Sandbox: training dummy and boss clown.");
    }
}
