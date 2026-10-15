namespace ClownSquad.UI;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;

/// <summary>
/// "NEW ACT!" The first time a kind of enemy turns up, a small comic card
/// slides in at the side with its portrait, its name and the one thing to know
/// about fighting it, then slides away. The fight carries on underneath.
/// Several new acts at once queue up and take turns.
/// </summary>
public partial class ActCard : CanvasLayer
{
    private static readonly Queue<string> Pending = new();
    private static bool _showing;
    private string _kind = "";

    public static void Show(Node owner, string kind)
    {
        Pending.Enqueue(kind);
        if (!_showing && owner.IsInsideTree()) Next(owner.GetTree().Root);
    }

    private static void Next(Node root)
    {
        if (Pending.Count == 0) { _showing = false; return; }
        _showing = true;
        root.AddChild(new ActCard { _kind = Pending.Dequeue(), Layer = 17 });
    }

    public override void _Ready()
    {
        (string title, _, string tip) = EnemyCatalog.Info(_kind);
        Vector2 view = GetViewport().GetVisibleRect().Size;
        var root = new Control { Position = new Vector2(-460f, view.Y * 0.36f), RotationDegrees = -2f };
        AddChild(root);
        var plate = new Panel { Size = new Vector2(430f, 120f) };
        plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.9f, 0.4f), BorderColor = new Color(0.08f, 0.04f, 0.1f),
            BorderWidthLeft = 5, BorderWidthRight = 5, BorderWidthTop = 5, BorderWidthBottom = 5,
            CornerRadiusTopRight = 10, CornerRadiusBottomRight = 10,
            ShadowColor = new Color(0f, 0f, 0f, 0.4f), ShadowSize = 0, ShadowOffset = new Vector2(6f, 7f),
        });
        root.AddChild(plate);
        TextureRect face = Portraits.Make(EnemyCatalog.SheetOf(_kind), 96f);
        face.Position = new Vector2(12f, 12f);
        root.AddChild(face);
        var tag = new PanelContainer { Position = new Vector2(118f, -14f), RotationDegrees = 3f };
        tag.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.86f, 0.18f, 0.24f), BorderColor = new Color(0.08f, 0.04f, 0.1f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 8, ContentMarginRight = 8,
        });
        tag.AddChild(new Label { Text = "NEW ACT!", LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 18, FontColor = Colors.White } });
        root.AddChild(tag);
        root.AddChild(new Label
        {
            Text = title, Position = new Vector2(118f, 14f), Size = new Vector2(300f, 36f),
            LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 28, FontColor = new Color(0.12f, 0.06f, 0.16f) },
        });
        var tipLabel = new Label
        {
            Text = tip, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(296f, 0f),
            LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 15, FontColor = new Color(0.25f, 0.12f, 0.2f) },
        };
        root.AddChild(tipLabel);
        tipLabel.Position = new Vector2(118f, 50f);
        tipLabel.Size = new Vector2(296f, 64f);

        Sfx.Play("tada", 1.15f, -4f);
        Tween t = CreateTween();
        t.TweenProperty(root, "position:x", 0f, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenInterval(3.2f);
        t.TweenProperty(root, "position:x", -480f, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        t.TweenCallback(Callable.From(() =>
        {
            Node r = GetTree().Root;
            QueueFree();
            Next(r);
        }));
    }

    public override void _ExitTree()
    {
        if (Pending.Count == 0) _showing = false;
    }
}
