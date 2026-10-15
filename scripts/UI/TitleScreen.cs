namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// The first thing on screen: the circus ring, crowd and spotlights live behind
/// the CLOWN SQUAD logo, with the four clowns standing in the sawdust.
///
///   Main menu    CONTINUE (the most recent save) / NEW GAME / LOAD GAME / QUIT
///   Slots        three save slots, each summarised; a new game over a used slot asks first
///   The squad    character select: player one on the keyboard, pads join with A;
///                each picks a clown (no two the same) and readies up
///
/// Test and debug flags on the command line skip straight to where they need to be.
/// </summary>
public partial class TitleScreen : Node3D
{
    private enum Mode { Main, Slots, Squad }
    private Mode _mode = Mode.Main;
    private bool _newGame;
    private int _slotCursor;
    private int _confirmSlot = -1;
    private int _mainCursor;
    private readonly List<(string label, Action act)> _main = new();

    private CircusArena _circus = null!;
    private readonly List<DirectionalSprite> _standees = new();
    private Control _ui = null!;
    private Label _logo = null!;
    private VBoxContainer _menu = null!;
    private Control _menuBacking = null!;
    private Label _hint = null!;
    private readonly List<MapPad> _pads = new();
    private double _clock;

    private sealed class Picker
    {
        public int Device;
        public int Choice;
        public bool Ready;
        public MapPad Pad = null!;
        public PanelContainer Card = null!;
        public Label Title = null!, Status = null!;
        public AnimatedSprite2D Sprite = null!;
    }
    private readonly List<Picker> _pickers = new();
    private HBoxContainer? _squadRow;

    public override void _Ready()
    {
        // Tests and debug runs go straight where they are going.
        string[] args = OS.IsDebugBuild() ? OS.GetCmdlineUserArgs() : Array.Empty<string>();
        foreach (string f in new[] { "wavetest", "losetest", "movetest", "animtest", "aitest", "sandbox", "fight", "stage", "tentshot", "stageshot", "castshot", "weapontest", "skillshot", "demo", "bossrush" })
            if (Array.IndexOf(args, f) >= 0) { CallDeferred(MethodName.Go, Campaign.FightScene); return; }
        if (Array.IndexOf(args, "mapshot") >= 0) { CallDeferred(MethodName.Go, Campaign.MapScene); return; }

        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
        ArtStyle.Classic = Array.IndexOf(args, "classic") >= 0;
        if (Sfx.Instance is null) AddChild(new Sfx());
        BuildStage();
        BuildUi();
        _pads.Add(new MapPad(LocalDeviceInput.Keyboard));
        foreach (int pad in Godot.Input.GetConnectedJoypads()) _pads.Add(new MapPad(pad));
        ShowMain();

        // The comic-book opening plays over the ring; the menu waits for it.
        _ui.Visible = false;
        _intro = new ComicIntro(() => _logo.GetGlobalRect());
        _intro.LogoLanded += () => Sfx.PlayMusic("music_title");
        _intro.Finished += () => { _ui.Visible = true; _intro = null; };
        AddChild(_intro);

        if (Array.IndexOf(args, "titleshot") >= 0) TitleShots();
        if (Array.IndexOf(args, "titlemovie") >= 0) TitleMovie();
    }

    /// <summary>
    /// "-- titlemovie" (with Godot's --write-movie): the intro, then the menu
    /// with the cursor walked down and back up, then quit. For recording.
    /// </summary>
    private void TitleMovie()
    {
        double[] at = { 7.0, 7.7, 8.4, 9.4, 10.1 };
        MapPad.Press[] press = { MapPad.Press.Down, MapPad.Press.Down, MapPad.Press.Down, MapPad.Press.Up, MapPad.Press.Up };
        for (int i = 0; i < at.Length; i++)
        {
            MapPad.Press p = press[i];
            GetTree().CreateTimer(at[i]).Timeout += () => { if (_intro is null) TickMain(p); };
        }
        GetTree().CreateTimer(12.0).Timeout += () => GetTree().Quit();
    }

