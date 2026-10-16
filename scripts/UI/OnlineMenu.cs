namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Map;
using ClownSquad.Net;
using ClownSquad.Progression;

/// <summary>
/// Title > ONLINE: host a game or join one by IP address (no matchmaking).
/// The lobby lists everyone with their clown, level and weapon; the host sees
/// the addresses friends should type. Each player brings P1's character from
/// their own most recent save. The host's START opens their world map, and
/// every stage they enter, the guests play too (ADR 0010, phase 2).
/// </summary>
public partial class OnlineMenu : CanvasLayer
{
    private enum Page { Choose, Join, Lobby }

    private static string _lastAddress = "127.0.0.1";

    private readonly List<MapPad> _pads = new();
    private readonly CharacterRecord _me;
    private readonly List<(string label, Action act)> _items = new();
    private Page _page;
    private int _cursor;
    private VBoxContainer _box = null!;
    private Label _title = null!, _info = null!, _status = null!;
    private VBoxContainer _list = null!;
    private LineEdit? _address;
    private NetSession _net = null!;

    public event Action? Closed;
    /// <summary>The host pressed START: the title takes them to their world map.</summary>
    public event Func<string?>? HostStarts;

    public OnlineMenu(IEnumerable<int> devices, CharacterRecord me)
    {
        foreach (int d in devices) _pads.Add(new MapPad(d));
        _me = me;
        Layer = 24;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _net = NetSession.Ensure(GetTree());
        _net.Changed += OnNetChanged;
        _net.Ended += OnNetEnded;
        foreach (MapPad p in _pads) p.Swallow();

        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.9f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.97f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 14, ContentMarginBottom = 18,
        });
        centre.AddChild(panel);
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(_box);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(38, new Color(1f, 0.85f, 0.3f), 8) };
        _box.AddChild(_title);
        _info = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0), LabelSettings = Style(15, new Color(0.85f, 0.82f, 0.95f), 3) };
        _box.AddChild(_info);
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 4);
        _box.AddChild(_list);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0), LabelSettings = Style(15, new Color(1f, 0.75f, 0.45f), 3) };
        _box.AddChild(_status);
        // Back on the title after a stage online: straight to the lobby.
        Show(_net.Mode == NetSession.Role.Offline ? Page.Choose : Page.Lobby);
        Sfx.Play("pop", 0.9f);
    }

    public override void _ExitTree()
    {
        _net.Changed -= OnNetChanged;
        _net.Ended -= OnNetEnded;
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    private string Me => $"{_me.Name}  (LV {_me.Level}, {Weapons.Get(_me.Weapon).Name})";

    private void Show(Page page)
    {
        _page = page;
        _cursor = 0;
        _items.Clear();
        _address?.QueueFree();
        _address = null;
        switch (page)
        {
            case Page.Choose:
                _title.Text = "PLAY ONLINE";
                _info.Text = $"Playing as {Me}, from your last save.\nHost a game for up to 3 friends, or join one by its address.";
                _items.Add(("HOST A GAME", HostGame));
                _items.Add(("JOIN A GAME", () => Show(Page.Join)));
                _items.Add(("BACK", Close));
                break;
            case Page.Join:
                _title.Text = "JOIN A GAME";
                _info.Text = "Type the host's address (they can see it in their lobby), then press ENTER.\nOn the same Wi-Fi it starts 192.168. Over the internet the host must open UDP port " + NetSession.Port + ", or use a VPN app like Tailscale.";
                _address = new LineEdit
                {
                    Text = _lastAddress, PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(360, 44),
                    Alignment = HorizontalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                };
                _address.AddThemeFontOverride("font", UiFont.Display);
                _address.AddThemeFontSizeOverride("font_size", 24);
                _address.TextSubmitted += _ => JoinGame();
                _box.AddChild(_address);
                _box.MoveChild(_address, 2);
                _address.GrabFocus();
                _address.CaretColumn = _address.Text.Length;
                _items.Add(("CONNECT", JoinGame));
                _items.Add(("BACK", () => Show(Page.Choose)));
                break;
            case Page.Lobby:
                _title.Text = _net.Mode == NetSession.Role.Host ? "YOUR GAME" : "LOBBY";
                _items.Add(("START", Start));
                _items.Add(("LEAVE", () => { _net.Leave(); Show(Page.Choose); }));
                break;
        }
        Refresh();
    }

    /// <summary>Screenshot runs: host straight away.</summary>
    public void HostForShot() => HostGame();

    private void HostGame()
    {
        if (_net.Host(_me) == Error.Ok) { Sfx.Play("tada", 1.1f, -6f); Show(Page.Lobby); }
        else { Sfx.Play("honk_low", 1.2f, -6f); Refresh(); }
    }

    private void JoinGame()
    {
        string address = _address?.Text.Trim() ?? "";
        if (address.Length == 0) { Sfx.Play("honk_low", 1.2f, -6f); return; }
        _lastAddress = address;
        if (_net.Join(address, _me) == Error.Ok) { Sfx.Play("whoosh", 1.2f); Show(Page.Lobby); }
        else { Sfx.Play("honk_low", 1.2f, -6f); Refresh(); }
    }

    private void Start()
    {
        if (_net.Mode != NetSession.Role.Host)
        {
            Sfx.Play("honk_low", 1.1f, -6f);
            _status.Text = "Only the host can start. You'll join them as soon as they pick a stage.";
            return;
        }
        if (_net.Members.Count < 2)
        {
            Sfx.Play("honk_low", 1.1f, -6f);
            _status.Text = "Waiting for a friend to join first.";
            return;
        }
        // The host plays from their own world map; guests follow them into each stage.
        if (HostStarts?.Invoke() is string problem)
        {
            Sfx.Play("honk_low", 1.1f, -6f);
            _status.Text = problem;
        }
    }

    private void OnNetChanged() { if (IsInsideTree()) Refresh(); }

    private void OnNetEnded(string why)
    {
        if (!IsInsideTree()) return;
        Sfx.Play("crowd_boo", 1f, -8f);
        Show(Page.Choose);
        _status.Text = why;
    }

    private void Refresh()
    {
        foreach (Node n in _list.GetChildren()) n.QueueFree();
        if (_page == Page.Lobby)
        {
            if (_net.Mode == NetSession.Role.Host)
            {
                List<string> mine = NetSession.LocalAddresses();
                _info.Text = mine.Count > 0
                    ? $"Friends join with:  {string.Join("   or   ", mine)}\n(port {NetSession.Port}; on the same network, or over a VPN app)"
                    : $"Friends join with this computer's address (port {NetSession.Port}).";
            }
            else _info.Text = "Connected. Waiting for the host.";
            foreach (NetMember m in _net.Members)
            {
                var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                row.AddThemeConstantOverride("separation", 10);
                string sheet = Roster.Sheet(m.Look);
                TextureRect face = Portraits.Make(sheet, 40);
                row.AddChild(face);
                row.AddChild(new Label
                {
                    Text = $"{(m.IsHost ? "HOST  " : "")}{m.Name}   LV {m.Level}   {Weapons.Get(m.Weapon).Name}",
                    LabelSettings = Style(20, m.IsHost ? new Color(1f, 0.85f, 0.3f) : Colors.White, 4),
                });
                _list.AddChild(row);
            }
        }
        for (int i = 0; i < _items.Count; i++)
        {
            bool sel = i == _cursor;
            _list.AddChild(new Label
            {
                Text = (sel ? "> " : "  ") + _items[i].label, HorizontalAlignment = HorizontalAlignment.Center,
                LabelSettings = Style(26, sel ? new Color(1f, 0.85f, 0.3f) : Colors.White, 6),
            });
        }
        if (_net.Status.Length > 0) _status.Text = _net.Status;
    }

    public override void _Process(double delta)
    {
        // While typing an address, letters are text, not menu presses.
        bool typing = _address is not null && _address.HasFocus();
        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p == MapPad.Press.None || typing && p is not (MapPad.Press.Down or MapPad.Press.Back)) continue;
            if (typing && p == MapPad.Press.Down) { _address!.ReleaseFocus(); continue; }
            switch (p)
            {
                case MapPad.Press.Up:
                    if (_cursor == 0 && _address is not null) { _address.GrabFocus(); break; }
                    _cursor = (_cursor + _items.Count - 1) % _items.Count; Sfx.Play("blip", 1.1f); break;
                case MapPad.Press.Down: _cursor = (_cursor + 1) % _items.Count; Sfx.Play("blip"); break;
                case MapPad.Press.Enter: Sfx.Play("clack"); _items[_cursor].act(); return;
                case MapPad.Press.Back:
                    if (_page == Page.Lobby) { _net.Leave(); Show(Page.Choose); }
                    else if (_page == Page.Join) Show(Page.Choose);
                    else Close();
                    return;
            }
            Refresh();
            return;
        }
    }

    private void Close()
    {
        Sfx.Play("clack");
        Closed?.Invoke();
        QueueFree();
    }
}
