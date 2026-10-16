namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Progression;

/// <summary>
/// The inventory: everything a player owns, in one place. What is worn sits at
/// the top, the bag below it, and the highlighted item's full stats underneath,
/// compared with what is worn in that slot. Put something on, or sell what is
/// not wanted for gold. One card per player, each on their own controller.
/// Opened from the pause menu, in a stage or on the world map.
///
/// Controls: up/down browse, ATTACK wear it, DODGE sell it (press twice), JUMP done.
/// </summary>
public partial class InventoryMenu : CanvasLayer
{
    private const int Rows = 6;
    private static readonly Color Gold = new(1f, 0.85f, 0.3f);
    private static readonly Color Cream = new(1f, 0.97f, 0.9f);
    private static readonly Color Dim = new(0.75f, 0.7f, 0.85f);

    private readonly List<Seat> _seats;
    private readonly List<Card> _cards = new();
    private HBoxContainer _row = null!;

    public event Action? Closed;

    public InventoryMenu(IEnumerable<Seat> seats)
    {
        _seats = new List<Seat>(seats);
        Layer = 21;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>What an item sells back for: a third of its shop price.</summary>
    public static int SellPrice(ItemData item) => Mathf.Max(1, ShopMenu.Price(item) / 3);

    public override void _Ready()
    {
        GetTree().Paused = true;
        var shade = new ColorRect { Color = new Color(0.06f, 0.02f, 0.1f, 0.8f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var title = new Label { Text = "INVENTORY", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, Gold, 10) };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 14;
        AddChild(title);
        _row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 14);
        _row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _row.OffsetTop = 72;
        AddChild(_row);
        foreach (Seat s in _seats)
        {
            var c = new Card(s, new MenuPad(s.Device));
            Build(c);
            _cards.Add(c);
            Refresh(c);
        }
        Sfx.Play("boing_small", 0.9f);
    }

    public override void _Process(double delta)
    {
        bool allDone = _cards.Count > 0;
        foreach (Card c in _cards)
        {
            if (!c.Done) Drive(c);
            allDone &= c.Done;
        }
        if (!allDone) return;
        GetTree().Paused = false;
        SaveSystem.Save();
        Closed?.Invoke();
        QueueFree();
    }

    private static List<ItemData> Bag(CharacterRecord r)
    {
        var bag = new List<ItemData>(r.Stash);
        bag.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : Loot.Score(b).CompareTo(Loot.Score(a)));
        return bag;
    }

    private void Drive(Card c)
    {
        MenuPad.Press press = c.Pad.Poll();
        if (press == MenuPad.Press.None) return;
        CharacterRecord r = c.Seat.Record;
        List<ItemData> bag = Bag(r);
        if (press != MenuPad.Press.Undo) c.SellArmed = false;
        switch (press)
        {
            case MenuPad.Press.Up when bag.Count > 0:
                c.Cursor = (c.Cursor + bag.Count - 1) % bag.Count; Sfx.Play("blip", 1.1f); c.Message = ""; break;
            case MenuPad.Press.Down when bag.Count > 0:
                c.Cursor = (c.Cursor + 1) % bag.Count; Sfx.Play("blip"); c.Message = ""; break;
            case MenuPad.Press.Add when bag.Count > 0:
                ItemData wear = bag[c.Cursor];
                r.Equip(wear.Slot, wear);
                c.Seat.Changed?.Invoke();
                c.Message = $"NOW WEARING {wear.Name}";
                Sfx.Play("clack", 1.2f);
                break;
            case MenuPad.Press.Undo when bag.Count > 0:
                ItemData sell = bag[c.Cursor];
                if (!c.SellArmed) { c.SellArmed = true; c.Message = $"SELL FOR {SellPrice(sell)} G? PRESS DODGE AGAIN"; Sfx.Play("honk_low", 1.3f, -8f); break; }
                r.Stash.Remove(sell);
                r.Gold += SellPrice(sell);
                c.SellArmed = false;
                c.Message = $"SOLD FOR {SellPrice(sell)} G";
                Sfx.Play("coin");
                break;
            case MenuPad.Press.Done:
                c.Done = true;
                Sfx.Play("clack");
                break;
        }
        c.Cursor = Mathf.Clamp(c.Cursor, 0, Mathf.Max(0, Bag(r).Count - 1));
        Refresh(c);
    }

