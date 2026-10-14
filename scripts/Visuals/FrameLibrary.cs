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

    /// <summary>
    /// Geometry for a loaded sheet. Baseline is the pixel row the feet stand on;
    /// sheets that pad below the feet (room for a flip to rotate through) record
    /// it explicitly, and it defaults to the frame bottom otherwise.
    /// </summary>
    public static SheetInfo Info(string sheetPath) =>
        Infos.TryGetValue(sheetPath, out SheetInfo info) ? info : new SheetInfo(64, 64, 64);

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

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        foreach (Variant key in clips.Keys)
        {
            string name = key.AsString();
            frames.AddAnimation(name);
            // Match on suffix so prefixed sets (empty_walk) share looping and rate.
            bool loops = name.EndsWith("idle") || name.EndsWith("walk") || name.EndsWith("spin");
            frames.SetAnimationLoop(name, loops);
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
                frames.AddFrame(name, new AtlasTexture
                {
                    Atlas = sheet,
                    Region = new Rect2((int)idx * fw, 0, fw, fh),
                    FilterClip = true,
                });
            }
        }
        Cache[sheetPath] = frames;
        return frames;
    }
}
