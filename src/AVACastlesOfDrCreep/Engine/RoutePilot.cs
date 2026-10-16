using System.Globalization;

namespace AVACastlesOfDrCreep.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests): it follows the way out written at the end of a castle
/// file, one step at a time.
/// <code>
/// walk X      along the floor until the middle of square X (or to X exactly, if it has a decimal point)
/// up, down    onto the ladder (or down the pole) here, and on until standing on a floor again
/// fire        the button, once
/// door        up, through the door (or the matter transmitter) here
/// aim L ROW   from a ray gun's control, move gun L to a row
/// wait S      stand still for S seconds
/// watch X     stand still until a monster in the room reaches square X (or twenty seconds go by)
/// </code>
/// The castle tests drive each castle this way from the door in to the way out, which is how they know every
/// castle can be escaped.
/// </summary>
public sealed class RoutePilot
{
    private List<string> _steps = [];
    private int _step, _held;
    private bool _started;
    private double _waited;
    private (int Room, double X, double Y) _at;

    /// <summary>What it is doing now, for the probe and the tests.</summary>
    public string Doing => _step < _steps.Count ? _steps[_step] : "done";
    public bool Done => _step >= _steps.Count;
    /// <summary>A watch that gave up waiting: the monster never came (so the castle didn't go as written).</summary>
    public bool TimedOut { get; private set; }
    public int StepIndex => _step;

    public void Reset(Castle castle)
    {
        _steps = [.. castle.Solution];
        _step = 0;
        _held = 0;
        _started = false;
        _waited = 0;
        TimedOut = false;
    }

    public Controls Decide(GameEngine g)
    {
        if (g.Phase != Phase.Play || g.Players.Count == 0 || _step >= _steps.Count) return default;
        var p = g.Players[0];
        if (!p.Active) return default;
        var w = _steps[_step].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (w[0])
        {
            case "walk":
            {
                if (p.Pose != Pose.Walking) return default;
                double x = w[1].Contains('.') ? double.Parse(w[1], CultureInfo.InvariantCulture) : int.Parse(w[1]) + 0.5;
                double d = x - p.X;
                if (Math.Abs(d) < 0.09) return Next();
                return new Controls(Math.Sign(d), 0);
            }
            case "up":
            case "down":
            {
                int dy = w[0] == "up" ? -1 : 1;
                if (p.Pose != Pose.Walking) _started = true;
                else if (_started) return Next();
                return new Controls(0, dy);
            }
            case "fire":
                // Press, then let go.
                if (_held++ == 0) return new Controls(0, 0, true);
                return Next();
            case "door":
                if (_held++ == 0) { _at = (p.Room, p.X, p.Y); return new Controls(0, -1); }
                if (p.Room != _at.Room || p.Pose == Pose.Escaped || Math.Abs(p.X - _at.X) + Math.Abs(p.Y - _at.Y) > 1.5) return Next();
                // Not through yet: let go, and push again.
                if (_held % 2 == 0) return default;
                return _held > 40 ? Next() : new Controls(0, -1);
            case "aim":
            {
                var gun = g.Castle.Room(p.Room).Find(w[1][0]);
                double row = double.Parse(w[2], CultureInfo.InvariantCulture);
                if (gun == null || Math.Abs(gun.Aim - row) < 0.05) return Next();
                return new Controls(0, Math.Sign(row - gun.Aim));
            }
            case "watch":
            {
                double x = int.Parse(w[1]) + 0.5;
                _waited += Field.Step;
                if (_waited > 20) { TimedOut = true; return Next(); }
                if (g.Monsters.Any(m => !m.Dead && m.Room == p.Room && Math.Abs(m.X - x) < 0.5)) return Next();
                return default;
            }
            case "wait":
                _waited += Field.Step;
                if (_waited >= double.Parse(w[1], CultureInfo.InvariantCulture)) return Next();
                return default;
            default:
                return Next();
        }
    }

    private Controls Next()
    {
        _step++;
        _held = 0;
        _started = false;
        _waited = 0;
        return default;
    }
}
