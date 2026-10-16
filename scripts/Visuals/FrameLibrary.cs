namespace ClownSquad.Visuals;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Builds and caches SpriteFrames from a sheet plus its JSON frame map.
/// Shared per sheet, since atlases are immutable.
///
/// Note: Godot does not reimport changed assets when running the game binary.
/// Regenerate art with tools/refresh_sprites.sh, which imports as well.
/// </summary>
public static class FrameLibrary
{
    /// <summary>Frame geometry a sprite needs in order to stand on the ground.</summary>
    public readonly record struct SheetInfo(int FrameWidth, int FrameHeight, int Baseline);

    private static readonly Dictionary<string, SpriteFrames> Cache = new();
    private static readonly Dictionary<string, SheetInfo> Infos = new();
    // Packed sheets: where each frame's trimmed box sits on the sheet, and its offset in the full frame.
    private static readonly Dictionary<string, (Rect2 region, Vector2 offset)[]> Packed = new();
    private static readonly Dictionary<string, Dictionary<string, int[]>> ClipIndex = new();

    /// <summary>
    /// Geometry for a loaded sheet. Baseline is the pixel row the feet stand on;
    /// sheets that pad below the feet (room for a flip to rotate through) record
    /// it explicitly, and it defaults to the frame bottom otherwise.
    /// </summary>
    public static SheetInfo Info(string sheetPath) =>
        Infos.TryGetValue(sheetPath, out SheetInfo info) ? info : new SheetInfo(64, 64, 64);

    /// <summary>The sheet index of frame i of a clip (0 if the clip is missing).</summary>
    public static int FrameIndex(string sheetPath, string clip, int i = 0)
    {
        Load(sheetPath, ArtStyle.FramesFor(sheetPath));
        return ClipIndex.TryGetValue(sheetPath, out var idx) && idx.TryGetValue(clip, out int[]? ids) && ids.Length > 0
            ? ids[System.Math.Min(i, ids.Length - 1)] : 0;
    }

    /// <summary>Where a frame's own box sits on the sheet, and where that box sits inside the full frame.</summary>
    public static (Rect2 region, Vector2 offset) FrameBox(string sheetPath, int frame)
    {
        Load(sheetPath, ArtStyle.FramesFor(sheetPath));
        if (Packed.TryGetValue(sheetPath, out var packed) && frame < packed.Length) return packed[frame];
        SheetInfo info = Info(sheetPath);
        return (new Rect2(frame * info.FrameWidth, 0, info.FrameWidth, info.FrameHeight), Vector2.Zero);
    }

    /// <summary>
    /// A rectangle given in frame coordinates (as if the frame were a whole,
    /// untrimmed picture) turned into sheet coordinates, clipped to what the
    /// sheet stores: what the HUD and portraits use to cut faces out of a sheet.
    /// </summary>
    public static Rect2 FrameToSheet(string sheetPath, int frame, Rect2 inFrame)
    {
        (Rect2 region, Vector2 off) = FrameBox(sheetPath, frame);
        Rect2 local = new(off, region.Size);
        Rect2 clipped = inFrame.Intersection(local);
        if (clipped.Size.X <= 0 || clipped.Size.Y <= 0) clipped = local;
        return new Rect2(region.Position + (clipped.Position - off), clipped.Size);
    }

    public static SpriteFrames Load(string sheetPath, string framesPath)
    {
        if (Cache.TryGetValue(sheetPath, out SpriteFrames? cached)) return cached;

        var sheet = GD.Load<Texture2D>(sheetPath);
        using FileAccess f = FileAccess.Open(framesPath, FileAccess.ModeFlags.Read);
        var meta = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        int fw = (int)meta["frame_width"];
        int fh = (int)meta["frame_height"];
        int baseline = meta.ContainsKey("baseline") ? (int)meta["baseline"] : fh;
        Infos[sheetPath] = new SheetInfo(fw, fh, baseline);
        var clips = meta["clips"].AsGodotDictionary();

        // A packed sheet (tools/pixelart_hd.pack_frames) lists each frame's
        // trimmed box; the margin added back keeps every frame fw x fh, so a
        // sprite stands exactly where it would on a plain strip.
        (Rect2 region, Vector2 offset)[]? packed = null;
        if (meta.ContainsKey("rects"))
        {
            var rects = meta["rects"].AsGodotArray();
            packed = new (Rect2, Vector2)[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i].AsGodotArray();
                packed[i] = (new Rect2((int)r[0], (int)r[1], (int)r[2], (int)r[3]), new Vector2((int)r[4], (int)r[5]));
            }
            Packed[sheetPath] = packed;
        }
        var index = new Dictionary<string, int[]>();
        foreach (Variant key in clips.Keys)
        {
            var list = clips[key].AsGodotArray();
            var ids = new int[list.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = (int)list[i];
            index[key.AsString()] = ids;
        }
        ClipIndex[sheetPath] = index;

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        foreach (Variant key in clips.Keys)
        {
            string name = key.AsString();
            frames.AddAnimation(name);
            // Match on suffix so prefixed sets (empty_walk) share looping and rate.
            bool loops = name.EndsWith("idle") || name.EndsWith("walk") || name.EndsWith("spin") || name.EndsWith("victory");
            frames.SetAnimationLoopMode(name, loops ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
            // A sheet may set its own rates (more in-betweens play faster);
            // otherwise these defaults, tuned for the classic frame counts.
            var fps = meta.ContainsKey("fps") ? meta["fps"].AsGodotDictionary() : null;
            if (fps is not null && fps.ContainsKey(name)) frames.SetAnimationSpeed(name, (double)fps[name]);
            else frames.SetAnimationSpeed(name,
                  name.EndsWith("idle") ? 3.0
                : name.EndsWith("walk") ? 11.0
                : name.EndsWith("attack") ? 14.0
                : name.EndsWith("burst") ? 16.0
                : name.EndsWith("spin") ? 20.0
                : 8.0);
            foreach (Variant idx in clips[key].AsGodotArray())
            {
                int i = (int)idx;
                if (packed is not null)
                {
                    (Rect2 region, Vector2 off) = packed[i];
                    frames.AddFrame(name, new AtlasTexture
                    {
                        Atlas = sheet,
                        Region = region,
                        Margin = new Rect2(off.X, off.Y, fw - region.Size.X, fh - region.Size.Y),
                        FilterClip = true,
                    });
                }
                else
                {
                    frames.AddFrame(name, new AtlasTexture { Atlas = sheet, Region = new Rect2(i * fw, 0, fw, fh), FilterClip = true });
                }
            }
        }
        Cache[sheetPath] = frames;
        return frames;
    }
}
