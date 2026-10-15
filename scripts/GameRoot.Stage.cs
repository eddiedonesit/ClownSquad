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
using ClownSquad.Services;
using ClownSquad.UI;
using ClownSquad.Visuals;
using ClownSquad.World;
using ClownSquad.Net;

/// <summary>
/// The stage itself: wave phases, the scrolling road and its camera, the secret shop.
/// </summary>
public partial class GameRoot
{
    // ----------------------------------------------------------- the fight

    private void OnPhaseChanged(WaveDirector.Phase phase)
    {
        WaveDirector d = _director!;
        GD.Print($"[{GameClock.Seconds:F1}s] phase={phase} wave={d.WaveNumber}/{d.WaveCount} lives={d.Lives}");
        if (phase == WaveDirector.Phase.Intermission) SaveSystem.Save();
        if (phase == WaveDirector.Phase.Intermission && d.WaveNumber == _secretWave && _secretCurtain is null)
            SpawnSecretCurtain();
        // The win probe forces Rare grunts for its first wave only: enough to be
        // sure of drops, without turning every later wave into a wall.
        if (_outcomeProbe == "win" && phase == WaveDirector.Phase.Intermission && d.WaveNumber >= 2) d.ForceRank = null;
        // A boss rush: a breather and half a heal between bosses.
        if (_rush && phase == WaveDirector.Phase.Intermission && d.WaveNumber >= 2)
            foreach (PlayerFighter p in _players) if (!p.IsDead) p.Heal(p.MaxHealth * 0.5f);
        if (_rush && phase == WaveDirector.Phase.Victory)
        {
            _rushTime = GameClock.Seconds;
            _rushRecord = GameSettings.BossRushBest <= 0 || _rushTime < GameSettings.BossRushBest;
            if (_rushRecord && _outcomeProbe.Length == 0) { GameSettings.BossRushBest = _rushTime; GameSettings.Save(); }
        }
        if (phase == WaveDirector.Phase.Victory)
        {
            OnWonForAchievements();
            CrowdCheer(6f, 20);
            BankLoot();
            if (_stage is StageDef cleared)
            {
                Campaign.MarkCleared(SaveSystem.Data.Campaign, cleared);
                SaveSystem.Data.Campaign.At = cleared.Id;
                SaveSystem.Save();
                GD.Print($"Stage {cleared.Id} cleared.");
            }
        }
        if (phase == WaveDirector.Phase.Defeat) LoseLoot();
        if (phase is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat)
        {
            _endedAt = GameClock.Seconds;
            bool windowed = !DisplayServer.GetName().Contains("headless");
            if ((_stage is not null || _rush) && (_outcomeProbe.Length == 0 || windowed))
                GetTree().CreateTimer(1.2).Timeout += () => ShowResults(phase == WaveDirector.Phase.Victory);
            // Win or lose, XP is kept (ADR 0006). Anyone with points to spend
            // is shown the Stat Tent once the ending has had a moment.
            SaveSystem.Save();
            foreach (PlayerFighter p in _players)
                if (p.Record is { StatPoints: > 0 }) _tentDueAt = GameClock.Seconds + 2.0;
        }
    }

    private void GoToMap() => GetTree().ChangeSceneToFile(Campaign.MapScene);

    // ------------------------------------------------------- scrolling stage

    /// <summary>
    /// The stage camera: lower and flatter than the ring's, so the scenery and
    /// sky behind the walkway are in view, Castle Crashers style. It rides a rig
    /// that slides along x; shake still moves the camera within the rig.
    /// </summary>
    private void MountStageCamera(Camera3D cam)
    {
        _rig = new Node3D { Name = "CameraRig" };
        AddChild(_rig);
        RemoveChild(cam);
        _rig.AddChild(cam);
        // Framed so the walkway fills the lower two thirds and the scenery the rest.
        cam.Position = new Vector3(0f, 3.8f, 9.6f);
        cam.RotationDegrees = new Vector3(-17f, 0f, 0f);
        cam.Fov = 35f;
        if (!ClownSquad.Visuals.ArtStyle.Classic)
        {
            // The 3D scenery wants a view past the fence to the horizon: a
            // little lower, further back and tilted up, the clowns the same
            // size on screen and the whole walkway still in frame.
            cam.Position = new Vector3(0f, 3.3f, 10.4f);
            cam.RotationDegrees = new Vector3(-9.5f, 0f, 0f);
            cam.Fov = 33f;
        }
    }

    /// <summary>Spread the stage's groups along the road, put the exit past the last, and fill in crates.</summary>
    private void ConfigureEncounters(StageDef st)
    {
        var at = new float[st.Waves.Length];
        for (int i = 0; i < at.Length; i++) at[i] = FirstEncounter + EncounterSpacing * i;
        _director!.EncounterX = at;
        _director.ExitX = at[^1] + 12f;
        _director.LeadX = () =>
        {
            float lead = float.MinValue;
            foreach (PlayerFighter p in _players) if (!p.IsDead) lead = Mathf.Max(lead, p.GlobalPosition.X);
            return lead == float.MinValue ? _camX : lead;
        };
        CallDeferred(MethodName.PlaceStageObjects);
    }