    private void Refresh(Card c)
    {
        CharacterRecord r = c.Seat.Record;
        c.Title.Text = r.Name;
        c.GoldLabel.Text = $"{r.Gold} G";
        ItemSlot[] slots = { ItemSlot.Mallet, ItemSlot.Hat, ItemSlot.Ruff, ItemSlot.Shoes };
        for (int i = 0; i < 4; i++)
        {
            ItemData? worn = r.EquippedIn(slots[i]);
            c.Worn[i].Text = $"{slots[i].ToString().ToUpperInvariant()}:  {worn?.Name ?? "- nothing -"}";
            c.Worn[i].LabelSettings.FontColor = worn is null ? Dim : Loot.RarityColor(worn.Rarity);
        }

        List<ItemData> bag = Bag(r);
        // the picked item, tried on
        c.Doll?.Show(r, bag.Count > 0 && !c.Done ? bag[Mathf.Clamp(c.Cursor, 0, bag.Count - 1)] : null);
        c.BagTitle.Text = bag.Count == 0 ? "BAG  - EMPTY -" : $"BAG  ({bag.Count})";
        int first = Mathf.Clamp(c.Cursor - Rows / 2, 0, Mathf.Max(0, bag.Count - Rows));
        for (int i = 0; i < Rows; i++)
        {
            int at = first + i;
            Label l = c.Lines[i];
            l.Visible = at < bag.Count;
            c.LineBg[i].Visible = false;
            if (!l.Visible) continue;
            ItemData it = bag[at];
            bool sel = at == c.Cursor;
            l.Text = (sel ? "> " : "  ") + $"[{it.Slot.ToString()[0]}] {it.Name}";
            l.LabelSettings.FontColor = Loot.RarityColor(it.Rarity);
            c.LineBg[i].Visible = sel && !c.Done;
        }

        if (bag.Count == 0)
            c.Details.Text = "Loot you bank by clearing stages lands here.\nShops sell more.";
        else
        {
            ItemData sel = bag[c.Cursor];
            ItemData? worn = r.EquippedIn(sel.Slot);
            float delta = Loot.Score(sel) - (worn is null ? 0f : Loot.Score(worn));
            var lines = new System.Text.StringBuilder();
            lines.AppendLine($"{sel.Rarity.ToString().ToUpperInvariant()} {sel.Slot.ToString().ToUpperInvariant()}  -  LV {sel.Level}  -  SELLS {SellPrice(sel)} G");
            foreach (AffixData a in sel.Affixes) lines.AppendLine("  " + Loot.Describe(a));
            lines.Append(worn is null ? "  (nothing worn here: an easy upgrade)"
                       : delta > 0.5f ? $"  BETTER than {worn.Name}" : delta < -0.5f ? $"  WORSE than {worn.Name}" : $"  about the same as {worn.Name}");
            c.Details.Text = lines.ToString();
        }
        c.Footer.Text = c.Done ? "DONE!" : (c.Message.Length > 0 ? c.Message : "ATTACK wear    DODGE sell    JUMP done");
    }

    private void Build(Card c)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        panel.AddThemeStyleboxOverride("panel", Plate(new Color(0.2f, 0.12f, 0.3f, 0.97f), Gold));
        _row.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);
        var head = new HBoxContainer();
        box.AddChild(head);
        c.Title = new Label { LabelSettings = Style(24, Gold, 6), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        c.GoldLabel = new Label { LabelSettings = Style(20, Gold, 5) };
        head.AddChild(c.Title);
        head.AddChild(c.GoldLabel);
        // the clown, dressed in what they wear, beside the list of it
        var dressed = new HBoxContainer();
        dressed.AddThemeConstantOverride("separation", 6);
        box.AddChild(dressed);
        c.Doll = new GearDoll();
        dressed.AddChild(c.Doll);
        var wearing = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dressed.AddChild(wearing);
        wearing.AddChild(new Label { Text = "WEARING", LabelSettings = Style(14, Dim, 3) });
        for (int i = 0; i < 4; i++)
        {
            var l = new Label { LabelSettings = Style(14, Cream, 4), CustomMinimumSize = new Vector2(214, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            wearing.AddChild(l);
            c.Worn.Add(l);
        }
        c.BagTitle = new Label { LabelSettings = Style(14, Dim, 3) };
        box.AddChild(c.BagTitle);
        for (int i = 0; i < Rows; i++)
        {
            var row = new Control { CustomMinimumSize = new Vector2(350, 22) };
            box.AddChild(row);
            var bg = new Panel { Size = new Vector2(356, 22), Position = new Vector2(-3, 0), Visible = false };
            bg.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f, 0.55f), new Color(1f, 0.85f, 0.3f, 0.9f), 6, 2));
            row.AddChild(bg);
            var l = new Label { Position = new Vector2(2, 1), Size = new Vector2(350, 20), ClipText = true, LabelSettings = Style(13, Cream, 3) };
            row.AddChild(l);
            c.Lines.Add(l);
            c.LineBg.Add(bg);
        }
        c.Details = new Label { LabelSettings = Style(12, Cream, 3), CustomMinimumSize = new Vector2(350, 128) };
        box.AddChild(c.Details);
        c.Footer = new Label { LabelSettings = Style(13, Dim, 4), HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(c.Footer);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f), ShadowSize = 2, ShadowColor = new Color(0, 0, 0, 0.45f), ShadowOffset = new Vector2(2, 3),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 14, int width = 4) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 12,
    };

    private sealed class Card
    {
        public GearDoll? Doll;
        public readonly Seat Seat;
        public readonly MenuPad Pad;
        public readonly List<Label> Worn = new(), Lines = new();
        public readonly List<Panel> LineBg = new();
        public int Cursor;
        public bool Done, SellArmed;
        public string Message = "";
        public Label Title = null!, GoldLabel = null!, BagTitle = null!, Details = null!, Footer = null!;

        public Card(Seat seat, MenuPad pad)
        {
            Seat = seat;
            Pad = pad;
        }
    }
}
