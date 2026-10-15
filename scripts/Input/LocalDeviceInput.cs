namespace ClownSquad.Input;

using Godot;

/// <summary>
/// Polls one physical device directly rather than through Godot's InputMap.
/// InputMap actions are global, so two gamepads would both drive every player.
/// Polling per device id is what makes local co-op work.
/// </summary>
public sealed class LocalDeviceInput : IInputSource
{
    /// <summary>Device id, or <see cref="Keyboard"/> for keyboard+mouse.</summary>
    public const int Keyboard = -1;

    private const float StickDeadzone = 0.25f;

    private readonly int _device;
    private bool _attackHeldLast;
    private bool _dodgeHeldLast;
    private bool _jumpHeldLast;
    private bool _specialHeldLast;

    public LocalDeviceInput(int device) => _device = device;

    /// <summary>The device this reads, so menus can poll the same one.</summary>
    public int Device => _device;

    public string Describe() => _device == Keyboard ? "Keyboard" : $"Gamepad {_device}";

    private bool Held(GameAction a) => Bindings.Held(_device, a);

    public InputCommand Poll()
    {
        Vector2 move;
        bool attackHeld, dodgeHeld, jumpHeld, specialHeld;

        if (_device == Keyboard)
        {
            move = new Vector2(
                (Held(GameAction.Right) ? 1f : 0f) - (Held(GameAction.Left) ? 1f : 0f),
                (Held(GameAction.Down) ? 1f : 0f) - (Held(GameAction.Up) ? 1f : 0f));
        }
        else
        {
            move = new Vector2(
                Godot.Input.GetJoyAxis(_device, JoyAxis.LeftX),
                Godot.Input.GetJoyAxis(_device, JoyAxis.LeftY));
            if (move.Length() < StickDeadzone)
            {
                move = Vector2.Zero;
            }
            else
            {
                // Rescale past the deadzone so slow walking stays available.
                move = move.Normalized() * ((move.Length() - StickDeadzone) / (1f - StickDeadzone));
            }
            // The d-pad (or whatever movement is bound to) works alongside the stick.
            Vector2 pad = new((Held(GameAction.Right) ? 1f : 0f) - (Held(GameAction.Left) ? 1f : 0f),
                              (Held(GameAction.Down) ? 1f : 0f) - (Held(GameAction.Up) ? 1f : 0f));
            if (pad != Vector2.Zero) move = pad;
        }
        // Buttons come from the rebindable table (Settings > CONTROLS).
        attackHeld = Held(GameAction.Attack);
        dodgeHeld = Held(GameAction.Dodge);
        jumpHeld = Held(GameAction.Jump);
        specialHeld = Held(GameAction.Special);

        bool attackPressed = attackHeld && !_attackHeldLast;
        bool dodgePressed = dodgeHeld && !_dodgeHeldLast;
        bool jumpPressed = jumpHeld && !_jumpHeldLast;
        bool specialPressed = specialHeld && !_specialHeldLast;
        _attackHeldLast = attackHeld;
        _dodgeHeldLast = dodgeHeld;
        _jumpHeldLast = jumpHeld;
        _specialHeldLast = specialHeld;

        return new InputCommand(move, attackPressed, dodgePressed, jumpPressed, specialPressed, attackHeld);
    }
}
