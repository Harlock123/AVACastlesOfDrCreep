using Avalonia.Input;

namespace AVACastlesOfDrCreep.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: one or two players, each with left and right, up and down (up the screen is -1), and a button.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);
    private readonly ControllerManager _pad2 = new(1);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
        _pad2.Update();
    }

    /// <summary>
    /// Player one. In a two-player game: the arrows, with right Ctrl, right Shift or Enter for the button. In a
    /// one-player game: the arrows or W A S D, with Space, Ctrl, Shift, Z or X.
    /// </summary>
    public Controls Player1 => TwoPlayers
        ? Read(Key.Left, Key.Right, Key.Up, Key.Down, [Key.RightCtrl, Key.RightShift, Key.Enter, Key.NumPad0], _pad)
        : Read(Key.Left, Key.Right, Key.Up, Key.Down, [Key.Space, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.Z, Key.X], _pad, also: true);

    /// <summary>Player two (a two-player game only): W A S D, with Space, left Ctrl or left Shift; or the second gamepad.</summary>
    public Controls Player2 => TwoPlayers ? Read(Key.A, Key.D, Key.W, Key.S, [Key.Space, Key.LeftCtrl, Key.LeftShift, Key.Q, Key.E], _pad2) : default;

    /// <summary>Set by the screen while a two-player game is on, so each player gets their own keys.</summary>
    public bool TwoPlayers { get; set; }

    private Controls Read(Key left, Key right, Key up, Key down, Key[] fire, ControllerManager pad, bool also = false)
    {
        int dx = (Any(right) || also && Any(Key.D, Key.NumPad6) ? 1 : 0) - (Any(left) || also && Any(Key.A, Key.NumPad4) ? 1 : 0);
        if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
        // Down the screen is +1.
        int dy = (Any(down) || also && Any(Key.S, Key.NumPad2) ? 1 : 0) - (Any(up) || also && Any(Key.W, Key.NumPad8) ? 1 : 0);
        if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
        bool button = Any(fire) || pad.ButtonA || pad.ButtonB || pad.RightTrigger > 0.5f;
        return new Controls(dx, dy, button);
    }

    /// <summary>One player: Enter or 1. Two players: 2.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;
    public bool Start2 => Any(Key.D2, Key.NumPad2) || _pad2.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose()
    {
        _pad.Dispose();
        _pad2.Dispose();
    }
}
