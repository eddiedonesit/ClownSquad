namespace ClownSquad;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Visuals;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.World;

/// <summary>
/// The practice ring (Title > PRACTICE RING): your own clowns, copied from the
/// last save played, in the circus ring with three training dummies. Weapons
/// change on the spot, the whole skill tree can be switched on and off, grunts
/// can be called in, and a meter shows the damage dealt. Nothing done here is
/// kept (Session.BeginPractice saves to a scratch file).
/// </summary>
public partial class GameRoot
{
    private const int MaxPracticeGrunts = 6;
    private static readonly GameAction[] PracticeKeys =
        { GameAction.PrevWeapon, GameAction.NextWeapon, GameAction.PracticeSkills, GameAction.PracticeGrunts };

    private readonly Dictionary<int, HashSet<GameAction>> _practiceHeld = new();
    private readonly List<(double at, float amount, Fighter? by)> _practiceHits = new();
    private readonly List<GruntClown> _practiceGrunts = new();
    private Label? _practicePanel;
    private float _lastHit;

    private void SetUpPractice()
    {
        foreach (Vector3 at in new[] { new Vector3(1.6f, 0f, 0f), new Vector3(4.2f, 0f, -1.4f), new Vector3(4.2f, 0f, 1.4f) })
        {
            var d = new TrainingDummy { Name = "Dummy" };
            AddChild(d);
            d.GlobalPosition = at;
            _dummies.Add(d);
        }
        Fighter.HitLanded += OnPracticeHit;

        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        _practicePanel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings
            {
                Font = UiFont.Display, FontSize = 16, FontColor = new Color(1f, 0.95f, 0.85f),
                OutlineSize = 5, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
            },
        };
        _practicePanel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _practicePanel.OffsetTop = -132;
        _practicePanel.OffsetBottom = -12;
        layer.AddChild(_practicePanel);
        GD.Print("Practice ring: three dummies; weapons, skills and grunts on the practice keys.");
    }

    private void OnPracticeHit(Fighter? attacker, Fighter victim, float amount)
    {
        if (attacker is not PlayerFighter || victim.Hostile == false) return;
        _practiceHits.Add((GameClock.Seconds, amount, attacker));
        _lastHit = amount;
    }

    private void TickPractice()
    {
        // Each player's practice keys, edge-detected on their own device.
        foreach (PlayerFighter p in _players.ToArray())
        {
            if (p.Source is not LocalDeviceInput input || p.Record is not CharacterRecord rec) continue;
            if (!_practiceHeld.TryGetValue(input.Device, out HashSet<GameAction>? held)) _practiceHeld[input.Device] = held = new();
            foreach (GameAction a in PracticeKeys)
            {
                bool down = Bindings.Held(input.Device, a);
                bool fresh = down && !held.Contains(a);
                if (down) held.Add(a); else held.Remove(a);
                if (!fresh || GetTree().Paused) continue;
                switch (a)
                {
                    case GameAction.PrevWeapon: SwapWeapon(p, rec, -1); break;
                    case GameAction.NextWeapon: SwapWeapon(p, rec, 1); break;
                    case GameAction.PracticeSkills: ToggleTree(p, rec); break;
                    case GameAction.PracticeGrunts: CallGrunts(); break;
                }
            }
        }
        // A weapon chosen in the skill menu: hand the fighter the new weapon.
        foreach (PlayerFighter p in _players.ToArray())
            if (p.Record is CharacterRecord r && r.Weapon != p.Weapon) ReplacePlayer(p);

        _practiceGrunts.RemoveAll(g => !IsInstanceValid(g) || g.IsDead);
        _practiceHits.RemoveAll(h => GameClock.Seconds - h.at > 5.0);
        if (_practicePanel is not null) _practicePanel.Text = PracticeText();
    }

    private string PracticeText()
    {
        var sb = new StringBuilder();
        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            WeaponDef w = Weapons.Get(r.Weapon);
            int known = Weapons.Learned(r, r.Weapon).Count;
            sb.AppendLine($"P{p.PlayerIndex + 1} {r.Name}:  {w.Name}   (hold attack: {w.Charge.ToString().ToUpperInvariant()})   skills {known}/9");
        }
        float recent = 0f;
        foreach ((double _, float amount, Fighter? _) in _practiceHits) recent += amount;
        sb.AppendLine($"LAST HIT {_lastHit:F0}     LAST 5 S {recent:F0}  ({recent / 5f:F1} PER SECOND)     GRUNTS {_practiceGrunts.Count}/{MaxPracticeGrunts}");
        string K(GameAction a) => Bindings.KeyName(Bindings.KeysFor(a)) + " / " + Bindings.ButtonName(Bindings.ButtonsFor(a));
        sb.Append($"WEAPON  {K(GameAction.PrevWeapon)}  {K(GameAction.NextWeapon)}     ALL SKILLS  {K(GameAction.PracticeSkills)}     " +
                  $"GRUNTS  {K(GameAction.PracticeGrunts)}     LEAVE  {K(GameAction.Pause)}");
        return sb.ToString();
    }

    private void SwapWeapon(PlayerFighter p, CharacterRecord rec, int dir)
    {
        rec.Weapon = (WeaponKind)Mathf.PosMod((int)rec.Weapon + dir, Weapons.All.Length);
        ClownSquad.Audio.Sfx.Play(rec.Weapon == WeaponKind.Seltzer ? "fizz" : "clack", 1.1f);
        ReplacePlayer(p);
    }

    /// <summary>The whole tree of the weapon in hand, on or off. Free here: practice does not cost points.</summary>
    private void ToggleTree(PlayerFighter p, CharacterRecord rec)
    {
        List<string> known = Weapons.Learned(rec, rec.Weapon);
        bool all = known.Count >= Weapons.Get(rec.Weapon).Tree.Length;
        known.Clear();
        if (!all) foreach (Skill s in Weapons.Get(rec.Weapon).Tree) known.Add(s.Id);
        p.ApplyProgression(rec);
        ClownSquad.Audio.Sfx.Play(all ? "slide_down" : "level_up", 1.2f, -6f);
        PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.5f, 0f), all ? "SKILLS OFF" : "ALL SKILLS!", new Color(1f, 0.85f, 0.3f), 0.8f, 0.7f);
    }

    /// <summary>Three grunts in from the right, to try a weapon on things that fight back.</summary>
    private void CallGrunts()
    {
        for (int i = 0; i < 3 && _practiceGrunts.Count < MaxPracticeGrunts; i++)
        {
            var g = new GruntClown { Name = "PracticeGrunt", DisplayName = "GRUNT" };
            g.Source = new BrawlerAi(g, attackRange: 1.3f, backoffRange: 0.7f, attackCooldownMs: 1300);
            AddChild(g);
            g.GlobalPosition = new Vector3(6f, 0f, -1.5f + i * 1.5f);
            g.Died += f => f.BeginDespawn();
            _practiceGrunts.Add(g);
        }
        ClownSquad.Audio.Sfx.Play("honk", 0.9f);
    }

    /// <summary>
    /// Swap a fighter for a fresh one built from its record: the only way to
    /// change weapon, since the sheet and combo are set when a fighter is made.
    /// It keeps its place in the player list, so its HUD panel stays put.
    /// </summary>
    private void ReplacePlayer(PlayerFighter old)
    {
        if (old.Source is not LocalDeviceInput input) return;
        int at = _players.IndexOf(old);
        Vector3 where = old.GlobalPosition;
        _players.RemoveAt(at);
        _boundDevices.Remove(input.Device);
        _homes.Remove(old);
        old.Died -= OnPlayerDied;
        old.QueueFree();
        AddPlayer(input.Device, old.PlayerIndex);
        PlayerFighter fresh = _players[^1];
        _players.RemoveAt(_players.Count - 1);
        _players.Insert(at, fresh);
        fresh.GlobalPosition = where;
    }
}
