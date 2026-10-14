namespace ClownSquad.Visuals;

/// <summary>
/// Which art the game draws. The HD sheets (tools/gen_sprite_hd.py and
/// tools/gen_enemy_hd.py) are the default; the original sheets are kept intact
/// and come back with "-- classic". Both share clip names and poses, so only
/// the file and the pixel size differ: HD has twice the pixels, each half the size.
/// </summary>
public static class ArtStyle
{
    public static bool Classic { get; set; }

    public static string Sheet(string classic, string hd) => Classic ? classic : hd;

    public static float Pixel(float classicSize) => Classic ? classicSize : classicSize * 0.5f;

    /// <summary>A sheet's frame map sits beside it with the same name.</summary>
    public static string FramesFor(string sheet) => sheet.Replace(".png", ".frames.json");
}
