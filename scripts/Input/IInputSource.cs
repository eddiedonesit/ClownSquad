namespace ClownSquad.Input;

/// <summary>
/// Supplies commands for one fighter. Implementations: local device, remote
/// peer (online co-op), or AI. Gameplay depends on this interface only, which is
/// what lets online co-op be a transport swap rather than a rewrite (ADR 0003).
/// </summary>
public interface IInputSource
{
    /// <summary>Human-readable source, for debug overlays.</summary>
    string Describe();

    /// <summary>Sample input for this frame. Presses are edge-triggered.</summary>
    InputCommand Poll();
}
