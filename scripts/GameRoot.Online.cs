namespace ClownSquad;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Map;
using ClownSquad.Net;
using ClownSquad.Progression;
using ClownSquad.World;

/// <summary>
/// Playing a stage online (ADR 0010, phase 2). On the host the stage runs as
/// ever, with a fighter for each guest driven by their <see cref="RemoteInput"/>
/// and a <see cref="NetHost"/> streaming the picture. On a guest nothing is
/// simulated: the stage's scenery, lights and music are set up as usual and a
/// <see cref="NetGuestView"/> shows the host's fight.
/// </summary>
public partial class GameRoot
{
    private NetHost? _netHost;

    /// <summary>Host: a fighter for every guest, playing the character they brought; then start streaming.</summary>
    private void AddOnlinePlayers(NetSession net)
    {
        foreach ((long peer, CharacterRecord record) in net.GuestRecords)
        {
            int index = _players.Count;
            var p = new PlayerFighter
            {
                Name = $"Player{index}",
                Source = net.InputFor(peer),
                PlayerIndex = index,
                DisplayName = record.Name,
                Look = record.Look,
            };
            p.ApplyProgression(record);
            AddChild(p);
            Vector3 home = new(-1.5f + index * 1.5f, 0f, 1.5f);
            p.GlobalPosition = home;
            _homes[p] = home;
            p.Died += OnPlayerDied;
            _players.Add(p);
            GD.Print($"Online: {record.Name} (peer {peer}) joins the stage as player {index + 1}.");
        }
        _netHost = new NetHost(() => _camX, OnlineHud);
        AddChild(_netHost);
        net.BeginStage(_stage!.Id, GameSettings.Field);
    }

    /// <summary>The HUD's numbers for guests: every clown, and the boss if one is up.</summary>
    private Godot.Collections.Array OnlineHud()
    {
        var players = new Godot.Collections.Array();
        foreach (PlayerFighter p in _players)
            players.Add(new Godot.Collections.Array { p.DisplayName, p.Record?.Level ?? 1, p.Health, p.MaxHealth, p.IsDead });
        var boss = new Godot.Collections.Array();
        foreach (Node n in Fighter.Live)
            if (n is PlayerFighter f && f is IBoss && !f.IsDead && f.IsInsideTree())
            {
                boss = new Godot.Collections.Array { f.DisplayName, f.Health, f.MaxHealth };
                break;
            }
        return new Godot.Collections.Array { players, boss };
    }

    /// <summary>Guest: build the stage to watch, and the view that mirrors the host's fight.</summary>
    private void SetUpGuest(NetSession net, string stageId)
    {
        Session.BeginOnlineGuest();
        _stage = Campaign.Get(stageId);
        _scrolling = _stage is StageDef sc && Campaign.IsFight(sc) && sc.Kind != StageKind.Bonus;
        GameClock.Reset();
        NetRegistry.Reset();
        Engine.TimeScale = GameClock.Speed;
        AddChild(new ClownSquad.Audio.Sfx());
        GameSettings.Load();
        GameSettings.Field = net.GuestField;     // the host's field, for this stage only (reloaded on leaving)
        if (GetNodeOrNull<Camera3D>("Camera3D") is Camera3D cam)
        {
            if (_scrolling) MountStageCamera(cam);
            PullBack(cam, GameSettings.FieldScale);
            _arenaCamera = cam;
            _arenaCameraRest = cam.GlobalTransform;
            RebuildArena();
            GetViewport().SizeChanged += RebuildArena;
            cam.AddChild(new ClownSquad.Visuals.CameraShake());
        }
        if (!ClownSquad.Visuals.ArtStyle.Classic) SetUpLighting();
        ClownSquad.Audio.Sfx.PlayMusic(StageMusic());
        ClownSquad.Services.Platform.SetPresence($"Playing stage {stageId} online");
        AddChild(new NetGuestView(GuestCamera));
        GD.Print($"Online: watching and playing the host's stage {stageId}.");
    }

    /// <summary>Guest: the host's camera position along a scrolling stage.</summary>
    private void GuestCamera(float x)
    {
        if (!_scrolling) return;
        _camX = x;
        if (_rig is not null) _rig.Position = new Vector3(x, 0f, 0f);
        if (Arena.Current is Arena arena) arena.CameraX = x;
        _scenery?.Follow(x);
    }

    private string StageMusic() => _practice ? "music_bonus" : _stage switch
    {
        StageDef { Kind: StageKind.Boss } => "music_boss",
        StageDef { Kind: StageKind.Bonus } => "music_bonus",
        StageDef song => $"music_world{song.World}",
        _ => "music_world1",
    };
}
