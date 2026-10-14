namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Progression;

/// <summary>Someone browsing a shop: whose gold, which controller, and what to refresh after a purchase.</summary>
public readonly record struct Shopper(CharacterRecord Record, int Device, Action? Changed);

/// <summary>
/// A shop: a shared shelf of rolled items, one card per player browsing it on
/// their own controller, each paying from their own gold. Bought items go
/// straight to the buyer's stash, paid for with banked gold, so nothing bought
/// can be lost to a failed stage. Used by the world map's shops and by the
/// secret shop that sometimes appears mid-stage.
///
/// Controls: up/down browse, ATTACK buy, JUMP leave.
/// </summary>
public partial class ShopMenu : CanvasLayer
{
    private static readonly Color Gold = new(1f, 0.85f, 0.3f);
    private static readonly Color Cream = new(1f, 0.97f, 0.9f);
    private static readonly Color Dim = new(0.75f, 0.7f, 0.85f);

    private readonly string _title;
    private readonly List<Shopper> _shoppers;
    private readonly List<(ItemData item, int price)> _shelf;
    private readonly bool _pauses;
    private readonly List<Card> _cards = new();
    private Font _font = null!;
    private HBoxContainer _row = null!;

    public bool IsOpen { get; private set; }
    public event Action? Closed;

    public ShopMenu(string title, IEnumerable<(ItemData, int)> shelf, IEnumerable<Shopper> shoppers, bool pauses = true)
    {
        _title = title;
        _shelf = new List<(ItemData, int)>(shelf);
        _shoppers = new List<Shopper>(shoppers);
        _pauses = pauses;
        Layer = 21;
        ProcessMode = ProcessModeEnum.Always;
    }

    // ------------------------------------------------------------ stock

    private static readonly float[] RarityPrice = { 1f, 2.2f, 5f, 11f, 25f };

    public static int Price(ItemData item) =>
        Mathf.Max(5, (int)Mathf.Round(14f * Mathf.Pow(item.Level, 1.15f) * RarityPrice[(int)item.Rarity] * item.Quality));

