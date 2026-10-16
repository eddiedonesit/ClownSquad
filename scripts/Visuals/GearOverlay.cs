namespace ClownSquad.Visuals;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Progression;

/// <summary>
/// A squad clown's gear, worn: the hat on the brim, shoes on both feet, the
/// neckpiece and the weapon charm, each a sprite that follows its named point
/// in whatever frame the clown is showing (the bare sheets carry those points,
/// and the turn of a somersault or a fall, so gear tumbles with the clown).
/// Lit, tinted and flashed with the body.
/// </summary>
public partial class GearOverlay : Node3D
{
    private sealed class Piece
    {
        public string Anchor = "";
        public bool Far;
        public AnimatedSprite3D Sprite = null!;
        public ShaderMaterial? Lit;
    }

    private readonly PlayerFighter _owner;
    private readonly DirectionalSprite _body;
    private readonly float _pixel;
    private readonly List<Piece> _pieces = new();

    public GearOverlay(PlayerFighter owner, DirectionalSprite body, float pixel)
    {
        _owner = owner;
        _body = body;
        _pixel = pixel;
    }

    /// <summary>Put on what this record wears (call again after equipping).</summary>
    public void Dress(CharacterRecord record)
    {
        foreach (Piece p in _pieces) p.Sprite.QueueFree();
        _pieces.Clear();
        foreach ((string anchor, string sheet, bool far) in GearLook.Pieces(record))
        {
            var sprite = new AnimatedSprite3D
            {
                SpriteFrames = FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet)),
                PixelSize = _pixel,
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                Shaded = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            ShaderMaterial? lit = LitSprite.For(sheet);
            if (lit is not null) sprite.MaterialOverride = lit;
            AddChild(sprite);
            _pieces.Add(new Piece { Anchor = anchor, Far = far, Sprite = sprite, Lit = lit });
        }
        Follow();
    }

    public override void _Process(double delta) => Follow();

    private void Follow()
    {
        bool shown = _body.Visible && _owner.Visible;
        bool mirrored = _body.Mirrored;
        // the turn of the frame (a somersault, a fall): gear is drawn turned in 45 degree steps
        int step = _body.AnchorRaw("rot") is Vector2 rot ? Mathf.PosMod(Mathf.RoundToInt(rot.X / 45f), 8) : 0;
        if (mirrored) step = (8 - step) % 8;
        Color tint = _body.Tint;
        float flash = _body.CurrentModulate.R > 2f ? 1.1f : 0f;
        foreach (Piece p in _pieces)
        {
            Vector3? at = _body.Anchor(p.Anchor);
            p.Sprite.Visible = shown && at is not null;
            if (at is not Vector3 a) continue;
            // in front of the body, the far shoe just behind it
            p.Sprite.Position = _body.Position + a + new Vector3(0f, 0f, p.Far ? -0.004f : 0.006f);
            p.Sprite.Animation = (p.Far ? "f" : "r") + step;
            p.Sprite.FlipH = mirrored;
            if (p.Lit is not null) LitSprite.Apply(p.Lit, tint, flash, mirrored);
            else p.Sprite.Modulate = tint;
        }
    }
}
