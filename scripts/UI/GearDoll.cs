namespace ClownSquad.UI;

using Godot;
using ClownSquad.Progression;
using ClownSquad.Visuals;

/// <summary>
/// The equipment screen's clown, dressed in what they wear (and, while an item
/// in the bag is picked, trying it on): the bare sheet's standing frame with
/// each gear piece laid over it at its named point.
/// </summary>
public partial class GearDoll : Control
{
    private readonly float _scale;

    public GearDoll(float scale = 1.05f)
    {
        _scale = scale;
        CustomMinimumSize = new Vector2(170, 230);
        ClipContents = true;
    }

    public void Show(CharacterRecord r, ItemData? tryOn = null)
    {
        foreach (Node n in GetChildren()) n.QueueFree();
        string bare = Roster.BareSheet(r.Look, r.Weapon);
        if (!ResourceLoader.Exists(bare)) return;
        SpriteFrames frames = FrameLibrary.Load(bare, ArtStyle.FramesFor(bare));
        FrameLibrary.SheetInfo info = FrameLibrary.Info(bare);
        int frame = FrameLibrary.FrameIndex(bare, "idle", 0);
        // the clown stands centred, feet a little above the bottom
        Vector2 size = CustomMinimumSize;
        Vector2 origin = new(size.X * 0.5f - info.FrameWidth * 0.5f * _scale, size.Y - 6 - info.Baseline * _scale);

        var pieces = GearLook.Pieces(r, tryOn);
        void Piece(string anchor, string sheet, bool far)
        {
            if (FrameLibrary.Anchor(bare, frame, anchor) is not Vector2 at) return;
            SpriteFrames g = FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet));
            FrameLibrary.SheetInfo gi = FrameLibrary.Info(sheet);
            var tex = new TextureRect
            {
                Texture = g.GetFrameTexture(far ? "f0" : "r0", 0), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = TextureFilterEnum.Nearest,
                Size = new Vector2(gi.FrameWidth, gi.FrameHeight) * _scale,
                Position = origin + at * _scale - new Vector2(gi.FrameWidth, gi.FrameHeight) * 0.5f * _scale,
                Modulate = far ? new Color(0.82f, 0.8f, 0.88f) : Colors.White,
            };
            AddChild(tex);
        }
        foreach (var p in pieces) if (p.far) Piece(p.anchor, p.sheet, true);
        AddChild(new TextureRect
        {
            Texture = frames.GetFrameTexture("idle", 0), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = TextureFilterEnum.Nearest,
            Size = new Vector2(info.FrameWidth, info.FrameHeight) * _scale, Position = origin,
        });
        foreach (var p in pieces) if (!p.far) Piece(p.anchor, p.sheet, false);
    }
}
