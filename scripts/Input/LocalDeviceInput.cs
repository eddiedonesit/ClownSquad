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

    public LocalDeviceInput(int device) => _device = device;

    public string Describe() => _device == Keyboard ? "Keyboard" : $"Gamepad {_device}";

    public InputCommand Poll()
    {
        Vector2 move;
        bool attackHeld, dodgeHeld;

        if (_device == Keyboard)
        {
            move = new Vector2(
                (Godot.Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Godot.Input.IsKeyPressed(Key.A) ? 1f : 0f),
                (Godot.Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Godot.Input.IsKeyPressed(Key.W) ? 1f : 0f));
            attackHeld = Godot.Input.IsKeyPressed(Key.J) || Godot.Input.IsKeyPressed(Key.Space);
            dodgeHeld = Godot.Input.IsKeyPressed(Key.K) || Godot.Input.IsKeyPressed(Key.Shift);
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
            attackHeld = Godot.Input.IsJoyButtonPressed(_device, JoyButton.X)
                      || Godot.Input.IsJoyButtonPressed(_device, JoyButton.A);
            dodgeHeld = Godot.Input.IsJoyButtonPressed(_device, JoyButton.B)
                     || Godot.Input.IsJoyButtonPressed(_device, JoyButton.RightShoulder);
        }

        bool attackPressed = attackHeld && !_attackHeldLast;
        bool dodgePressed = dodgeHeld && !_dodgeHeldLast;
        _attackHeldLast = attackHeld;
        _dodgeHeldLast = dodgeHeld;

        return new InputCommand(move, attackPressed, dodgePressed);
    }
}
