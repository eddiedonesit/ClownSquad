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
            into[(int)Press.Up] = K(Key.W) || K(Key.Up);
            into[(int)Press.Down] = K(Key.S) || K(Key.Down);
            into[(int)Press.Left] = K(Key.A) || K(Key.Left);
            into[(int)Press.Right] = K(Key.D) || K(Key.Right);
            into[(int)Press.Enter] = K(Key.J) || K(Key.Space) || K(Key.Enter);
            into[(int)Press.World] = K(Key.Q) || K(Key.E);
            into[(int)Press.Back] = K(Key.Escape) || K(Key.Backspace) || K(Key.K);
            return;
        }
        float x = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftX), y = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftY);
        into[(int)Press.Up] = y < -0.6f || B(JoyButton.DpadUp);
        into[(int)Press.Down] = y > 0.6f || B(JoyButton.DpadDown);
        into[(int)Press.Left] = x < -0.6f || B(JoyButton.DpadLeft);
        into[(int)Press.Right] = x > 0.6f || B(JoyButton.DpadRight);
        into[(int)Press.Enter] = B(JoyButton.A) || B(JoyButton.X);
        into[(int)Press.World] = B(JoyButton.LeftShoulder) || B(JoyButton.RightShoulder);
        into[(int)Press.Back] = B(JoyButton.B);
    }

    private static bool K(Key k) => Godot.Input.IsKeyPressed(k);
    private bool B(JoyButton b) => Godot.Input.IsJoyButtonPressed(_device, b);
}
