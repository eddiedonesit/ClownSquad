namespace ClownSquad.Map;

using Godot;
using ClownSquad.Input;

/// <summary>Map controls from one device, edge-detected: four directions, enter, and change world.</summary>
public sealed class MapPad
{
    public enum Press { None, Up, Down, Left, Right, Enter, World, Back }

    private readonly int _device;
    private readonly bool[] _held = new bool[8];

    public MapPad(int device)
    {
        _device = device;
        Read(_held);
    }

    /// <summary>Treat everything currently held as already handled (after a menu closes).</summary>
    public void Swallow() => Read(_held);

    public Press Poll()
    {
        var now = new bool[8];
        Read(now);
        Press result = Press.None;
        for (int i = 1; i < now.Length; i++)
            if (now[i] && !_held[i] && result == Press.None) result = (Press)i;
        System.Array.Copy(now, _held, now.Length);
        return result;
    }

    private void Read(bool[] into)
    {
        if (_device == LocalDeviceInput.Keyboard)
        {
            // Fixed keys (arrows, Enter, Esc, Backspace) always work; the rebound ones work too.
            into[(int)Press.Up] = K(Key.Up) || H(GameAction.Up);
            into[(int)Press.Down] = K(Key.Down) || H(GameAction.Down);
            into[(int)Press.Left] = K(Key.Left) || H(GameAction.Left);
            into[(int)Press.Right] = K(Key.Right) || H(GameAction.Right);
            into[(int)Press.Enter] = K(Key.Enter) || H(GameAction.Attack) || H(GameAction.Jump);
            into[(int)Press.World] = H(GameAction.PrevWeapon) || H(GameAction.NextWeapon);
            into[(int)Press.Back] = K(Key.Escape) || K(Key.Backspace) || H(GameAction.Dodge);
            return;
        }
        float x = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftX), y = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftY);
        into[(int)Press.Up] = y < -0.6f || B(JoyButton.DpadUp);
        into[(int)Press.Down] = y > 0.6f || B(JoyButton.DpadDown);
        into[(int)Press.Left] = x < -0.6f || B(JoyButton.DpadLeft);
        into[(int)Press.Right] = x > 0.6f || B(JoyButton.DpadRight);
        into[(int)Press.Enter] = H(GameAction.Attack) || H(GameAction.Jump);
        into[(int)Press.World] = B(JoyButton.LeftShoulder) || B(JoyButton.RightShoulder);
        into[(int)Press.Back] = H(GameAction.Dodge);
    }

    private static bool K(Key k) => Godot.Input.IsKeyPressed(k);
    private bool B(JoyButton b) => Godot.Input.IsJoyButtonPressed(_device, b);
    private bool H(GameAction a) => Bindings.Held(_device, a);
}
