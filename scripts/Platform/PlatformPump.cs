namespace ClownSquad.Services;

using Godot;

/// <summary>
/// Keeps the store layer running: calls <see cref="IPlatform.Tick"/> every
/// frame (Steam delivers its callbacks that way) and pauses the game while the
/// store's overlay is open, as Steam asks of games. Lives under the scene root
/// so it survives scene changes; every top-level scene ensures it exists.
/// </summary>
public partial class PlatformPump : Node
{
    private static PlatformPump? _instance;
    private bool _pausedByOverlay;

    public static void Ensure(SceneTree tree)
    {
        if (_instance is not null && IsInstanceValid(_instance)) return;
        _instance = new PlatformPump { Name = "PlatformPump", ProcessMode = ProcessModeEnum.Always };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    public override void _Ready() => Platform.Current.OverlayToggled += OnOverlay;

    public override void _ExitTree()
    {
        Platform.Current.OverlayToggled -= OnOverlay;
        if (_instance == this) _instance = null;
    }

    public override void _Process(double delta) => Platform.Current.Tick();

    private void OnOverlay(bool open)
    {
        if (open && !GetTree().Paused) { GetTree().Paused = true; _pausedByOverlay = true; }
        else if (!open && _pausedByOverlay) { GetTree().Paused = false; _pausedByOverlay = false; }
    }
}