    private ComicIntro? _intro;
    private SettingsMenu? _settings;

    private BestiaryMenu? _bestiary;

    private void OpenBestiary()
    {
        var devices = new List<int> { LocalDeviceInput.Keyboard };
        devices.AddRange(Godot.Input.GetConnectedJoypads());
        _bestiary = new BestiaryMenu(devices);
        _bestiary.Closed += () => { _bestiary = null; foreach (MapPad p in _pads) p.Swallow(); };
        AddChild(_bestiary);
    }

    private void OpenSettings()
    {
        var devices = new List<int> { LocalDeviceInput.Keyboard };
        devices.AddRange(Godot.Input.GetConnectedJoypads());
        _settings = new SettingsMenu(devices);
        _settings.Closed += () => { _settings = null; foreach (MapPad p in _pads) p.Swallow(); };
        AddChild(_settings);
    }

    /// <summary>Screenshots of the intro and every title page, for checking the art (needs a window).</summary>
    private void TitleShots()
    {
        if (DisplayServer.GetName() == "headless") { GetTree().CreateTimer(6.0).Timeout += () => GetTree().Quit(); return; }
        void Shot(string name) => GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}{name}.png");
        GetTree().CreateTimer(1.75).Timeout += () => Shot("intro_panels");
        GetTree().CreateTimer(2.75).Timeout += () => Shot("intro_logo");
        GetTree().CreateTimer(5.6).Timeout += () => OpenSettings();
        GetTree().CreateTimer(6.0).Timeout += () => { Shot("settings"); _settings?.QueueFree(); _settings = null; OpenBestiary(); };
        GetTree().CreateTimer(6.15).Timeout += () => { Shot("bestiary"); _bestiary?.QueueFree(); _bestiary = null; };
        GetTree().CreateTimer(6.2).Timeout += () =>
        {
            Shot("title");
            _newGame = false;
            ShowSlots();
            GetTree().CreateTimer(0.6).Timeout += () =>
            {
                Shot("load_slots");
                EnterSquad();
                GetTree().CreateTimer(1.0).Timeout += () =>
                {
                    Shot("squad_select");
                    GetTree().Quit();
                };
            };
        };
    }

    private void Go(string scene) => GetTree().ChangeSceneToFile(scene);

    // ------------------------------------------------------------- the ring

    private void BuildStage()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.09f, 0.06f, 0.14f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.5f, 0.7f), AmbientLightEnergy = 0.4f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, GlowEnabled = true, GlowIntensity = 0.5f,
            VolumetricFogEnabled = true, VolumetricFogDensity = 0.014f, VolumetricFogEmission = Colors.Black,
            AdjustmentEnabled = true, AdjustmentSaturation = 1.2f, AdjustmentContrast = 1.08f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), LightEnergy = 0.7f, LightVolumetricFogEnergy = 0f });
        AddChild(new Camera3D { Position = new Vector3(0f, 3.6f, 9.5f), RotationDegrees = new Vector3(-14f, 0f, 0f), Fov = 40f, Current = true });
        _circus = new CircusArena();
        AddChild(_circus);
        foreach ((Vector3 at, Color c) in new[] { (new Vector3(-6.5f, 3.2f, 1.5f), new Color(1f, 0.55f, 0.45f)), (new Vector3(6.5f, 3.2f, 1.5f), new Color(0.45f, 0.7f, 1f)) })
            AddChild(new OmniLight3D { Position = at, LightColor = c, LightEnergy = 3f, OmniRange = 11f, LightVolumetricFogEnergy = 0f });

        // The squad stands in the ring, two facing each way.
        // Out to the sides, leaving the middle of the ring for the menu.
        float[] xs = { -5.2f, -3.4f, 3.4f, 5.2f };
        for (int i = 0; i < Roster.All.Length; i++)
        {
            string sheet = Roster.Sheet(Roster.All[i].Id);
            var s = new DirectionalSprite(sheet, ArtStyle.FramesFor(sheet), ArtStyle.Pixel(0.0225f));
            var holder = new Node3D { Position = new Vector3(xs[i], 0f, 1.2f) };
            AddChild(holder);
            holder.AddChild(s);
            s.SetFacing(new Vector3(i < 2 ? 1f : -1f, 0f, 0f));
            _standees.Add(s);
        }
    }

    // ------------------------------------------------------------------ UI

    private static LabelSettings Style(int size, Color color, int outline = 6) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        ShadowSize = 3, ShadowColor = new Color(0f, 0f, 0f, 0.5f), ShadowOffset = new Vector2(3f, 4f),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 14, int width = 4) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 14,
    };

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _ui = new Control();
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_ui);

        _logo = new Label { Text = "CLOWN SQUAD", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(112, new Color(1f, 0.85f, 0.3f), 18) };
        _logo.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _logo.Position = new Vector2(-460, 24);
        _logo.Size = new Vector2(920, 140);
        _logo.PivotOffset = new Vector2(460, 70);
        _ui.AddChild(_logo);

        // Centred in the ring below the logo, sized to whatever the menu holds.
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.OffsetTop = 190;
        centre.OffsetBottom = -60;
        _ui.AddChild(centre);
        var backing = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        backing.AddThemeStyleboxOverride("panel", Plate(new Color(0.12f, 0.06f, 0.18f, 0.72f), new Color(1f, 0.85f, 0.3f, 0.8f), 18, 3));
        centre.AddChild(backing);
        _menuBacking = backing;
        _menu = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _menu.AddThemeConstantOverride("separation", 8);
        backing.AddChild(_menu);

        _hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(15, new Color(0.85f, 0.8f, 0.95f), 4) };
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hint.Position = new Vector2(-560, -44);
        _hint.Size = new Vector2(1120, 30);
        _ui.AddChild(_hint);
    }

    private void ClearMenu()
    {
        foreach (Node n in _menu.GetChildren()) n.QueueFree();
        if (_squadRow is not null) { _squadRow.QueueFree(); _squadRow = null; }
    }

    private Label MenuLine(string text, bool selected, int size = 30)
    {
        var l = new Label
        {
            Text = selected ? $"> {text} <" : text, HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(size, selected ? new Color(1f, 0.85f, 0.3f) : Colors.White, 7),
        };
        _menu.AddChild(l);
        return l;
    }

    // ------------------------------------------------------------ main menu

    private static int MostRecentSlot()
    {
        int best = -1;
        DateTime newest = DateTime.MinValue;
        for (int s = 1; s <= SaveSystem.Slots; s++)
            if (SaveSystem.Peek(s) is not null && SaveSystem.SavedAt(s) is DateTime t && t > newest) { newest = t; best = s; }
        return best;
    }

    private void ShowMain()
    {
        _mode = Mode.Main;
        _main.Clear();
        int recent = MostRecentSlot();
        if (recent > 0) _main.Add(("CONTINUE", () => { SaveSystem.ActiveSlot = recent; _newGame = false; EnterSquad(); }));
        _main.Add(("NEW GAME", () => { _newGame = true; ShowSlots(); }));
        if (recent > 0) _main.Add(("LOAD GAME", () => { _newGame = false; ShowSlots(); }));
        _main.Add(("BOSS RUSH", () => { _starting = true; Sfx.Play("drumroll"); BossRush.Start(GetTree()); }));
        _main.Add(("BESTIARY", OpenBestiary));
        _main.Add(("SETTINGS", OpenSettings));
        _main.Add(("QUIT", () => GetTree().Quit()));
        _mainCursor = Mathf.Clamp(_mainCursor, 0, _main.Count - 1);
        RefreshMain();
        _hint.Text = "UP/DOWN  CHOOSE      ATTACK/JUMP  SELECT";
    }

    private void RefreshMain()
    {
        ClearMenu();
        for (int i = 0; i < _main.Count; i++) MenuLine(_main[i].label, i == _mainCursor);
    }

    // ---------------------------------------------------------------- slots

    private void ShowSlots()
    {
        _mode = Mode.Slots;
        _confirmSlot = -1;
        _slotCursor = Mathf.Clamp(SaveSystem.ActiveSlot - 1, 0, SaveSystem.Slots - 1);
        RefreshSlots();
        _hint.Text = _newGame ? "PICK A SLOT FOR THE NEW GAME      ESC/B  BACK" : "PICK A SAVE TO LOAD      ESC/B  BACK";
    }

    private static string Summary(int slot)
    {
        SaveData? d = SaveSystem.Peek(slot);
        if (d is null) return "- EMPTY -";
        var names = new List<string>();
        foreach (CharacterRecord c in d.Characters) names.Add($"{c.Name} LV{c.Level}");
        int world = 1;
        for (int w = 2; w <= Campaign.Worlds; w++) if (d.Campaign.Cleared.Contains($"{w - 1}-G")) world = w;
        string when = SaveSystem.SavedAt(slot) is DateTime t ? t.ToString("MMM d  HH:mm") : "";
        return $"{string.Join("  ", names)}   WORLD {world}   {d.Campaign.Cleared.Count} CLEARED   {when}";
    }

    private void RefreshSlots()
    {
        ClearMenu();
        MenuLine(_newGame ? "NEW GAME" : "LOAD GAME", false, 26).LabelSettings.FontColor = new Color(0.6f, 0.9f, 1f);
        for (int s = 1; s <= SaveSystem.Slots; s++)
        {
            bool sel = s - 1 == _slotCursor;
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", Plate(sel ? new Color(0.86f, 0.2f, 0.25f, 0.92f) : new Color(0.2f, 0.12f, 0.3f, 0.9f),
                                                          sel ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.5f, 0.7f), 12, sel ? 4 : 2));
            var box = new VBoxContainer();
            panel.AddChild(box);
            string label = $"SLOT {s}";
            if (_confirmSlot == s) label += "   - PRESS AGAIN TO START OVER -";
            box.AddChild(new Label { Text = label, LabelSettings = Style(22, sel ? new Color(1f, 0.85f, 0.3f) : Colors.White, 5) });
            box.AddChild(new Label { Text = Summary(s), LabelSettings = Style(13, new Color(0.9f, 0.88f, 1f), 3) });
            _menu.AddChild(panel);
        }
    }

    private void PickSlot()
    {
        int slot = _slotCursor + 1;
        bool used = SaveSystem.Peek(slot) is not null;
        if (!_newGame && !used) { Sfx.Play("squeak", 0.8f); return; }
        if (_newGame && used && _confirmSlot != slot)
        {
            // Starting over on a used slot asks once more.
            _confirmSlot = slot;
            Sfx.Play("honk_low", 1.2f, -6f);
            RefreshSlots();
            return;
        }
        SaveSystem.ActiveSlot = slot;
        if (_newGame) SaveSystem.StartFresh();
        EnterSquad();
    }

    // ---------------------------------------------------------------- squad

    private void EnterSquad()
    {
        _mode = Mode.Squad;
        Session.BeginCampaign();
        SaveSystem.Load();
        ClearMenu();
        _pickers.Clear();
        _squadRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _squadRow.AddThemeConstantOverride("separation", 16);
        _squadRow.SetAnchorsPreset(Control.LayoutPreset.Center);
        _squadRow.Position = new Vector2(-560, -110);
        _squadRow.Size = new Vector2(1120, 330);
        _ui.AddChild(_squadRow);

        // Player one is the keyboard; pads already in the squad come back
        // with it. Anyone else joins by pressing A.
        AddPicker(LocalDeviceInput.Keyboard);
        foreach (int d in Campaign.SquadDevices)
            if (d != LocalDeviceInput.Keyboard) AddPicker(d);
        _hint.Text = "LEFT/RIGHT  PICK A CLOWN      ATTACK/JUMP  READY      ESC/B  BACK      PADS: PRESS A TO JOIN";
        Sfx.Play("boing_small");
    }

    private void AddPicker(int device)
    {
        if (_pickers.Count >= Roster.All.Length || _pickers.Exists(p => p.Device == device)) return;
        int index = _pickers.Count;
        // Start on the clown this slot played last, or the first free one.
        string saved = SaveSystem.Data.Characters.Count > index ? SaveSystem.Data.Characters[index].Look : "";
        int choice = Array.FindIndex(Roster.All, c => c.Id == saved);
        if (choice < 0 || _pickers.Exists(p => p.Choice == choice)) choice = FreeChoice(index, 1);
        var picker = new Picker { Device = device, Choice = choice, Pad = new MapPad(device) };
        var card = new PanelContainer { CustomMinimumSize = new Vector2(250, 320) };
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        card.AddChild(box);
        picker.Title = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(34, Colors.White, 8) };
        box.AddChild(picker.Title);
        var stage = new Control { CustomMinimumSize = new Vector2(220, 170) };
        box.AddChild(stage);
        picker.Sprite = new AnimatedSprite2D { Position = new Vector2(110, 120), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        stage.AddChild(picker.Sprite);
        picker.Status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(220, 0), LabelSettings = Style(14, new Color(0.9f, 0.88f, 1f), 3),
        };
        box.AddChild(picker.Status);
        picker.Card = card;
        _squadRow!.AddChild(card);
        _pickers.Add(picker);
        RefreshPicker(picker);
        if (index > 0) Sfx.Play("ding");
    }

    private int FreeChoice(int from, int step)
    {
        int n = Roster.All.Length;
        for (int i = 0; i < n; i++)
        {
            int c = ((from + i * step) % n + n) % n;
            if (!_pickers.Exists(p => p.Choice == c)) return c;
        }
        return 0;
    }

    private void RefreshPicker(Picker p)
    {
        Roster.Clown clown = Roster.All[p.Choice];
        int n = _pickers.IndexOf(p) + 1;
        p.Title.Text = clown.Name;
        p.Title.LabelSettings.FontColor = clown.Color.Lightened(0.2f);
        p.Card.AddThemeStyleboxOverride("panel", Plate(new Color(0.18f, 0.12f, 0.28f, 0.94f), p.Ready ? new Color(0.5f, 1f, 0.6f) : clown.Color, 16, p.Ready ? 6 : 4));
        string sheet = Roster.Sheet(clown.Id);
        p.Sprite.SpriteFrames = FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet));
        p.Sprite.Scale = Vector2.One * (ArtStyle.Classic ? 2f : 1f);
        p.Sprite.Play(p.Ready ? "walk" : "idle");
        string who = p.Device == LocalDeviceInput.Keyboard ? "KEYBOARD" : $"PAD {p.Device}";
        p.Status.Text = $"P{n}  ({who})\n{clown.Blurb}\n" + (p.Ready ? "READY!" : "< PICK >");
    }

    private void StartGame()
    {
        // Each player slot takes the chosen clown's name and colours; levels,
        // gear and gold stay with the slot.
        Campaign.SquadDevices.Clear();
        for (int i = 0; i < _pickers.Count; i++)
        {
            Roster.Clown clown = Roster.All[_pickers[i].Choice];
            CharacterRecord r = SaveSystem.CharacterFor(i, clown.Name);
            r.Name = clown.Name;
            r.Look = clown.Id;
            Campaign.SquadDevices.Add(_pickers[i].Device);
        }
        SaveSystem.Save();
        _circus.Cheer(3f, 12);
        Sfx.Play("fanfare");
        _mode = Mode.Main;      // no more input while the crowd cheers
        _starting = true;
        GetTree().CreateTimer(0.9).Timeout += () => Go(Campaign.MapScene);
    }

    private bool _starting;

    // --------------------------------------------------------------- input

    public override void _Process(double delta)
    {
        _clock += delta;
        _logo.Rotation = Mathf.Sin((float)_clock * 1.6f) * 0.025f;
        _logo.Scale = Vector2.One * (1f + 0.025f * Mathf.Sin((float)_clock * 3.1f));
        foreach (DirectionalSprite s in _standees) s.Tick((float)delta);
        if (_starting || _intro is not null || _settings is not null || _bestiary is not null) return;
        _menuBacking.Visible = _mode != Mode.Squad;

        if (_mode == Mode.Squad) { TickSquad(); return; }
        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p == MapPad.Press.None) continue;
            if (_mode == Mode.Main) TickMain(p);
            else if (_mode == Mode.Slots) TickSlots(p);
            break;
        }
    }

    private void TickMain(MapPad.Press p)
    {
        switch (p)
        {
            case MapPad.Press.Up: _mainCursor = (_mainCursor + _main.Count - 1) % _main.Count; Sfx.Play("blip", 1.1f); RefreshMain(); break;
            case MapPad.Press.Down: _mainCursor = (_mainCursor + 1) % _main.Count; Sfx.Play("blip"); RefreshMain(); break;
            case MapPad.Press.Enter: Sfx.Play("clack"); _main[_mainCursor].act(); break;
        }
    }

    private void TickSlots(MapPad.Press p)
    {
        switch (p)
        {
            case MapPad.Press.Up: _slotCursor = (_slotCursor + SaveSystem.Slots - 1) % SaveSystem.Slots; _confirmSlot = -1; Sfx.Play("blip", 1.1f); RefreshSlots(); break;
            case MapPad.Press.Down: _slotCursor = (_slotCursor + 1) % SaveSystem.Slots; _confirmSlot = -1; Sfx.Play("blip"); RefreshSlots(); break;
            case MapPad.Press.Enter: PickSlot(); break;
            case MapPad.Press.Back: Sfx.Play("pop"); ShowMain(); break;
        }
    }

    private void TickSquad()
    {
        // New pads join by pressing A.
        foreach (int pad in Godot.Input.GetConnectedJoypads())
            if (!_pickers.Exists(p => p.Device == pad) && Godot.Input.IsJoyButtonPressed(pad, JoyButton.A)) AddPicker(pad);

        foreach (Picker p in _pickers.ToArray())
        {
            switch (p.Pad.Poll())
            {
                case MapPad.Press.Left when !p.Ready:
                    p.Choice = FreeChoice(p.Choice - 1, -1); Sfx.Play("blip", 1.1f); RefreshPicker(p); break;
                case MapPad.Press.Right when !p.Ready:
                    p.Choice = FreeChoice(p.Choice + 1, 1); Sfx.Play("blip"); RefreshPicker(p); break;
                case MapPad.Press.Enter:
                    if (!p.Ready) { p.Ready = true; Sfx.Play("coin"); RefreshPicker(p); }
                    break;
                case MapPad.Press.Back:
                    if (p.Ready) { p.Ready = false; Sfx.Play("pop"); RefreshPicker(p); }
                    else if (_pickers.IndexOf(p) == 0) { Sfx.Play("pop"); ShowMain(); return; }
                    else { _pickers.Remove(p); p.Card.QueueFree(); Sfx.Play("pop"); foreach (Picker q in _pickers) RefreshPicker(q); }
                    break;
            }
        }
        if (_pickers.Count > 0 && _pickers.TrueForAll(p => p.Ready)) StartGame();
    }
}
