namespace ClownSquad.UI;

using Godot;

/// <summary>
/// The game's lettering: Luckiest Guy (Apache 2.0, bundled in assets/fonts with
/// its licence). Fat, bouncy circus-poster capitals, chosen from the candidates
/// on 2026-10-14. Everything that draws text asks for it here, so the face can
/// change in one place. Falls back to a chunky system face if the file is missing.
/// </summary>
public static class UiFont
{
    private const string Path = "res://assets/fonts/LuckiestGuy-Regular.ttf";
    private static Font? _display;

    public static Font Display => _display ??= Load();

    private static Font Load()
    {
        if (ResourceLoader.Exists(Path) && GD.Load<FontFile>(Path) is FontFile f) return f;
        GD.PushWarning("UiFont: Luckiest Guy missing; using a system font.");
        return new SystemFont { FontNames = new[] { "Comic Sans MS", "Segoe UI Black", "Arial Black" }, FontWeight = 800 };
    }
}
