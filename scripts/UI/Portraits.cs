namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Visuals;

/// <summary>
/// Head-and-shoulders portraits cut from any sprite sheet: the top of the
/// figure in its first frame, found from the pixels once and remembered. The
/// HUD, act cards and bestiary all use it, so a new character gets a portrait
/// without anyone measuring it.
/// </summary>
public static class Portraits
{
    private static readonly Dictionary<string, Rect2> Cache = new();

    public static Rect2 FaceOf(string sheet)
    {
        if (Cache.TryGetValue(sheet, out Rect2 r)) return r;
        Image img = GD.Load<Texture2D>(sheet).GetImage();
        if (img.IsCompressed()) img.Decompress();
        FrameLibrary.SheetInfo info = FrameLibrary.Info(sheet);
        if (info.FrameWidth <= 64 && info.FrameHeight <= 64)
        {
            // Sheet not loaded by a sprite yet: read its frame map ourselves.
            FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet));
            info = FrameLibrary.Info(sheet);
        }
        // The "hit" pose is the one with nothing in the air above the head (no
        // juggled pins, no raised mallet), so its top is the face. Sheets
        // without one use their first frame.
        int ox = 0;
        SpriteFrames frames = FrameLibrary.Load(sheet, ArtStyle.FramesFor(sheet));
        if (frames.HasAnimation("hit") && frames.GetFrameTexture("hit", 0) is AtlasTexture hit) ox = (int)hit.Region.Position.X;
        int x0 = info.FrameWidth, x1 = 0, y0 = info.FrameHeight, y1 = 0;
        for (int y = 0; y < info.FrameHeight; y += 2)
            for (int x = 0; x < info.FrameWidth; x += 2)
                if (img.GetPixel(ox + x, y).A > 0.5f) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        float side = Math.Min(x1 - x0, (y1 - y0) * 0.45f);
        // A little below the very top, so a tall hat does not crowd out the face.
        r = new Rect2(ox + (x0 + x1) * 0.5f - side * 0.5f, y0 + side * 0.22f, side, side);
        // A long figure (a tiger, a car) faces right: its face is at the front end, not the middle.
        if (x1 - x0 > (y1 - y0) * 1.3f)
        {
            side = (y1 - y0) * 0.7f;
            r = new Rect2(ox + x1 - side, y0, side, side);
        }
        Cache[sheet] = r;
        return r;
    }

    /// <summary>A ready-made portrait for a sheet.</summary>
    public static TextureRect Make(string sheet, float size) => new()
    {
        Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>(sheet), Region = FaceOf(sheet) },
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        CustomMinimumSize = new Vector2(size, size),
        Size = new Vector2(size, size),
    };
}