    private void PlaceStageObjects()
    {
        if (_scenery is null || _director?.EncounterX is not float[] at) return;
        _scenery.PlaceCrates(this, at, _director.ExitX);
        _scenery.PlaceExit(_director.ExitX);
    }

    /// <summary>
    /// The camera follows the furthest player right and never backs up, the
    /// rule of the genre; while a group is being fought it holds at that group.
    /// </summary>
    private void FollowCamera(float dt)
    {
        if (_director is null || _rig is null) return;
        float lead = _director.LeadX();
        float target = Mathf.Max(_camX, lead - 1.0f);
        if (_director.CameraLock is float hold) target = hold;
        target = Mathf.Min(target, _director.ExitX + 1f);
        _camX = Mathf.MoveToward(_camX, target, 9f * dt);
        _rig.Position = new Vector3(_camX, 0f, 0f);
        if (Arena.Current is Arena arena) arena.CameraX = _camX;
        _scenery?.Follow(_camX);
    }

    // ------------------------------------------------------------ secret shop

    /// <summary>Chance a stage has the secret shop somewhere in it.</summary>
    private const float SecretShopChance = 0.07f;

    /// <summary>A little velvet booth appears in the ring between waves. Walk into it to shop.</summary>
    private void SpawnSecretCurtain()
    {
        const float px = 0.0105f;
        var tex = GD.Load<Texture2D>("res://assets/arena/secret_curtain.png");
        var node = new Node3D();
        AddChild(node);
        float x = _scrolling ? _camX + (float)GD.RandRange(1.5, 4.5) : (float)GD.RandRange(-3.5, 3.5);
        node.GlobalPosition = new Vector3(x, 0f, -3.2f);
        var sprite = new Sprite3D
        {
            Texture = tex, PixelSize = px, Position = new Vector3(0f, tex.GetHeight() * px * 0.5f, 0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
        };
        if (LitSprite.For("res://assets/arena/secret_curtain.png") is ShaderMaterial lit)
        {
            LitSprite.Apply(lit, Colors.White, 0f, false);
            sprite.MaterialOverride = lit;
        }
        node.AddChild(sprite);
        node.AddChild(new OmniLight3D { LightColor = new Color(0.8f, 0.4f, 1f), LightEnergy = 3f, OmniRange = 3f, Position = new Vector3(0f, 0.8f, 0.6f) });
        _secretCurtain = node;
        Fx.Pop(this, node.GlobalPosition + new Vector3(0f, 1.8f, 0f), "SECRET SHOP!?", new Color(0.85f, 0.55f, 1f), 1.2f, 1.2f);
        Fx.Sound("ding", 0.6f);
        Fx.Sound("kazoo_rise", 1.3f, -6f);
        GD.Print("The secret shop has appeared.");
        if (_outcomeProbe.Length > 0 && !DisplayServer.GetName().Contains("headless"))
            GetTree().CreateTimer(0.8).Timeout += () =>
                GetViewport().GetTexture().GetImage().SavePng(ClownSquad.Debug.DebugShots.Dir + "secret_shop.png");
    }

    private void TickSecretCurtain()
    {
        if (_secretCurtain is null || !IsInstanceValid(_secretCurtain) || _shop is not null) return;
        foreach (PlayerFighter p in _players)
        {
            if (p.IsDead) continue;
            Vector3 d = p.GlobalPosition - _secretCurtain.GlobalPosition;
            if (new Vector2(d.X, d.Z).Length() > 0.9f) continue;
            OpenSecretShop();
            return;
        }
    }

    private void OpenSecretShop()
    {
        Achievements.Award("secret_shop");
        int level = Mathf.Max(_director?.LevelFloor ?? 1, _players[0].Record?.Level ?? 1) + 3;
        // Better than any world shop: higher level, rarity pushed hard.
        var shelf = ShopMenu.Stock(_director!.Seed ^ 0xb0057UL, level, 5, 4f);
        var shoppers = new List<Seat>();
        foreach (PlayerFighter p in _players)
            if (p.Record is CharacterRecord r && p.Source is LocalDeviceInput d)
                shoppers.Add(new Seat(r, d.Device, () => p.ApplyProgression(r)));
        _shop = new ShopMenu("THE SECRET SHOP", shelf, shoppers);
        _shop.Closed += () =>
        {
            if (_secretCurtain is not null && IsInstanceValid(_secretCurtain)) _secretCurtain.QueueFree();
            Fx.Sound("pop");
        };
        AddChild(_shop);
    }

    private void CrowdCheer(float seconds, int flashes)
    {
        if (_circus is null) return;
        _circus.Cheer(seconds, flashes);
        if (seconds >= 1.3f) Fx.Sound("crowd_cheer", 1f, seconds >= 3f ? 0f : -5f);
    }
}
