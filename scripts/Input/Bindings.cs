namespace ClownSquad.Input;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>Everything a player can rebind.</summary>
public enum GameAction
{
    Up, Down, Left, Right,
    Attack, Jump, Dodge, Special,
    Pause, Stats,
    /// <summary>Practice ring: previous / next weapon, toggle the full skill tree, call in grunts.</summary>
    PrevWeapon, NextWeapon, PracticeSkills, PracticeGrunts,
}

/// <summary>
/// The control scheme, rebindable (Settings > CONTROLS). Every place that reads
/// a device asks here instead of naming keys and buttons. There are two tables:
/// one for the keyboard, one shared by every gamepad (each pad is still read by
/// its own device id; Godot's InputMap is global, which is why local co-op
/// polls devices directly, see LocalDeviceInput).
///
/// Each action has up to two keys and two buttons; rebinding replaces the
/// first and clears the second. A key or button already used by another
/// action is swapped onto it, so nothing ever ends up unbound by accident.
/// Saved in user://controls.cfg.
/// </summary>
public static class Bindings
{
    /// <summary>Where bindings are saved. Tests point this elsewhere before first use.</summary>
    public static string FilePath { get; set; } = "user://controls.cfg";

    /// <summary>Forget what is loaded, so the next read loads FilePath again (tests).</summary>
    public static void Reload() => _loaded = false;

    private static readonly Dictionary<GameAction, Key[]> DefaultKeys = new()
    {
        [GameAction.Up] = new[] { Key.W }, [GameAction.Down] = new[] { Key.S },
        [GameAction.Left] = new[] { Key.A }, [GameAction.Right] = new[] { Key.D },
        [GameAction.Attack] = new[] { Key.J }, [GameAction.Jump] = new[] { Key.Space },
        [GameAction.Dodge] = new[] { Key.K, Key.Shift }, [GameAction.Special] = new[] { Key.L },
        [GameAction.Pause] = new[] { Key.Escape }, [GameAction.Stats] = new[] { Key.Tab },
        [GameAction.PrevWeapon] = new[] { Key.Q }, [GameAction.NextWeapon] = new[] { Key.E },
        [GameAction.PracticeSkills] = new[] { Key.R }, [GameAction.PracticeGrunts] = new[] { Key.G },
    };

    private static readonly Dictionary<GameAction, JoyButton[]> DefaultButtons = new()
    {
        [GameAction.Up] = new[] { JoyButton.DpadUp }, [GameAction.Down] = new[] { JoyButton.DpadDown },
        [GameAction.Left] = new[] { JoyButton.DpadLeft }, [GameAction.Right] = new[] { JoyButton.DpadRight },
        [GameAction.Attack] = new[] { JoyButton.X }, [GameAction.Jump] = new[] { JoyButton.A },
        [GameAction.Dodge] = new[] { JoyButton.B }, [GameAction.Special] = new[] { JoyButton.Y },
        [GameAction.Pause] = new[] { JoyButton.Start }, [GameAction.Stats] = new[] { JoyButton.Back },
        [GameAction.PrevWeapon] = new[] { JoyButton.LeftShoulder }, [GameAction.NextWeapon] = new[] { JoyButton.RightShoulder },
        [GameAction.PracticeSkills] = new[] { JoyButton.LeftStick }, [GameAction.PracticeGrunts] = new[] { JoyButton.RightStick },
    };

    private static readonly Dictionary<GameAction, Key[]> Keys = new();
    private static readonly Dictionary<GameAction, JoyButton[]> Buttons = new();
    private static bool _loaded;

    /// <summary>Actions shown in the controls menu, in order, with their names.</summary>
    public static readonly (GameAction action, string name)[] Listed =
    {
        (GameAction.Up, "MOVE UP"), (GameAction.Down, "MOVE DOWN"), (GameAction.Left, "MOVE LEFT"), (GameAction.Right, "MOVE RIGHT"),
        (GameAction.Attack, "ATTACK"), (GameAction.Jump, "JUMP"), (GameAction.Dodge, "DODGE"), (GameAction.Special, "SPECIAL"),
        (GameAction.Pause, "PAUSE"), (GameAction.Stats, "STAT TENT"),
        (GameAction.PrevWeapon, "PRACTICE: PREV WEAPON"), (GameAction.NextWeapon, "PRACTICE: NEXT WEAPON"),
        (GameAction.PracticeSkills, "PRACTICE: ALL SKILLS"), (GameAction.PracticeGrunts, "PRACTICE: CALL GRUNTS"),
    };

