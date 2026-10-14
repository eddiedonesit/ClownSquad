namespace ClownSquad.Input;

using Godot;

/// <summary>
/// The only thing gameplay reads. Deliberately device-agnostic: a command may
/// come from a local gamepad, a remote peer, or AI, and nothing downstream can
/// tell the difference. See ADR 0003.
/// </summary>
public readonly struct InputCommand
{
    /// <summary>Stick/keys as a direction on the ground plane, length clamped to 1.</summary>
    public readonly Vector2 Move;
    public readonly bool AttackPressed;
    public readonly bool DodgePressed;
    public readonly bool JumpPressed;
    public readonly bool SpecialPressed;
    /// <summary>Attack is currently down. Charge moves are driven by this, not the press.</summary>
    public readonly bool AttackHeld;

    public InputCommand(Vector2 move, bool attack, bool dodge, bool jump = false,
                        bool special = false, bool attackHeld = false)
    {
        Move = move.LengthSquared() > 1f ? move.Normalized() : move;
        AttackPressed = attack;
        DodgePressed = dodge;
        JumpPressed = jump;
        SpecialPressed = special;
        AttackHeld = attackHeld || attack;
    }

    public static readonly InputCommand None = new(Vector2.Zero, false, false);
}
