namespace ClownSquad.Net;

using Godot;
using ClownSquad.Input;

/// <summary>
/// A guest's controller, on the host: what the guest last sent, read by the
/// guest's fighter like any local pad (ADR 0003, 0010).
///
/// Packets are unreliable, so button presses travel as running counts, one
/// byte each, rather than as "pressed this frame" flags: a press is a count
/// that moved since the last poll. A dropped packet delays a press to the
/// next one instead of losing it, and a repeated one cannot press twice.
/// </summary>
public sealed class RemoteInput : IInputSource
{
    public long Peer { get; }
    private Vector2 _move;
    private bool _attackHeld;
    private int _counts;
    private int _seen;
    private bool _primed;

    /// <summary>Presses received, for tests.</summary>
    public int Presses { get; private set; }

    public RemoteInput(long peer) => Peer = peer;

    public string Describe() => $"Online (peer {Peer})";

    /// <summary>The latest packet from the guest.</summary>
    public void Receive(Vector2 move, bool attackHeld, int counts)
    {
        _move = move.LengthSquared() > 1f ? move.Normalized() : move;
        _attackHeld = attackHeld;
        _counts = counts;
        if (!_primed) { _seen = counts; _primed = true; }
    }

    public InputCommand Poll()
    {
        bool Moved(int shift) => ((_counts >> shift) & 0xff) != ((_seen >> shift) & 0xff);
        bool attack = Moved(0), dodge = Moved(8), jump = Moved(16), special = Moved(24);
        if (attack || dodge || jump || special) Presses++;
        _seen = _counts;
        return new InputCommand(_move, attack, dodge, jump, special, _attackHeld);
    }

    /// <summary>Pack four press counts into the int a guest sends.</summary>
    public static int Pack(int attack, int dodge, int jump, int special) =>
        (attack & 0xff) | (dodge & 0xff) << 8 | (jump & 0xff) << 16 | (special & 0xff) << 24;
}