    /// <summary>Whether the action is held on a device (<see cref="LocalDeviceInput.Keyboard"/> or a pad id).</summary>
    public static bool Held(int device, GameAction action)
    {
        EnsureLoaded();
        if (device == LocalDeviceInput.Keyboard)
        {
            foreach (Key k in Keys[action]) if (Godot.Input.IsKeyPressed(k)) return true;
            return false;
        }
        foreach (JoyButton b in Buttons[action]) if (Godot.Input.IsJoyButtonPressed(device, b)) return true;
        return false;
    }

    public static IReadOnlyList<Key> KeysFor(GameAction a) { EnsureLoaded(); return Keys[a]; }
    public static IReadOnlyList<JoyButton> ButtonsFor(GameAction a) { EnsureLoaded(); return Buttons[a]; }

    /// <summary>Bind a key, swapping it off whichever action had it.</summary>
    public static void BindKey(GameAction action, Key key)
    {
        EnsureLoaded();
        Key old = Keys[action].Length > 0 ? Keys[action][0] : Key.None;
        foreach (GameAction other in Enum.GetValues<GameAction>())
        {
            if (other == action || Array.IndexOf(Keys[other], key) < 0) continue;
            Keys[other] = Array.ConvertAll(Keys[other], k => k == key ? old : k);
            Keys[other] = Array.FindAll(Keys[other], k => k != Key.None);
        }
        Keys[action] = new[] { key };
        Save();
    }

    /// <summary>Bind a pad button, swapping it off whichever action had it.</summary>
    public static void BindButton(GameAction action, JoyButton button)
    {
        EnsureLoaded();
        JoyButton old = Buttons[action].Length > 0 ? Buttons[action][0] : JoyButton.Invalid;
        foreach (GameAction other in Enum.GetValues<GameAction>())
        {
            if (other == action || Array.IndexOf(Buttons[other], button) < 0) continue;
            Buttons[other] = Array.ConvertAll(Buttons[other], b => b == button ? old : b);
            Buttons[other] = Array.FindAll(Buttons[other], b => b != JoyButton.Invalid);
        }
        Buttons[action] = new[] { button };
        Save();
    }

    public static void ResetDefaults()
    {
        foreach (GameAction a in Enum.GetValues<GameAction>())
        {
            Keys[a] = (Key[])DefaultKeys[a].Clone();
            Buttons[a] = (JoyButton[])DefaultButtons[a].Clone();
        }
        _loaded = true;
        Save();
    }

    public static string KeyName(IReadOnlyList<Key> keys) =>
        keys.Count == 0 ? "-" : string.Join(" / ", Array.ConvertAll(System.Linq.Enumerable.ToArray(keys), k => OS.GetKeycodeString(k).ToUpperInvariant()));

    public static string ButtonName(IReadOnlyList<JoyButton> buttons) =>
        buttons.Count == 0 ? "-" : string.Join(" / ", Array.ConvertAll(System.Linq.Enumerable.ToArray(buttons), Pretty));

    private static string Pretty(JoyButton b) => b switch
    {
        JoyButton.A => "A", JoyButton.B => "B", JoyButton.X => "X", JoyButton.Y => "Y",
        JoyButton.LeftShoulder => "LB", JoyButton.RightShoulder => "RB",
        JoyButton.LeftStick => "L-STICK", JoyButton.RightStick => "R-STICK",
        JoyButton.Back => "BACK", JoyButton.Start => "START", JoyButton.Guide => "GUIDE",
        JoyButton.DpadUp => "D-UP", JoyButton.DpadDown => "D-DOWN", JoyButton.DpadLeft => "D-LEFT", JoyButton.DpadRight => "D-RIGHT",
        _ => b.ToString().ToUpperInvariant(),
    };

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (GameAction a in Enum.GetValues<GameAction>())
        {
            Keys[a] = (Key[])DefaultKeys[a].Clone();
            Buttons[a] = (JoyButton[])DefaultButtons[a].Clone();
        }
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok) return;
        foreach (GameAction a in Enum.GetValues<GameAction>())
        {
            string n = a.ToString();
            if (cfg.HasSectionKey("keys", n)) Keys[a] = Array.ConvertAll(cfg.GetValue("keys", n).AsInt64Array(), v => (Key)v);
            if (cfg.HasSectionKey("pad", n)) Buttons[a] = Array.ConvertAll(cfg.GetValue("pad", n).AsInt64Array(), v => (JoyButton)v);
        }
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        foreach (GameAction a in Enum.GetValues<GameAction>())
        {
            cfg.SetValue("keys", a.ToString(), Array.ConvertAll(Keys[a], k => (long)k));
            cfg.SetValue("pad", a.ToString(), Array.ConvertAll(Buttons[a], b => (long)b));
        }
        cfg.Save(FilePath);
    }
}
