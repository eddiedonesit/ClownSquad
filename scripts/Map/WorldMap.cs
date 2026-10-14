namespace ClownSquad.Map;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.Visuals;

/// <summary>
/// The world map, Super Mario World style. The squad's clown stands on a
/// node; a direction walks it along an open path to the next node; ATTACK or
/// JUMP enters what it stands on. Stages unlock as the ones before them are
/// cleared. Each world has a shop off the road and a bonus stage on a side
/// branch, and its gate leads on to the next world. Q/E (bumpers) look at
/// other worlds already reached. Pads join the squad here with A.
///
/// The island art is tools/gen_map.py; this draws the paths, nodes, the
/// walking clown and the labels over it, all in map pixels scaled by two.
/// </summary>
public partial class WorldMap : Node2D
{
    private const float Scale = 2f;
    private const float WalkSpeed = 190f;            // screen pixels per second
    private const string Icons = "res://assets/map/map_icons.png";
    private static readonly string[] Names = { "PIP", "POPPY", "BINGO", "DOT" };

    private enum Icon { Open = 0, Cleared = 1, Locked = 2, Boss = 3, Bonus = 4, Shop = 5, Gate = 6 }

    private CampaignState _state = null!;
    private int _world = 1;
    private StageDef _at = null!;
    private StageDef? _walkingTo;
    private Vector2 _pos;                              // the clown, in screen pixels
    private TextureRect _background = null!;
    private Texture2D _icons = null!;
    private AnimatedSprite2D _clown = null!;
    private Label _worldTitle = null!, _nodeTitle = null!, _nodeInfo = null!, _squad = null!;
    private Font _font = null!;
    private readonly List<MapPad> _pads = new();
    private ShopMenu? _shop;
    private double _clock;
    private string _justUnlocked = "";

    /// <summary>Probe hook: true once the map has finished setting up.</summary>
    public static bool Ready { get; private set; }