    /// <summary>A shelf of items rolled from a seed: the same seed always stocks the same shelf.</summary>
    public static List<(ItemData, int)> Stock(ulong seed, int level, int count, float rarityBoost)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var shelf = new List<(ItemData, int)>();
        for (int i = 0; i < count; i++)
        {
            ItemData item = Loot.Roll(rng, level + rng.RandiRange(0, 1), rarityBoost);
            shelf.Add((item, Price(item)));
        }
        shelf.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return shelf;
    }

    // -------------------------------------------------------------- life

    public override void _Ready()
    {
        _font = UiFont.Display;
        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var shade = new ColorRect { Color = new Color(0.06f, 0.02f, 0.1f, 0.78f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(shade);
        var title = new Label { Text = _title, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(42, Gold, 10) };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 26;
        root.AddChild(title);
        _row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 16);
        _row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _row.OffsetTop = 96;
        root.AddChild(_row);

        foreach (Shopper s in _shoppers)
        {
            var card = new Card(s, new MenuPad(s.Device));
            BuildCard(card);
            _cards.Add(card);
        }
        IsOpen = _cards.Count > 0;
        if (!IsOpen) { QueueFree(); return; }
        if (_pauses) GetTree().Paused = true;
        Sfx.Play("ding", 0.8f);
        foreach (Card c in _cards) Refresh(c);
    }

    private void Close()
    {
        IsOpen = false;
        if (_pauses) GetTree().Paused = false;
        SaveSystem.Save();
        Closed?.Invoke();
        QueueFree();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;
        bool allDone = true;
        foreach (Card c in _cards)
        {
            if (!c.Done) Drive(c);
            allDone &= c.Done;
        }
        if (allDone) Close();
    }

    /// <summary>Buy the shelf item at an index for a shopper. Public so tests can shop without a controller.</summary>
    public static bool TryBuy(CharacterRecord r, List<(ItemData item, int price)> shelf, int index, out string message)
    {
        (ItemData item, int price) = shelf[index];
        if (r.Gold < price) { message = "NOT ENOUGH GOLD"; return false; }
        r.Gold -= price;
        r.Stash.Add(item);
        // Put it on if the slot is empty or it is plainly better.
        ItemData? worn = r.EquippedIn(item.Slot);
        if (worn is null || Loot.Score(item) > Loot.Score(worn)) r.Equip(item.Slot, item);
        shelf.RemoveAt(index);
        message = $"BOUGHT {item.Name}";
        return true;
    }

    private void Drive(Card c)
    {
        MenuPad.Press press = c.Pad.Poll();
        if (press == MenuPad.Press.None) return;
        int n = _shelf.Count;
        switch (press)
        {
            case MenuPad.Press.Up when n > 0:
                c.Cursor = (c.Cursor + n - 1) % n;
                Sfx.Play("blip", 1.1f);
                break;
            case MenuPad.Press.Down when n > 0:
                c.Cursor = (c.Cursor + 1) % n;
                Sfx.Play("blip");
                break;
            case MenuPad.Press.Add when n > 0:
                if (!TryBuy(c.Shopper.Record, _shelf, c.Cursor, out c.Message)) { Sfx.Play("squeak", 0.7f); break; }
                c.Shopper.Changed?.Invoke();
                foreach (Card other in _cards) other.Cursor = Mathf.Clamp(other.Cursor, 0, Mathf.Max(0, _shelf.Count - 1));
                Sfx.Play("coin", 0.8f);
                Sfx.Play("ding", 1.2f);
                break;
            case MenuPad.Press.Done:
                c.Done = true;
                Sfx.Play("clack");
                break;
        }
        foreach (Card card in _cards) Refresh(card);
    }

    // ------------------------------------------------------------- cards

    private void Refresh(Card c)
    {
        CharacterRecord r = c.Shopper.Record;
        c.Name.Text = r.Name;
        c.Gold.Text = $"{r.Gold} G";
        for (int i = 0; i < c.Rows.Count; i++)
        {
            (Label name, Label price, Panel bg) = c.Rows[i];
            bool has = i < _shelf.Count;
            name.Visible = price.Visible = has;
            bg.Visible = has && i == c.Cursor && !c.Done;
            if (!has) continue;
            (ItemData item, int cost) = _shelf[i];
            name.Text = (i == c.Cursor ? "> " : "  ") + item.Name;
            name.LabelSettings.FontColor = Loot.RarityColor(item.Rarity);
            price.Text = $"{cost} G";
            price.LabelSettings.FontColor = r.Gold >= cost ? Gold : new Color(0.75f, 0.45f, 0.45f);
        }
        if (_shelf.Count == 0) c.Details.Text = "SOLD OUT!";
        else
        {
            var lines = new System.Text.StringBuilder();
            ItemData sel = _shelf[c.Cursor].item;
            lines.AppendLine($"{sel.Slot.ToString().ToUpperInvariant()}  -  {sel.Rarity}  -  level {sel.Level}");
            foreach (AffixData a in sel.Affixes) lines.AppendLine("  " + Loot.Describe(a));
            ItemData? worn = r.EquippedIn(sel.Slot);
            lines.Append(worn is null ? "  (nothing worn here)" : $"  wearing: {worn.Name}");
            c.Details.Text = lines.ToString();
        }
        c.Footer.Text = c.Done ? "SEE YOU AGAIN!" : (c.Message.Length > 0 ? c.Message : "ATTACK buy    JUMP leave");
    }

    private void BuildCard(Card c)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        panel.AddThemeStyleboxOverride("panel", Plate(new Color(0.2f, 0.12f, 0.3f, 0.97f), Gold));
        _row.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);
        var head = new HBoxContainer();
        box.AddChild(head);
        c.Name = new Label { LabelSettings = Style(24, Gold, 6), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        c.Gold = new Label { LabelSettings = Style(22, Gold, 5) };
        head.AddChild(c.Name);
        head.AddChild(c.Gold);
        for (int i = 0; i < Mathf.Max(1, _shelf.Count); i++)
        {
            var row = new Control { CustomMinimumSize = new Vector2(330, 26) };
            box.AddChild(row);
            var bg = new Panel { Size = new Vector2(336, 26), Position = new Vector2(-3, 0), Visible = false };
            bg.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f, 0.55f), new Color(1f, 0.85f, 0.3f, 0.9f), 6, 2));
            row.AddChild(bg);
            var name = new Label { Position = new Vector2(2, 2), Size = new Vector2(250, 22), ClipText = true, LabelSettings = Style(14, Cream, 4) };
            var price = new Label { Position = new Vector2(250, 2), Size = new Vector2(80, 22), HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = Style(14, Gold, 4) };
            row.AddChild(name);
            row.AddChild(price);
            c.Rows.Add((name, price, bg));
        }
        c.Details = new Label { LabelSettings = Style(13, Cream, 3), CustomMinimumSize = new Vector2(330, 140) };
        box.AddChild(c.Details);
        c.Footer = new Label { LabelSettings = Style(14, Dim, 4), HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(c.Footer);
    }

    private LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = _font, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        ShadowSize = 2, ShadowColor = new Color(0f, 0f, 0f, 0.45f), ShadowOffset = new Vector2(2f, 3f),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 14, int width = 4) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
    };

    private sealed class Card
    {
        public readonly Shopper Shopper;
        public readonly MenuPad Pad;
        public readonly List<(Label name, Label price, Panel bg)> Rows = new();
        public int Cursor;
        public bool Done;
        public string Message = "";
        public Label Name = null!, Gold = null!, Details = null!, Footer = null!;

        public Card(Shopper shopper, MenuPad pad)
        {
            Shopper = shopper;
            Pad = pad;
        }
    }
}
