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
    private static readonly Dictionary<string, SpriteFrames> Cache = new();

    public static SpriteFrames Load(string sheetPath, string framesPath)
    {
        if (Cache.TryGetValue(sheetPath, out SpriteFrames? cached)) return cached;

        var sheet = GD.Load<Texture2D>(sheetPath);
        using FileAccess f = FileAccess.Open(framesPath, FileAccess.ModeFlags.Read);
        var meta = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        int fw = (int)meta["frame_width"];
        int fh = (int)meta["frame_height"];
        var clips = meta["clips"].AsGodotDictionary();

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        foreach (Variant key in clips.Keys)
        {
            string name = key.AsString();
            frames.AddAnimation(name);
            // Match on suffix so every direction prefix shares looping and rate.
            frames.SetAnimationLoop(name, name.EndsWith("idle") || name.EndsWith("walk"));
            frames.SetAnimationSpeed(name,
                  name.EndsWith("idle") ? 3.0
                : name.EndsWith("walk") ? 11.0
                : name.EndsWith("attack") ? 14.0
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
