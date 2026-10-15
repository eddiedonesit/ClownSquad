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
/// Test and screenshot modes: the command-line probes (wavetest, losetest,
/// aitest, tentshot, skillshot, stageshot, demo) and their checks.
/// </summary>
public partial class GameRoot
{
    /// <summary>
    /// "-- demo stage 1-B level 4": the smart bot plays the stage at normal
    /// speed for a recording (with Godot's --write-movie), on a throwaway save;
    /// it quits a few seconds after the stage is won or lost.
    /// </summary>
    private void StartDemo()
    {
        PlayerFighter p = _players[0];
        p.Source = new SmartBot(p);
        p.ExtraDamageMultiplier = 1.4f;
        ApplyProbeLevel(p);
        _director!.PhaseChanged += phase =>
        {
            if (phase is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat)
                GetTree().CreateTimer(6.0, true, false, true).Timeout += () => GetTree().Quit();
        };
    }

    /// <summary>"weapon hammer" and "skills all" on a test's command line: fight with that weapon, its whole tree learned.</summary>
    private static void ApplyProbeWeapon(CharacterRecord record)
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "weapon");
        if (at >= 0 && at + 1 < args.Length && Enum.TryParse(args[at + 1], true, out WeaponKind kind)) record.Weapon = kind;
        if (Array.IndexOf(args, "skills") is int k and >= 0 && k + 1 < args.Length && args[k + 1] == "all")
        {
            List<string> known = Weapons.Learned(record, record.Weapon);
            foreach (Skill s in Weapons.Get(record.Weapon).Tree) if (!known.Contains(s.Id)) known.Add(s.Id);
        }
        GD.Print($"Probe weapon: {Weapons.Get(record.Weapon).Name}, {Weapons.Learned(record, record.Weapon).Count} skills.");
    }

    /// <summary>"level N" on the command line: raise the player's record to that level, points spent evenly.</summary>
    private static void ApplyProbeLevel(PlayerFighter p)
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "level");
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out int want)) return;
        ApplyLevel(p, want);
    }

    /// <summary>Raise a player's record to a level, points spent evenly on POW and GUTS.</summary>
    private static void ApplyLevel(PlayerFighter p, int want)
    {
        if (p.Record is not CharacterRecord rec) return;
        while (rec.Level < want && Leveling.XpToNext(rec.Level) > 0) Leveling.Grant(rec, Leveling.XpToNext(rec.Level));
        bool pow = true;
        while (rec.StatPoints > 0) { if (pow) rec.Pow++; else rec.Guts++; rec.StatPoints--; pow = !pow; }
        p.ApplyProgression(rec);
        p.Heal(p.MaxHealth);
        GD.Print($"Squad at level {rec.Level}: POW {rec.Pow}, GUTS {rec.Guts}, {p.MaxHealth:F0} hp.");
    }

    /// <summary>"-- stageshot stage 3-1": two stills of a stage's scenery, then quit. Needs a window.</summary>
    private void StartStageShot()
    {
        string id = _stage?.Id.Replace("*", "bonus") ?? "fight";
        GetTree().CreateTimer(1.6).Timeout += () =>
        {
            GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}stage_{id}.png");
            _camX += 14f;                                  // further along the road
            if (_rig is not null) _rig.Position = new Vector3(_camX, 0f, 0f);
            _scenery?.Follow(_camX);
            GetTree().CreateTimer(0.5).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}stage_{id}_b.png");
                GetTree().Quit();
            };
        };
    }

    /// <summary>"-- skillshot weapon hammer level 10": the skill menu as seen on the map, a few skills learned. Needs a window.</summary>
    private void StartSkillShot()
    {
        PlayerFighter p = _players[0];
        ApplyProbeLevel(p);
        CharacterRecord r = p.Record!;
        Skill[] tree = Weapons.Get(r.Weapon).Tree;
        foreach (string id in new[] { tree[0].Id, tree[1].Id, tree[3].Id }) Weapons.Learn(r, Array.Find(tree, x => x.Id == id)!);
        p.ApplyProgression(r);
        GetTree().CreateTimer(1.5, processAlways: true).Timeout += () =>
        {
            AddChild(new SkillMenu(Seats(), betweenStages: true));
            GetTree().CreateTimer(0.6, processAlways: true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}skills_{r.Weapon.ToString().ToLowerInvariant()}.png");
                GetTree().Quit();
            };
        };
    }

    /// <summary>"-- tentshot": a levelled character, a HUD still, then the Stat Tent open. Needs a window.</summary>
    private void StartTentShot()
    {
        PlayerFighter p = _players[0];
        Leveling.Grant(p.Record!, 200);
        // A few banked items so the GEAR page has something to show.
        for (int i = 0; i < 7; i++)
        {
            ItemData item = Loot.Roll(_lootRng, 3, 2.5f);
            p.Record!.Stash.Add(item);
            if (p.Record.EquippedIn(item.Slot) is not ItemData worn || Loot.Score(item) > Loot.Score(worn))
                p.Record.Equip(item.Slot, item);
        }
        p.ApplyProgression(p.Record!);
        SceneTreeTimer first = GetTree().CreateTimer(3.0, processAlways: true);
        first.Timeout += () =>
        {
            GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "hud_level.png");
            _tent!.Open();
            GetTree().CreateTimer(0.6, processAlways: true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "stat_tent.png");
                _tent.ShowPage(1);
                GetTree().CreateTimer(0.4, processAlways: true).Timeout += () =>
                {
                    GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "gear_page.png");
                    GetTree().Quit();
                };
            };
        };
    }


    /// <summary>
    /// "-- practicetest": the practice ring's controls, driven directly: change
    /// weapon both ways, the full tree on and off, grunts called and capped,
    /// the HUD panel keeping its place. Quits with a pass or fail line.
    /// </summary>
    private void StartPracticeProbe()
    {
        int fails = 0;
        void Check(string what, bool ok, string detail) { if (!ok) fails++; GD.Print($"  [{(ok ? "PASS" : "FAIL")}] {what}: {detail}"); }
        GD.Print("=== PRACTICE PROBE ===");
        GetTree().CreateTimer(1.0).Timeout += () =>
        {
            PlayerFighter p = _players[0];
            CharacterRecord rec = p.Record!;
            Check("starts with the save's weapon", p.Weapon == rec.Weapon, $"{p.Weapon}");
            SwapWeapon(p, rec, 1);
            PlayerFighter q = _players[0];
            Check("next weapon", q != p && q.Weapon == WeaponKind.Hammer && _players.Count == 1, $"{q.Weapon}, {_players.Count} player(s)");
            SwapWeapon(q, q.Record!, -2);
            PlayerFighter r = _players[0];
            Check("previous wraps round", r.Weapon == WeaponKind.BalloonSword, $"{r.Weapon}");
            ToggleTree(r, r.Record!);
            Check("all skills on", Weapons.Learned(r.Record!, r.Weapon).Count == 9, $"{Weapons.Learned(r.Record!, r.Weapon).Count}/9");
            ToggleTree(r, r.Record!);
            Check("all skills off", Weapons.Learned(r.Record!, r.Weapon).Count == 0, $"{Weapons.Learned(r.Record!, r.Weapon).Count}/9");
            CallGrunts(); CallGrunts(); CallGrunts();
            Check("grunts capped", _practiceGrunts.Count == MaxPracticeGrunts, $"{_practiceGrunts.Count}");
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                Check("new fighter is live", IsInstanceValid(r) && r.IsInsideTree() && !r.IsDead, $"hp {r.Health:F0}");
                Check("old fighters gone", !IsInstanceValid(p) && !IsInstanceValid(q), "freed");
                Check("practice save, not a slot", SaveSystem.Path == Session.TestSave, SaveSystem.Path);
                GD.Print(fails == 0 ? "=== PRACTICE PROBE: ALL PASS ===" : $"=== PRACTICE PROBE: {fails} FAILURE(S) ===");
                GetTree().Quit(fails == 0 ? 0 : 1);
            };
        };
    }

    // -------------------------------------------------------------- probes

    private float _aiProbeTime;
    private int _aiProbeTick;
    private bool _hangShot;
    private bool _punchShot;

    /// <summary>Boss AI closes distance, swings and slams. "-- aitest"</summary>
    private void TickAiProbe(float dt)
    {
        _aiProbeTime += dt;
        if (_aiProbeTime < _aiProbeTick * 0.6f) return;
        if (_players.Count == 0 || _bosses.Count == 0) return;

        BossClown b = _bosses[0];
        PlayerFighter p = _players[0];
        float d = b.GlobalPosition.DistanceTo(p.GlobalPosition);
        GD.Print($"  t={_aiProbeTime:F1}  dist={d:F2}  boss={b.DebugState} clip={b.CurrentClip}  playerHp={p.Health:F0}");

        if (b.Hanging && !_hangShot && !DisplayServer.GetName().Contains("headless"))
        {
            _hangShot = true;
            GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "flop_hang.png");
        }

        if (b.DebugState.StartsWith("ATK") && !_punchShot && !DisplayServer.GetName().Contains("headless"))
        {
            _punchShot = true;
            GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "boss_punch.png");
        }

        if (++_aiProbeTick > 26)
        {
            GD.Print("=== AI PROBE DONE ===");
            GetTree().Quit();
        }
    }

    private int _shots;
    private bool _sawDeadBlink;

    /// <summary>
    /// Plays the whole fight unattended, fast. For "win" an AI drives the
    /// player; for "lose" nobody does. Either way the outcome is asserted.
    /// </summary>
    /// <summary>Damage the probe's players took, by what dealt it: where a run went wrong.</summary>
    private readonly Dictionary<string, float> _hurtBy = new();

    private void TallyHurt(Fighter? attacker, Fighter victim, float amount)
    {
        if (victim.Hostile) return;
        string by = attacker is null ? "wall/hazard" : attacker.GetType().Name;
        _hurtBy[by] = _hurtBy.TryGetValue(by, out float sum) ? sum + amount : amount;
    }

    private void StartOutcomeProbe()
    {
        Fighter.HitLanded += TallyHurt;
        Engine.TimeScale = 4.0;
        GD.Print($"=== WAVE PROBE: expecting {_outcomeProbe} ===");
        if (_outcomeProbe != "win")
        {
            // A loss must cost the bag: start the losing run already carrying an item.
            Bag(_players[0]).Add(Loot.Roll(_lootRng, 1, 0f));
            // On a scrolling stage nothing comes to a player who stands still,
            // so the idle player starts where the first group waits.
            if (_scrolling) { _camX = FirstEncounter; _players[0].GlobalPosition = new Vector3(FirstEncounter, 0f, 0f); }
            return;
        }

        PlayerFighter p = _players[0];
        // The careful bot: reads telegraphs, hazards and shots and dodges them.
        // "-- fair" plays it at its real damage; by default it hits harder so
        // the regression runs stay short.
        p.Source = new SmartBot(p);
        p.ExtraDamageMultiplier = Array.IndexOf(OS.GetCmdlineUserArgs(), "fair") >= 0 ? 1f : 2.0f;

        // "-- level 20": play the probe as a squad of a fitting level, points
        // spent evenly on POW and GUTS, so late worlds and bosses can be tested.
        ApplyProbeLevel(p);
        // Every grunt ranked Rare, so the run is certain to drop loot and to
        // exercise the ranked-enemy path.
        _director!.ForceRank = EnemyRank.Rare;
    }

    /// <summary>
    /// After a won stage: the player has levelled, a spent point changes the
    /// fighter, and what was saved reads back from disk identical.
    /// </summary>
    private bool CheckProgression()
    {
        PlayerFighter p = _players[0];
        CharacterRecord r = p.Record!;
        bool levelled = r.Level >= 2 && r.Pow + r.Guts + r.StatPoints == Leveling.StatPointsPerLevel * (r.Level - 1);
        r.StatPoints++;   // one point back to spend below

        float before = p.MaxHealth;
        r.StatPoints--;
        r.Guts++;
        p.ApplyProgression(r);
        bool applied = Mathf.IsEqualApprox(p.MaxHealth, before + 8f);

        bool wrote = SaveSystem.Save();
        SaveSystem.Load();
        CharacterRecord back = SaveSystem.Data.Characters[0];
        bool persisted = wrote && back.Name == r.Name && back.Level == r.Level && back.Xp == r.Xp
                      && back.TotalXp == r.TotalXp && back.StatPoints == r.StatPoints && back.Guts == r.Guts;

        GD.Print($"  progression: {r.Name} level {r.Level}, {r.TotalXp} xp total, {r.StatPoints} point(s) unspent; " +
                 $"levelled={levelled} guts point applied={applied} ({before:F0} -> {p.MaxHealth:F0} hp) reloaded from disk={persisted}");
        return levelled && applied && persisted;
    }

    /// <summary>
    /// After a won stage: drops fell, everything was banked, the bank survived
    /// a save and reload, and enemies were scaled to the host.
    /// </summary>
    private bool CheckLoot()
    {
        CharacterRecord r = SaveSystem.Data.Characters[0];
        int owned = r.Stash.Count + r.Equipped.Count;
        bool dropped = _dropsSpawned > 0;
        bool banked = _itemsBanked == _dropsSpawned && owned == _itemsBanked;
        bool worn = r.Equipped.Count > 0;
        GD.Print($"  loot: {_dropsSpawned} dropped, {_itemsBanked} banked, {owned} owned after reload, {r.Equipped.Count} worn; " +
                 $"dropped={dropped} banked={banked} worn={worn}");
        return dropped && banked && worn;
    }

    /// <summary>
    /// After a won campaign stage: it is saved as cleared, what it leads to is
    /// open, gold was banked, a shop sells for the right price, and a forced
    /// secret shop really appeared. Passes trivially for the free fight.
    /// </summary>
    private bool CheckCampaign()
    {
        if (_stage is not StageDef st) return true;
        CampaignState c = SaveSystem.Data.Campaign;
        CharacterRecord r = SaveSystem.Data.Characters[0];
        bool cleared = c.Cleared.Contains(st.Id);
        bool opened = true;
        foreach (StageDef next in Campaign.Neighbours(st))
            if (System.Array.IndexOf(next.Requires, st.Id) >= 0) opened &= Campaign.Unlocked(next, c);
        bool paid = r.Gold > 0;

        int before = r.Gold + 100000;
        r.Gold = before;
        var shelf = ShopMenu.Stock(42, st.EnemyLevel, 3, 1f);
        (ItemData item, int price) = shelf[0];
        bool bought = ShopMenu.TryBuy(r, shelf, 0, out _) && r.Gold == before - price && (r.Stash.Contains(item) || r.Equipped.Contains(item));

        bool secret = Array.IndexOf(OS.GetCmdlineUserArgs(), "secret") < 0 || _secretCurtain is not null;
        GD.Print($"  campaign: {st.Id} cleared={cleared} next open={opened} gold banked={paid} shop buy={bought} secret shop={secret}");
        return cleared && opened && paid && bought && secret;
    }

    private void TickOutcomeProbe()
    {
        WaveDirector d = _director!;
        if (_players[0].IsDead && !_players[0].SpriteVisible) _sawDeadBlink = true;

        // A few stills along the way, when there is a window to take them from.
        if (!DisplayServer.GetName().Contains("headless") && GameClock.Seconds > 4.0 + _shots * 5.0 && _shots < 6)
        {
            GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}wave_{_shots}.png");
            _shots++;
        }

        bool over = d.Current is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat;
        if (!over && GameClock.Seconds < 300) return;
        if (over && GameClock.Seconds - _endedAt < 2.0) return;   // let the ending show

        string got = d.Current == WaveDirector.Phase.Victory ? "win"
                   : d.Current == WaveDirector.Phase.Defeat ? "lose" : "timeout";
        bool ok = got == _outcomeProbe;
        Fighter.HitLanded -= TallyHurt;
        var hurt = new List<KeyValuePair<string, float>>(_hurtBy);
        hurt.Sort((a, b) => b.Value.CompareTo(a.Value));
        GD.Print($"  damage taken by source: {string.Join(", ", hurt.ConvertAll(kv => $"{kv.Key} {kv.Value:F0}"))}");
        if (got == "timeout")
            foreach (Node n in Fighter.Live)
                if (n is Fighter f && f.Hostile)
                    GD.Print($"  still standing: {f.Name} '{f.DisplayName}' hp {f.Health:F0}/{f.MaxHealth:F0} at {f.GlobalPosition.X:F1},{f.GlobalPosition.Y:F1},{f.GlobalPosition.Z:F1}");
        var heard = new List<string>();
        foreach (var kv in ClownSquad.Audio.Sfx.Played) heard.Add($"{kv.Key}x{kv.Value}");
        heard.Sort();
        GD.Print($"  sound banks loaded: {ClownSquad.Audio.Sfx.BankCount}; played: {string.Join(" ", heard)}");
        GD.Print($"  sound names with no file: {(ClownSquad.Audio.Sfx.Missing.Count == 0 ? "none" : string.Join(" ", ClownSquad.Audio.Sfx.Missing))}");
        ok &= ClownSquad.Audio.Sfx.Missing.Count == 0;
        // A boss rush is judged by the result alone: no campaign, no progression to check.
        if (_outcomeProbe == "win" && !_rush) ok &= _director!.IsBonus ? CheckCampaign() : CheckProgression() & CheckLoot() & CheckCampaign();
        if (_outcomeProbe == "lose")
        {
            CharacterRecord r = _players[0].Record!;
            bool lost = BagCount(_players[0]) == 0 && r.Stash.Count == 0 && r.Equipped.Count == 0;
            GD.Print($"  loot on a loss: bag {BagCount(_players[0])}, stash {r.Stash.Count}, worn {r.Equipped.Count} -> lost={lost}");
            ok &= lost;
            GD.Print($"  player blinked while lying dead: {_sawDeadBlink}");
            ok &= _sawDeadBlink;
        }
        GD.Print($"=== WAVE PROBE {(ok ? "PASS" : "FAIL")}: got {got}, expected {_outcomeProbe}; " +
                 $"wave {d.WaveNumber}/{d.WaveCount}, lives {d.Lives}, {GameClock.Seconds:F0}s of game time ===");
        _outcomeProbe = "";
        Engine.TimeScale = GameClock.Speed;
        GetTree().Quit(ok ? 0 : 1);
    }
}