    public override void _Ready()
    {
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
        if (Sfx.Instance is null) AddChild(new Sfx());
        SaveSystem.Path = SaveSystem.DefaultPath;
        SaveSystem.Load();
        _state = SaveSystem.Data.Campaign;
        _at = Campaign.Get(_state.At) ?? Campaign.Get("1-1")!;
        _world = _at.World;
        _font = UiFont.Display;
        _icons = GD.Load<Texture2D>(Icons);

        _background = new TextureRect
        {
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Size = new Vector2(576f * Scale, 324f * Scale),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ZIndex = -10,
        };
        AddChild(_background);

        string sheet = ArtStyle.Sheet("res://assets/sprites/clown_16bit.png", "res://assets/sprites/pip_hd.png");
        SpriteFrames frames = FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet));
        FrameLibrary.SheetInfo info = FrameLibrary.Info(sheet);
        _clown = new AnimatedSprite2D
        {
            SpriteFrames = frames,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            Scale = Vector2.One * (ArtStyle.Classic ? 0.75f : 0.38f),
            // Feet on the node: the sheet's baseline row sits on the sprite's origin.
            Offset = new Vector2(0f, -(info.Baseline - info.FrameHeight * 0.5f)),
            ZIndex = 5,
        };
        AddChild(_clown);
        _clown.Play("idle");

        BuildLabels();
        foreach (int d in Campaign.SquadDevices) _pads.Add(new MapPad(d));

        // Whatever the last clear unlocked gets a moment of fanfare.
        if (_state.Cleared.Contains(_at.Id))
            foreach (StageDef s in Campaign.Neighbours(_at))
                if (s.World == _world && !_state.Cleared.Contains(s.Id) && Campaign.Unlocked(s, _state)) _justUnlocked = s.Id;
        if (_justUnlocked.Length > 0) Sfx.Play("fanfare", 1.1f, -6f);

        ShowWorld(_world);
        _pos = Screen(_at);
        _clown.Position = _pos;
        Sfx.PlayMusic("music_march");
        RefreshInfo();
        Ready = true;
        GD.Print($"World map: world {_world}, standing on {_at.Id}; {_state.Cleared.Count} node(s) cleared.");

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "mapshot") >= 0)
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/world_map.png");
                GetTree().Quit();
            };
    }

    private static Vector2 Screen(StageDef s) => s.MapPos * Scale;

    private void ShowWorld(int world)
    {
        _world = Mathf.Clamp(world, 1, Campaign.Worlds);
        _background.Texture = GD.Load<Texture2D>($"res://assets/map/world_{_world}.png");
        _worldTitle.Text = $"WORLD {_world}  -  {Campaign.WorldNames[_world - 1]}";
        QueueRedraw();
    }

    private bool WorldReached(int world) => world == 1 || _state.Cleared.Contains($"{world - 1}-G");

    // ---------------------------------------------------------------- input

    public override void _Process(double delta)
    {
        _clock += delta;
        QueueRedraw();
        if (_shop is not null) return;
        PollJoins();

        if (_walkingTo is StageDef target)
        {
            Vector2 goal = Screen(target);
            Vector2 to = goal - _pos;
            float step = WalkSpeed * (float)delta;
            if (to.Length() <= step)
            {
                _pos = goal;
                _at = target;
                _walkingTo = null;
                _state.At = _at.Id;
                _clown.Play("idle");
                Sfx.Play("boing_small", 1.3f, -6f);
            }
            else
            {
                _pos += to.Normalized() * step;
                if (Mathf.Abs(to.X) > 1f) _clown.FlipH = to.X < 0f;
            }
            _clown.Position = _pos;
            RefreshInfo();
            return;
        }
        _clown.Position = _pos + new Vector2(0f, Mathf.Sin((float)_clock * 4f) * 1.5f);

        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p == MapPad.Press.None) continue;
            Handle(p);
            break;
        }
    }

    private void Handle(MapPad.Press p)
    {
        switch (p)
        {
            case MapPad.Press.Up: TryWalk(new Vector2(0f, -1f)); break;
            case MapPad.Press.Down: TryWalk(new Vector2(0f, 1f)); break;
            case MapPad.Press.Right: TryWalk(new Vector2(1f, 0f)); break;
            case MapPad.Press.Left: TryWalk(new Vector2(-1f, 0f)); break;
            case MapPad.Press.Enter: Enter(); break;
            case MapPad.Press.World:
                // Look at the next world reached, wrapping, and stand at its start.
                for (int i = 1; i < Campaign.Worlds; i++)
                {
                    int w = (_world + i - 1) % Campaign.Worlds + 1;
                    if (!WorldReached(w)) continue;
                    ShowWorld(w);
                    _at = Campaign.Get($"{w}-1")!;
                    _pos = Screen(_at);
                    _state.At = _at.Id;
                    Sfx.Play("whoosh", 1.2f, -4f);
                    RefreshInfo();
                    break;
                }
                break;
        }
    }

    /// <summary>Walk to the open neighbour that lies most nearly in a direction.</summary>
    private void TryWalk(Vector2 dir)
    {
        StageDef? best = null;
        float bestDot = 0.35f;
        foreach (StageDef n in Campaign.Neighbours(_at))
        {
            if (n.World != _world || !Campaign.Unlocked(n, _state)) continue;
            float dot = (n.MapPos - _at.MapPos).Normalized().Dot(dir);
            if (dot > bestDot) { bestDot = dot; best = n; }
        }
        if (best is null) { Sfx.Play("squeak", 0.8f, -10f); return; }
        _walkingTo = best;
        _clown.Play("walk");
        Sfx.Play("zip", 1.3f, -8f);
    }

    private void Enter()
    {
        switch (_at.Kind)
        {
            case StageKind.Shop:
                OpenShop();
                return;
            case StageKind.Gate:
                if (_world >= Campaign.Worlds) { Sfx.Play("victory"); return; }
                Campaign.MarkCleared(_state, _at);
                ShowWorld(_world + 1);
                _at = Campaign.Get($"{_world}-1")!;
                _pos = Screen(_at);
                _state.At = _at.Id;
                SaveSystem.Save();
                Sfx.Play("fanfare");
                RefreshInfo();
                return;
        }
        // A fight: hand the stage to the fight scene and go.
        Campaign.Pending = _at;
        SaveSystem.Save();
        Sfx.Play("kazoo_rise", 1.2f, -4f);
        GetTree().ChangeSceneToFile(Campaign.FightScene);
    }

    private void OpenShop()
    {
        // Restocked every time a stage in this world is cleared; stocked to the
        // squad's level or the world's, whichever is higher.
        int restocks = _state.ShopRestocks.TryGetValue(_world.ToString(), out int n) ? n : 0;
        int level = Mathf.Max(_at.EnemyLevel, SaveSystem.CharacterFor(0, Names[0]).Level);
        var shelf = ShopMenu.Stock((ulong)(_world * 1000 + restocks), level, 6, 0.3f + 0.12f * _world);
        var shoppers = new List<Shopper>();
        for (int i = 0; i < Campaign.SquadDevices.Count; i++)
            shoppers.Add(new Shopper(SaveSystem.CharacterFor(i, Names[i % 4]), Campaign.SquadDevices[i], null));
        _shop = new ShopMenu($"{Campaign.WorldNames[_world - 1]} SHOP", shelf, shoppers, pauses: false);
        _shop.Closed += () =>
        {
            _shop = null;
            foreach (MapPad p in _pads) p.Swallow();
            RefreshInfo();
        };
        AddChild(_shop);
    }

    private void PollJoins()
    {
        foreach (int pad in Godot.Input.GetConnectedJoypads())
        {
            if (Campaign.SquadDevices.Contains(pad) || !Godot.Input.IsJoyButtonPressed(pad, JoyButton.A)) continue;
            Campaign.SquadDevices.Add(pad);
            var mp = new MapPad(pad);
            _pads.Add(mp);
            Sfx.Play("ding");
            RefreshInfo();
            GD.Print($"Pad {pad} joined the squad as player {Campaign.SquadDevices.Count}.");
        }
    }

    // --------------------------------------------------------------- drawing

    public override void _Draw()
    {
        List<StageDef> shown = Campaign.InWorld(_world);
        // Paths: bright dots where open, faint where still locked.
        foreach (StageDef s in shown)
            foreach (string req in s.Requires)
            {
                StageDef? from = Campaign.Get(req);
                if (from is null || from.World != _world) continue;
                bool open = Campaign.Unlocked(s, _state);
                Vector2 a = Screen(from), b = Screen(s);
                float len = a.DistanceTo(b);
                for (float t = 16f; t < len - 16f; t += 12f)
                    DrawCircle(a.Lerp(b, t / len), open ? 3.2f : 2.2f,
                               open ? new Color(1f, 0.95f, 0.8f) : new Color(0.25f, 0.2f, 0.25f, 0.6f));
            }

        foreach (StageDef s in shown)
        {
            bool open = Campaign.Unlocked(s, _state);
            bool done = _state.Cleared.Contains(s.Id);
            Icon icon = s.Kind switch
            {
                StageKind.Boss => Icon.Boss,
                StageKind.Bonus => Icon.Bonus,
                StageKind.Shop => Icon.Shop,
                StageKind.Gate => Icon.Gate,
                _ => !open ? Icon.Locked : done ? Icon.Cleared : Icon.Open,
            };
            float pop = 1f;
            if (s.Id == _justUnlocked) pop += 0.35f * Mathf.Max(0f, 1f - (float)_clock / 2f) * Mathf.Abs(Mathf.Sin((float)_clock * 10f));
            float size = 24f * 1.6f * pop;
            Vector2 at = Screen(s) - new Vector2(size / 2f, size * 0.62f);
            DrawTextureRectRegion(_icons, new Rect2(at, new Vector2(size, size)), new Rect2((int)icon * 24, 0, 24, 24),
                                  open ? Colors.White : new Color(0.45f, 0.42f, 0.5f));
            if (s.Kind == StageKind.Normal)
                DrawString(_font, Screen(s) + new Vector2(-20f, 30f), s.Id, HorizontalAlignment.Center, 40f, 14,
                           open ? Colors.White : new Color(0.6f, 0.6f, 0.65f));
        }
    }

    // ---------------------------------------------------------------- labels

    private LabelSettings Style(int size, Color color, int outline = 6) => new()
    {
        Font = _font, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        ShadowSize = 2, ShadowColor = new Color(0f, 0f, 0f, 0.45f), ShadowOffset = new Vector2(2f, 3f),
    };

    private void BuildLabels()
    {
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        var top = new Panel { Position = new Vector2(316, 8), Size = new Vector2(520, 44) };
        top.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f, 0.95f), new Color(0.98f, 0.8f, 0.2f)));
        layer.AddChild(top);
        _worldTitle = new Label { Size = new Vector2(520, 44), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, LabelSettings = Style(20, new Color(1f, 0.97f, 0.9f), 5) };
        top.AddChild(_worldTitle);

        var bottom = new Panel { Position = new Vector2(16, 556), Size = new Vector2(1120, 80) };
        bottom.AddThemeStyleboxOverride("panel", Plate(new Color(0.18f, 0.12f, 0.28f, 0.94f), new Color(0.98f, 0.8f, 0.2f)));
        layer.AddChild(bottom);
        _nodeTitle = new Label { Position = new Vector2(18, 8), LabelSettings = Style(24, new Color(1f, 0.85f, 0.3f), 6) };
        _nodeInfo = new Label { Position = new Vector2(18, 46), LabelSettings = Style(14, new Color(0.9f, 0.88f, 1f), 4) };
        _squad = new Label { Position = new Vector2(560, 10), Size = new Vector2(544, 28), HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = Style(16, new Color(0.6f, 1f, 0.7f), 4) };
        var hint = new Label
        {
            Position = new Vector2(500, 46), Size = new Vector2(604, 24), HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(13, new Color(0.85f, 0.8f, 0.95f), 4),
            Text = "WASD/stick walk    J/Space or X/A enter    Q/E worlds    pads press A to join",
        };
        bottom.AddChild(_nodeTitle);
        bottom.AddChild(_nodeInfo);
        bottom.AddChild(_squad);
        bottom.AddChild(hint);
    }

    private void RefreshInfo()
    {
        StageDef s = _walkingTo ?? _at;
        bool done = _state.Cleared.Contains(s.Id);
        _nodeTitle.Text = s.Kind switch
        {
            StageKind.Normal => $"STAGE {s.Id}" + (done ? "   - CLEARED -" : ""),
            StageKind.Boss => $"BOSS: {Campaign.WorldNames[s.World - 1]}" + (done ? "   - CLEARED -" : ""),
            _ => s.Title,
        };
        _nodeInfo.Text = s.Kind switch
        {
            StageKind.Normal or StageKind.Boss => $"{s.Waves.Length} waves  -  enemy level {s.EnemyLevel}+",
            StageKind.Bonus => $"{s.BonusSeconds:F0} seconds of pinatas full of gold  -  nothing hits back",
            StageKind.Shop => "this world's gear  -  restocks after every clear here",
            _ => s.World < Campaign.Worlds ? "the road to the next world" : "the end of the road... for now",
        };
        var parts = new List<string>();
        for (int i = 0; i < Campaign.SquadDevices.Count; i++)
        {
            CharacterRecord r = SaveSystem.CharacterFor(i, Names[i % 4]);
            parts.Add($"{r.Name} LV{r.Level}  {r.Gold}G");
        }
        _squad.Text = string.Join("     ", parts);
    }

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 12, int width = 4) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ShadowColor = new Color(0f, 0f, 0f, 0.35f), ShadowSize = 4, ShadowOffset = new Vector2(2f, 3f),
    };
}

/// <summary>Map controls from one device, edge-detected: four directions, enter, and change world.</summary>
public sealed class MapPad
{
    public enum Press { None, Up, Down, Left, Right, Enter, World }

    private readonly int _device;
    private readonly bool[] _held = new bool[7];

    public MapPad(int device)
    {
        _device = device;
        Read(_held);
    }

    /// <summary>Treat everything currently held as already handled (after a menu closes).</summary>
    public void Swallow() => Read(_held);

    public Press Poll()
    {
        var now = new bool[7];
        Read(now);
        Press result = Press.None;
        for (int i = 1; i < now.Length; i++)
            if (now[i] && !_held[i] && result == Press.None) result = (Press)i;
        System.Array.Copy(now, _held, now.Length);
        return result;
    }

    private void Read(bool[] into)
    {
        if (_device == LocalDeviceInput.Keyboard)
        {
            into[(int)Press.Up] = K(Key.W) || K(Key.Up);
            into[(int)Press.Down] = K(Key.S) || K(Key.Down);
            into[(int)Press.Left] = K(Key.A) || K(Key.Left);
            into[(int)Press.Right] = K(Key.D) || K(Key.Right);
            into[(int)Press.Enter] = K(Key.J) || K(Key.Space) || K(Key.Enter);
            into[(int)Press.World] = K(Key.Q) || K(Key.E);
            return;
        }
        float x = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftX), y = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftY);
        into[(int)Press.Up] = y < -0.6f || B(JoyButton.DpadUp);
        into[(int)Press.Down] = y > 0.6f || B(JoyButton.DpadDown);
        into[(int)Press.Left] = x < -0.6f || B(JoyButton.DpadLeft);
        into[(int)Press.Right] = x > 0.6f || B(JoyButton.DpadRight);
        into[(int)Press.Enter] = B(JoyButton.A) || B(JoyButton.X);
        into[(int)Press.World] = B(JoyButton.LeftShoulder) || B(JoyButton.RightShoulder);
    }

    private static bool K(Key k) => Godot.Input.IsKeyPressed(k);
    private bool B(JoyButton b) => Godot.Input.IsJoyButtonPressed(_device, b);
}
