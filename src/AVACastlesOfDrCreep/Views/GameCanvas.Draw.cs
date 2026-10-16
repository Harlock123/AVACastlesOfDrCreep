using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVACastlesOfDrCreep.Engine;
using Effect = AVACastlesOfDrCreep.Engine.Effect;

namespace AVACastlesOfDrCreep.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVACastlesOfDrCreep/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Stone = 0xFF3A3448, Moss = 0xFF6AA05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;

    public static uint KeyColour(string colour) => colour switch
    {
        "red" => 0xFFE8382A, "blue" => 0xFF3A8AE8, "green" => 0xFF3AC05A, "yellow" => 0xFFF0C820, "white" => 0xFFF2F2F2, _ => 0xFFB070E0,
    };

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0810), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            // One room, or (two players apart) both, side by side at half size.
            var rooms = g.Players.Where(p => p.Pose is not (Pose.Out or Pose.Escaped)).Select(p => p.Room).Distinct().ToList();
            if (rooms.Count == 0) rooms = [g.Players.Count > 0 ? g.Players[0].Room : g.Castle.StartRoom];
            if (rooms.Count == 1) DrawRoom(dc, g.Castle.Room(rooms[0]), Field.Left, Field.Top, 1);
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    double ox = 24 + i * 500, oy = Field.Top + 150;
                    Text(dc, g.Castle.Room(rooms[i]).Name.ToUpperInvariant(), new Point(ox + 240, oy - 22), 22, 0xFFD8C8F0, center: true);
                    DrawRoom(dc, g.Castle.Room(rooms[i]), ox, oy, 0.5);
                }
            }
            if (g.ShowMap) DrawMap(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── a room ─────────────────────────────

    /// <summary>A room drawn at a place and scale: its walls, floors, ladders and poles, everything in it, the monsters and the players.</summary>
    private void DrawRoom(DrawingContext dc, Room room, double ox, double oy, double k)
    {
        using var place = dc.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy));
        using var clip = dc.PushClip(new Rect(0, 0, Field.Cols * T, Field.Rows * T));
        DrawWall(dc, room.Id);
        // Ladders and poles behind the floors.
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            if (room.Ladder(c, r)) DrawLadder(dc, c, r);
            if (room.Pole(c, r)) dc.DrawLine(Pen(0xFFD8B050, 4), new Point(c * T + T / 2, r * T), new Point(c * T + T / 2, r * T + T));
        }
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            char ch = room.At(c, r);
            if (ch is '=' or '#' or '!') DrawSlab(dc, c, r, ch == '#' || ch == '!');
        }
        foreach (var t in room.Traps) DrawTrap(dc, t);
        foreach (var w in room.Walkways) DrawWalkway(dc, w);
        foreach (var t in room.Things) DrawThing(dc, room, t);
        foreach (var m in Engine.Monsters.Where(m => m.Room == room.Id && !m.Dead)) DrawMonster(dc, m);
        foreach (var p in Engine.Players.Where(p => p.Room == room.Id && p.Pose is not (Pose.Out or Pose.Escaped))) DrawPlayer(dc, p);
        foreach (var t in room.Things.Where(t => t.Kind == Kind.Ray && t.Beam > 0)) DrawBeam(dc, t);
        foreach (var e in Engine.Effects.Where(e => e.Room == room.Id)) DrawEffect(dc, e);
        dc.DrawRectangle(null, Pen(0xFF5A4A7A, 3), new Rect(0, 0, Field.Cols * T, Field.Rows * T));
    }

    /// <summary>The back wall: dark stone blocks, one or two torches.</summary>
    private void DrawWall(DrawingContext dc, int salt)
    {
        dc.FillRectangle(Brush(0xFF241E30), new Rect(0, 0, Field.Cols * T, Field.Rows * T));
        for (int r = 0; r < Field.Rows * 2; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            uint q = Hash(c, r, salt);
            double x = c * T + (r % 2) * T / 2, y = r * T / 2;
            dc.FillRectangle(Brush(q % 4 == 0 ? 0xFF2E2840u : q % 4 == 1 ? 0xFF2A2438u : Stone), new Rect(x + 1, y + 1, T - 2, T / 2 - 2), 2);
        }
        for (int i = 0; i < 2; i++)
        {
            double tx = (i == 0 ? 3 : 20) * T, ty = 2.5 * T;
            dc.FillRectangle(Brush(0xFF5A3A1E), new Rect(tx - 3, ty, 6, 20));
            double f = Math.Sin(_clock * 13 + i * 2) * 2;
            dc.DrawEllipse(Brush(0x40FFB040), null, new Point(tx, ty - 6), 26, 26);
            Poly(dc, 0xFFFF9A2A, null, 0, new Point(tx - 6, ty), new Point(tx + f, ty - 18), new Point(tx + 6, ty));
            Poly(dc, 0xFFFFE07A, null, 0, new Point(tx - 3, ty), new Point(tx + f * 0.5, ty - 10), new Point(tx + 3, ty));
        }
    }

    private static void DrawSlab(DrawingContext dc, int c, int r, bool hole)
    {
        double x = c * T, y = r * T;
        dc.FillRectangle(Brush(0xFF8A8098), new Rect(x, y, T, 9));
        dc.FillRectangle(Brush(0xFF5A5068), new Rect(x, y + 9, T, 4));
        dc.DrawLine(Pen(0xFF4A4058, 1), new Point(x, y), new Point(x, y + 13));
        if (hole) dc.FillRectangle(Brush(0xFF1A1622), new Rect(x + T / 2 - 9, y + 1, 18, 11));
    }

    private static void DrawLadder(DrawingContext dc, int c, int r)
    {
        double x = c * T, y = r * T;
        dc.DrawLine(Pen(0xFF9A6A3A, 3), new Point(x + 9, y), new Point(x + 9, y + T));
        dc.DrawLine(Pen(0xFF9A6A3A, 3), new Point(x + T - 9, y), new Point(x + T - 9, y + T));
        for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFFB88A4A, 3), new Point(x + 9, y + 7 + i * 13), new Point(x + T - 9, y + 7 + i * 13));
    }

    private void DrawTrap(DrawingContext dc, Trapdoor t)
    {
        double x = t.X * T, y = t.Y * T, w = t.Width * T;
        if (!t.Open)
        {
            dc.FillRectangle(Brush(0xFF7A5030), new Rect(x, y, w, 11));
            for (int i = 0; i <= t.Width * 3; i++) dc.DrawLine(Pen(0xFF4A3018, 1.5), new Point(x + i * T / 3, y), new Point(x + i * T / 3, y + 11));
            dc.DrawRectangle(null, Pen(0xFF2A1A0A, 1.5), new Rect(x, y, w, 11));
        }
        else
        {
            dc.FillRectangle(Brush(0xFF0A0810), new Rect(x, y, w, 13));
            dc.FillRectangle(Brush(0xFF7A5030), new Rect(x - 2, y, 9, T * 0.9));
            dc.FillRectangle(Brush(0xFF7A5030), new Rect(x + w - 7, y, 9, T * 0.9));
        }
    }

    private void DrawWalkway(DrawingContext dc, Walkway w)
    {
        double x = w.X * T, y = w.Y * T, len = w.Width * T;
        dc.FillRectangle(Brush(0xFF3A3A42), new Rect(x, y, len, 12), 4);
        double shift = (_clock * GameEngine.CarrySpeed * T * w.Dir) % 20;
        using (dc.PushClip(new Rect(x + 3, y, len - 6, 12)))
            for (double s = -20; s < len + 20; s += 20)
            {
                double cx = x + s + shift;
                uint col = w.Dir == 0 ? 0xFF6A6A72 : 0xFFE8C040;
                if (w.Dir >= 0) Poly(dc, col, null, 0, new Point(cx, y + 2), new Point(cx + 6, y + 6), new Point(cx, y + 10), new Point(cx + 3, y + 6));
                else Poly(dc, col, null, 0, new Point(cx + 6, y + 2), new Point(cx, y + 6), new Point(cx + 6, y + 10), new Point(cx + 3, y + 6));
            }
        dc.DrawEllipse(Brush(0xFF6A6A72), Pen(0xFF2A2A30, 1), new Point(x + 6, y + 6), 6, 6);
        dc.DrawEllipse(Brush(0xFF6A6A72), Pen(0xFF2A2A30, 1), new Point(x + len - 6, y + 6), 6, 6);
    }

    // ───────────────────────────── things ─────────────────────────────

    private void DrawThing(DrawingContext dc, Room room, Thing t)
    {
        double x = t.X * T, feet = t.Feet * T, mx = x + T / 2;
        switch (t.Kind)
        {
            case Kind.Door:
            case Kind.Exit:
                DrawDoor(dc, mx, feet, t.Open, t.Kind == Kind.Exit);
                break;
            case Kind.Bell:
                Plate(dc, mx, feet - 44, 0xFF8A7050);
                dc.DrawEllipse(Brush(0xFFF0C040), Pen(0xFF6A4A10, 1.5), new Point(mx, feet - 44), 7, 7);
                if (room.Find(t.Arg(0)[0]) is { } door) dc.DrawEllipse(Brush(door.Open ? 0xFF6AE06Au : 0xFFE04A3Au), null, new Point(mx, feet - 58), 3, 3);
                break;
            case Kind.Key:
                if (!t.Used) DrawKey(dc, mx, feet - 22 + Math.Sin(_clock * 3 + t.X) * 3, KeyColour(t.Arg(0)), 1);
                break;
            case Kind.Lock:
                Plate(dc, mx, feet - 44, KeyColour(t.Arg(0)));
                dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(mx, feet - 48), 4, 4);
                Poly(dc, 0xFF1A1A1A, null, 0, new Point(mx - 2, feet - 47), new Point(mx + 2, feet - 47), new Point(mx + 3, feet - 38), new Point(mx - 3, feet - 38));
                break;
            case Kind.TrapSwitch:
            case Kind.BoltSwitch:
            case Kind.WalkControl:
            case Kind.FieldButton:
            case Kind.RayBox:
                DrawControl(dc, room, t, mx, feet);
                break;
            case Kind.Field:
            {
                int h = int.Parse(t.Arg(0, "3"));
                double top = (t.Y - h + 1) * T;
                dc.FillRectangle(Brush(0xFF5A5A6A), new Rect(mx - 9, top - 6, 18, 8), 2);
                dc.FillRectangle(Brush(0xFF5A5A6A), new Rect(mx - 9, feet - 4, 18, 6), 2);
                if (t.On)
                    for (int i = 0; i < 3; i++)
                    {
                        double sx = mx - 6 + i * 6 + Math.Sin(_clock * 20 + i) * 1.5;
                        dc.DrawLine(Pen(Alpha(0xFF7AE0FF, 0.5 + 0.3 * Math.Sin(_clock * 15 + i * 2)), 2.5), new Point(sx, top + 2), new Point(sx, feet - 4));
                    }
                else if (t.Timer > 0) Text(dc, $"{Math.Ceiling(t.Timer):0}", new Point(mx, top - 16), 18, 0xFF7AE0FF, center: true);
                break;
            }
            case Kind.Bolt:
            {
                int h = int.Parse(t.Arg(0, "3"));
                double top = (t.Y - h + 1) * T;
                dc.DrawEllipse(Brush(0xFF8A8AA0), Pen(0xFF3A3A4A, 2), new Point(mx, top), 9, 9);
                dc.DrawEllipse(Brush(0xFF8A8AA0), Pen(0xFF3A3A4A, 2), new Point(mx, feet - 6), 9, 7);
                if (t.On)
                {
                    var pts = new List<Point> { new(mx, top + 8) };
                    int n = 8;
                    for (int i = 1; i < n; i++)
                        pts.Add(new Point(mx + (Hash(i, (int)(_clock * 20), t.X) % 15) - 7, top + 8 + (feet - 14 - top) * i / n));
                    pts.Add(new Point(mx, feet - 12));
                    for (int i = 1; i < pts.Count; i++)
                    {
                        dc.DrawLine(Pen(0x60A0C0FF, 6), pts[i - 1], pts[i]);
                        dc.DrawLine(Pen(0xFFF2F6FF, 2), pts[i - 1], pts[i]);
                    }
                }
                break;
            }
            case Kind.Ray:
            {
                double top = int.Parse(t.Arg(0)) * T;
                dc.DrawLine(Pen(0xFF6A6A7A, 4), new Point(mx, top), new Point(mx, t.Y * T + T));
                double gy = t.Aim * T + T / 2;
                dc.FillRectangle(Brush(0xFF8A9AB0), new Rect(mx - 12, gy - 10, 24, 20), 4);
                dc.FillRectangle(Brush(0xFF4A5A70), new Rect(t.Facing > 0 ? mx + 10 : mx - 24, gy - 4, 14, 8));
                dc.DrawEllipse(Brush(t.Cooldown <= 0 ? 0xFFFF4A3Au : 0xFF6A2A2A), null, new Point(mx, gy), 4, 4);
                break;
            }
            case Kind.Booth:
            {
                var rx = GameEngine.Receivers(room, t).ElementAtOrDefault(t.Selected);
                uint col = rx == null ? 0xFF8A8A8A : KeyColour(rx.Arg(1));
                dc.FillRectangle(Brush(0xFF3A3A4A), new Rect(mx - 22, feet - 78, 44, 78), 6);
                dc.FillRectangle(Brush(Alpha(col, 0.35 + 0.15 * Math.Sin(_clock * 6))), new Rect(mx - 17, feet - 72, 34, 66), 4);
                dc.DrawRectangle(null, Pen(0xFF8A8AA0, 2), new Rect(mx - 22, feet - 78, 44, 78), 6);
                dc.DrawEllipse(Brush(col), null, new Point(mx, feet - 84), 6, 6);
                break;
            }
            case Kind.Receiver:
            {
                uint col = KeyColour(t.Arg(1));
                dc.DrawEllipse(Brush(Alpha(col, 0.3)), Pen(col, 2.5), new Point(mx, feet - 4), 20, 6);
                dc.DrawEllipse(Brush(Alpha(col, 0.12 + 0.08 * Math.Sin(_clock * 5))), null, new Point(mx, feet - 34), 16, 32);
                break;
            }
            case Kind.Tomb:
                DrawTomb(dc, mx, feet, t.Used);
                break;
            case Kind.Ankh:
                if (room.Find(t.Arg(0)[0]) is { Used: false }) DrawAnkh(dc, mx, feet - 24, 1);
                break;
            case Kind.Coffin:
                DrawCoffin(dc, mx, feet, t.Used);
                break;
        }
    }

    private static void Plate(DrawingContext dc, double x, double y, uint rim)
    {
        dc.FillRectangle(Brush(0xFF4A4458), new Rect(x - 11, y - 14, 22, 28), 4);
        dc.DrawRectangle(null, Pen(rim, 2.5), new Rect(x - 11, y - 14, 22, 28), 4);
    }

    /// <summary>A door in an arch: shut, planks and iron; open, darkness (or, the way out, daylight).</summary>
    private void DrawDoor(DrawingContext dc, double mx, double feet, bool open, bool exit)
    {
        double w = 46, h = 74, x = mx - w / 2, y = feet - h;
        var arch = new StreamGeometry();
        using (var c = arch.Open())
        {
            c.BeginFigure(new Point(x, feet), true);
            c.LineTo(new Point(x, y + w / 2));
            c.ArcTo(new Point(x + w, y + w / 2), new Size(w / 2, w / 2), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(x + w, feet));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(0xFF5A5068), Pen(0xFF8A8098, 5), arch);
        if (open)
        {
            dc.DrawGeometry(Brush(exit ? 0xFF8AC8F0u : 0xFF0A0810u), null, arch);
            if (exit)
            {
                dc.FillRectangle(Brush(0xFF5AA04A), new Rect(x + 3, feet - 16, w - 6, 14));
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(mx + 8, y + 30), 6, 6);
            }
        }
        else
        {
            dc.DrawGeometry(Brush(0xFF7A4A28), null, arch);
            for (int i = 1; i < 4; i++) dc.DrawLine(Pen(0xFF4A2A14, 2), new Point(x + i * w / 4, y + 6), new Point(x + i * w / 4, feet));
            dc.FillRectangle(Brush(0xFF2A2A30), new Rect(x + 2, y + 34, w - 4, 4));
            dc.FillRectangle(Brush(0xFF2A2A30), new Rect(x + 2, feet - 18, w - 4, 4));
            dc.DrawEllipse(Brush(0xFFD0A040), null, new Point(x + w - 9, feet - 34), 3.5, 3.5);
        }
        if (exit) Text(dc, "EXIT", new Point(mx, y - 12), 20, open ? 0xFF8AF08A : 0xFFE0C060, center: true);
    }

    private static void DrawKey(DrawingContext dc, double x, double y, uint colour, double k)
    {
        dc.DrawEllipse(null, Pen(colour, 4 * k), new Point(x - 7 * k, y), 6 * k, 6 * k);
        dc.DrawLine(Pen(colour, 4 * k), new Point(x - 1 * k, y), new Point(x + 13 * k, y));
        dc.DrawLine(Pen(colour, 3.5 * k), new Point(x + 9 * k, y), new Point(x + 9 * k, y + 6 * k));
        dc.DrawLine(Pen(colour, 3.5 * k), new Point(x + 13 * k, y), new Point(x + 13 * k, y + 5 * k));
    }

    /// <summary>A control box on the wall: a lever for switches, a button for the force field, a joystick for the ray gun.</summary>
    private void DrawControl(DrawingContext dc, Room room, Thing t, double mx, double feet)
    {
        double y = feet - 46;
        dc.FillRectangle(Brush(0xFF5A5A6A), new Rect(mx - 13, y - 16, 26, 32), 4);
        dc.DrawRectangle(null, Pen(0xFF2A2A34, 1.5), new Rect(mx - 13, y - 16, 26, 32), 4);
        switch (t.Kind)
        {
            case Kind.TrapSwitch:
            {
                bool open = room.Traps.ElementAtOrDefault(int.Parse(t.Arg(0)) - 1) is { Open: true };
                Lever(dc, mx, y, open, 0xFFC08040);
                break;
            }
            case Kind.BoltSwitch:
            {
                bool on = room.Find(t.Arg(0)[0]) is { On: true };
                Lever(dc, mx, y, on, 0xFF7AE0FF);
                break;
            }
            case Kind.WalkControl:
            {
                var w = room.Walkways.ElementAtOrDefault(int.Parse(t.Arg(0)) - 1);
                int dir = w?.Dir ?? 0;
                if (dir == 0) dc.FillRectangle(Brush(0xFFE04A3A), new Rect(mx - 6, y - 6, 12, 12), 2);
                else Poly(dc, 0xFFE8C040, null, 0, new Point(mx - 8 * dir, y - 8), new Point(mx + 8 * dir, y), new Point(mx - 8 * dir, y + 8));
                break;
            }
            case Kind.FieldButton:
                dc.DrawEllipse(Brush(0xFF7AE0FF), Pen(0xFF2A5A7A, 1.5), new Point(mx, y), 8, 8);
                break;
            case Kind.RayBox:
                dc.DrawLine(Pen(0xFF2A2A2A, 4), new Point(mx, y + 8), new Point(mx, y - 8));
                dc.DrawEllipse(Brush(0xFFE04A3A), null, new Point(mx, y - 9), 5, 5);
                break;
        }
    }

    private static void Lever(DrawingContext dc, double x, double y, bool thrown, uint lit)
    {
        dc.DrawLine(Pen(0xFF2A2A2A, 4), new Point(x, y + 6), new Point(x + (thrown ? 9 : -9), y - 9));
        dc.DrawEllipse(Brush(thrown ? lit : 0xFF8A8A8A), null, new Point(x + (thrown ? 9 : -9), y - 9), 4.5, 4.5);
        dc.DrawEllipse(Brush(thrown ? lit : 0xFF3A3A3A), null, new Point(x, y + 10), 3, 3);
    }

    private static void DrawTomb(DrawingContext dc, double x, double feet, bool open)
    {
        dc.FillRectangle(Brush(0xFFB0965A), new Rect(x - 26, feet - 30, 52, 30), 4);
        dc.DrawRectangle(null, Pen(0xFF6A5A30, 2), new Rect(x - 26, feet - 30, 52, 30), 4);
        for (int i = 0; i < 3; i++) dc.FillRectangle(Brush(0xFF3A6AA0), new Rect(x - 20 + i * 15, feet - 22, 8, 14), 2);
        if (open) Poly(dc, 0xFFC8AE70, 0xFF6A5A30, 1.5, new Point(x - 30, feet - 30), new Point(x + 20, feet - 52), new Point(x + 26, feet - 44), new Point(x - 24, feet - 24));
        else dc.FillRectangle(Brush(0xFFC8AE70), new Rect(x - 28, feet - 36, 56, 8), 3);
    }

    private static void DrawAnkh(DrawingContext dc, double x, double y, double k)
    {
        dc.DrawEllipse(null, Pen(0xFFF0C030, 4 * k), new Point(x, y - 8 * k), 5 * k, 7 * k);
        dc.DrawLine(Pen(0xFFF0C030, 4 * k), new Point(x - 9 * k, y), new Point(x + 9 * k, y));
        dc.DrawLine(Pen(0xFFF0C030, 4 * k), new Point(x, y), new Point(x, y + 16 * k));
    }

    private static void DrawCoffin(DrawingContext dc, double x, double feet, bool open)
    {
        var shape = new[] { new Point(x - 10, feet), new Point(x - 18, feet - 54), new Point(x - 10, feet - 76), new Point(x + 10, feet - 76), new Point(x + 18, feet - 54), new Point(x + 10, feet) };
        Poly(dc, open ? 0xFF1A1018 : 0xFF6A3A2A, 0xFF3A1A10, 2.5, shape);
        if (!open)
        {
            dc.DrawLine(Pen(0xFFD0A040, 3), new Point(x, feet - 64), new Point(x, feet - 36));
            dc.DrawLine(Pen(0xFFD0A040, 3), new Point(x - 8, feet - 56), new Point(x + 8, feet - 56));
        }
    }

    private void DrawBeam(DrawingContext dc, Thing gun)
    {
        double y = gun.Aim * T + T / 2, x0 = gun.Middle * T + gun.Facing * 24, x1 = gun.Facing > 0 ? Field.Cols * T : 0;
        double a = gun.Beam / 0.25;
        dc.DrawLine(Pen(Alpha(0xFFFF4A6A, 0.4 * a), 10), new Point(x0, y), new Point(x1, y));
        dc.DrawLine(Pen(Alpha(0xFFFFE0F0, a), 3), new Point(x0, y), new Point(x1, y));
    }

    // ───────────────────────────── people and monsters ─────────────────────────────

    private void DrawPlayer(DrawingContext dc, Player p)
    {
        if (p.Pose == Pose.Dead) return;
        if (p.Safe > 0 && (int)(_clock * 12) % 2 == 0) return;
        double x = p.X * T, feet = p.Y * T;
        uint shirt = p.Index == 0 ? 0xFFF2F2F2 : 0xFFF0D040, legs = p.Index == 0 ? 0xFF3A5AC0 : 0xFF3A9A4A;
        DrawPerson(dc, x, feet, p.Facing, p.Walk, p.Pose, shirt, legs, 1);
    }

    /// <summary>A small explorer: hat, shirt, trousers; side on walking, back on when climbing, arms up sliding a pole.</summary>
    private static void DrawPerson(DrawingContext dc, double x, double feet, int facing, double walk, Pose pose, uint shirt, uint legs, double k)
    {
        bool climbing = pose == Pose.Climbing, sliding = pose == Pose.Sliding, falling = pose == Pose.Falling;
        double swing = pose == Pose.Walking ? Math.Sin(walk * 14) * 6 * k : climbing ? Math.Sin(walk * 10) * 4 * k : 0;
        double hip = feet - 30 * k, shoulder = feet - 54 * k;
        dc.DrawLine(Pen(legs, 6 * k), new Point(x - 3 * k, hip), new Point(x - 3 * k + (climbing ? 0 : swing), feet - 2 - (climbing ? Math.Max(0, swing) : 0)));
        dc.DrawLine(Pen(legs, 6 * k), new Point(x + 3 * k, hip), new Point(x + 3 * k - (climbing ? 0 : swing), feet - 2 - (climbing ? Math.Max(0, -swing) : 0)));
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 6 * k + (climbing ? 0 : swing), feet - 4 * k, 9 * k, 4 * k));
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 3 * k - (climbing ? 0 : swing), feet - 4 * k, 9 * k, 4 * k));
        dc.FillRectangle(Brush(shirt), new Rect(x - 8 * k, shoulder, 16 * k, 26 * k), (float)(3 * k));
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(x - 8 * k, hip - 4 * k, 16 * k, 4 * k));
        if (climbing || sliding || falling)
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x - 7 * k, shoulder + 3), new Point(x - 10 * k, shoulder - 14 * k + (climbing ? swing : 0)));
            dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x + 7 * k, shoulder + 3), new Point(x + 10 * k, shoulder - 14 * k - (climbing ? swing : 0)));
        }
        else
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x - 7 * k, shoulder + 3), new Point(x - 9 * k - swing * 0.5, shoulder + 22 * k));
            dc.DrawLine(Pen(0xFFF0C8A0, 4 * k), new Point(x + 7 * k, shoulder + 3), new Point(x + 9 * k + swing * 0.5, shoulder + 22 * k));
        }
        dc.DrawEllipse(Brush(0xFFF0C8A0), Pen(0xFF9A6A4A, 1), new Point(x, shoulder - 9 * k), 8 * k, 8 * k);
        if (!climbing) dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + facing * 4 * k, shoulder - 10 * k), 1.6 * k, 1.8 * k);
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(x - 11 * k, shoulder - 16 * k, 22 * k, 3 * k), (float)(1.5 * k));
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(x - 7 * k, shoulder - 23 * k, 14 * k, 8 * k), (float)(2 * k));
    }

    private void DrawMonster(DrawingContext dc, Monster m)
    {
        double x = m.X * T, feet = m.Y * T, k = 1;
        double swing = Math.Sin(m.Walk * 9) * 5;
        if (m.Kind == MonsterKind.Mummy)
        {
            dc.DrawLine(Pen(0xFFD8D0B0, 7), new Point(x - 4, feet - 32), new Point(x - 4 + swing, feet - 2));
            dc.DrawLine(Pen(0xFFD8D0B0, 7), new Point(x + 4, feet - 32), new Point(x + 4 - swing, feet - 2));
            dc.FillRectangle(Brush(0xFFE0D8B8), new Rect(x - 10, feet - 62, 20, 32), 4);
            for (int i = 0; i < 6; i++) dc.DrawLine(Pen(0xFFA8A080, 1.5), new Point(x - 10, feet - 58 + i * 6), new Point(x + 10, feet - 55 + i * 6));
            // Arms out in front.
            dc.DrawLine(Pen(0xFFE0D8B8, 6), new Point(x + m.Facing * 6, feet - 56), new Point(x + m.Facing * 24, feet - 54 + Math.Sin(m.Walk * 5) * 2));
            dc.DrawEllipse(Brush(0xFFE0D8B8), Pen(0xFFA8A080, 1), new Point(x, feet - 70), 9, 9);
            dc.DrawLine(Pen(0xFFA8A080, 1.5), new Point(x - 8, feet - 72), new Point(x + 8, feet - 69));
            dc.DrawEllipse(Brush(0xFFFF4A2A), null, new Point(x + m.Facing * 4, feet - 71), 2, 2);
            return;
        }
        // The monster: big boots, a dark coat, green skin, a flat head with a bolt each side.
        double climb = m.Climb != 0 ? Math.Sin(m.Walk * 9) * 4 : 0;
        dc.DrawLine(Pen(0xFF2A2A30, 8), new Point(x - 5, feet - 32), new Point(x - 5 + (m.Climb != 0 ? 0 : swing), feet - 4));
        dc.DrawLine(Pen(0xFF2A2A30, 8), new Point(x + 5, feet - 32), new Point(x + 5 - (m.Climb != 0 ? 0 : swing), feet - 4));
        dc.FillRectangle(Brush(0xFF1A1A1A), new Rect(x - 12 + swing, feet - 6, 13, 6), 2);
        dc.FillRectangle(Brush(0xFF1A1A1A), new Rect(x - 1 - swing, feet - 6, 13, 6), 2);
        dc.FillRectangle(Brush(0xFF3A3A5A), new Rect(x - 13, feet - 66, 26, 36), 4);
        if (m.Climb != 0)
        {
            dc.DrawLine(Pen(0xFF6AA05A, 6), new Point(x - 11, feet - 62), new Point(x - 13, feet - 80 + climb));
            dc.DrawLine(Pen(0xFF6AA05A, 6), new Point(x + 11, feet - 62), new Point(x + 13, feet - 80 - climb));
        }
        else
        {
            dc.DrawLine(Pen(0xFF3A3A5A, 7), new Point(x + m.Facing * 8, feet - 62), new Point(x + m.Facing * 26, feet - 58));
            dc.DrawEllipse(Brush(Moss), null, new Point(x + m.Facing * 27, feet - 58), 4, 4);
        }
        dc.FillRectangle(Brush(Moss), new Rect(x - 10, feet - 86, 20, 20), 3);
        dc.FillRectangle(Brush(0xFF1A1A1A), new Rect(x - 11, feet - 90, 22, 6), 2);
        dc.FillRectangle(Brush(0xFF8A8A8A), new Rect(x - 14, feet - 74, 4, 5));
        dc.FillRectangle(Brush(0xFF8A8A8A), new Rect(x + 10, feet - 74, 4, 5));
        if (m.Climb == 0)
        {
            dc.FillRectangle(Brush(0xFFFFF04A), new Rect(x + m.Facing * 3 - 2, feet - 80, 4, 3));
            dc.DrawLine(Pen(0xFF1A1A1A, 1.5), new Point(x - 4 + m.Facing * 2, feet - 71), new Point(x + 4 + m.Facing * 2, feet - 71));
            dc.DrawLine(Pen(0xFF1A1A1A, 1), new Point(x - 6, feet - 83), new Point(x + 6, feet - 82));
        }
        _ = k;
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life, x = e.X * T, y = e.Y * T;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(x, y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(x, y - t * 20), 20, e.Colour, center: true);
                break;
            case EffectKind.Puff:
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4, d = 8 + 30 * t;
                    dc.DrawEllipse(Brush(Alpha(0xFFD0D0E0, 0.8 * (1 - t))), null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d), 8 * (1 - t) + 3, 8 * (1 - t) + 3);
                }
                break;
            case EffectKind.Zap:
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3 + t * 4, d = 10 + 24 * t;
                    dc.DrawLine(Pen(Alpha(0xFFB0F0FF, 1 - t), 2), new Point(x + Math.Cos(a) * d * 0.4, y + Math.Sin(a) * d * 0.4), new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d));
                }
                break;
        }
    }

    // ───────────────────────────── the map ─────────────────────────────

    /// <summary>The castle map: the rooms found so far, the doors between them, and where everyone is.</summary>
    private void DrawMap(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Castle;
        dc.FillRectangle(Brush(0xE00A0810), new Rect(40, Field.Top + 10, Field.Width - 80, Field.Height - Field.Top - 30), 12);
        dc.DrawRectangle(null, Pen(0xFF8A7AB0, 2), new Rect(40, Field.Top + 10, Field.Width - 80, Field.Height - Field.Top - 30), 12);
        Text(dc, $"{c.Name.ToUpperInvariant()} - THE MAP", new Point(512, Field.Top + 40), 34, 0xFFD8C8F0, center: true);
        int minX = c.Rooms.Min(r => r.MapX), maxX = c.Rooms.Max(r => r.MapX + r.MapW), minY = c.Rooms.Min(r => r.MapY), maxY = c.Rooms.Max(r => r.MapY + r.MapH);
        double cell = Math.Min(860.0 / (maxX - minX + 1), 520.0 / (maxY - minY + 1));
        double ox = 512 - (maxX - minX) * cell / 2, oy = Field.Top + 80 + (560 - (maxY - minY) * cell) / 2;
        Point Mid(Room r) => new(ox + (r.MapX - minX + r.MapW / 2.0) * cell, oy + (r.MapY - minY + r.MapH / 2.0) * cell);
        foreach (var r in c.Rooms.Where(r => r.Seen))
            foreach (var d in r.Things.Where(t => t.Kind == Kind.Door))
            {
                var other = c.Room(int.Parse(d.Arg(0)));
                if (other.Seen && other.Id > r.Id) dc.DrawLine(Pen(0xFF8A7AB0, 3), Mid(r), Mid(other));
            }
        foreach (var r in c.Rooms.Where(r => r.Seen))
        {
            var rect = new Rect(ox + (r.MapX - minX) * cell + 4, oy + (r.MapY - minY) * cell + 4, r.MapW * cell - 8, r.MapH * cell - 8);
            bool here = g.Players.Any(p => p.Active && p.Room == r.Id);
            dc.FillRectangle(Brush(here ? 0xFF4A3A6Au : 0xFF2A2440u), rect, 6);
            dc.DrawRectangle(null, Pen(here ? 0xFFF0E0FFu : 0xFF8A7AB0u, 2), rect, 6);
            Text(dc, r.Name.ToUpperInvariant(), new Point(rect.Center.X, rect.Center.Y - 8), 16, 0xFFD8C8F0, center: true);
            if (r.Things.Any(t => t.Kind == Kind.Exit)) Text(dc, "EXIT", new Point(rect.Center.X, rect.Center.Y + 12), 18, 0xFF8AF08A, center: true);
            foreach (var p in g.Players.Where(p => p.Active && p.Room == r.Id))
                dc.DrawEllipse(Brush(p.Index == 0 ? 0xFFF2F2F2u : 0xFFF0D040u), null, new Point(rect.X + 14 + p.Index * 16, rect.Y + 14), 6, 6);
        }
        Text(dc, "TAB OR BACK: CLOSE THE MAP", new Point(512, Field.Height - 40), 20, Dim, center: true);
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF120E1A), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF5A4A7A), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 18), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 18), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 18), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 18), 28, Dim, right: true);
        if (!InGame) return;
        string room = g.Players.FirstOrDefault(p => p.Active) is { } a ? g.Castle.Room(a.Room).Name.ToUpperInvariant() : "";
        Text(dc, g.Castle.Name.ToUpperInvariant(), new Point(512, 16), 26, 0xFFD8C8F0, center: true);
        Text(dc, room, new Point(512, 42), 20, Dim, center: true);
        Text(dc, $"{(int)g.Clock / 60}:{g.Clock % 60:00.0}", new Point(Field.Width - 16, 54), 26, White, right: true);
        if (g.Best.Of(g.Castle.Name) is { } best) Text(dc, $"BEST {(int)best / 60}:{best % 60:00.0}", new Point(Field.Width - 130, 54), 18, Dim, right: true);
        // Each player's lives.
        for (int i = 0; i < g.Players.Count; i++)
        {
            var p = g.Players[i];
            double x = 16 + i * 150;
            Text(dc, $"P{i + 1}", new Point(x, 52), 18, i == 0 ? 0xFFF2F2F2 : 0xFFF0D040);
            if (p.Pose == Pose.Out) Text(dc, "OUT", new Point(x + 26, 52), 18, Red);
            else if (p.Pose == Pose.Escaped) Text(dc, "OUT!", new Point(x + 26, 52), 18, 0xFF8AF08A);
            else
                for (int l = 0; l < Math.Min(p.Lives, 6); l++)
                    dc.DrawEllipse(Brush(i == 0 ? 0xFFF2F2F2u : 0xFFF0D040u), null, new Point(x + 32 + l * 14, 52), 4.5, 4.5);
        }
        // The keys in hand.
        int n = 0;
        foreach (var key in g.Keys) DrawKey(dc, 330 + n++ * 34, 54, KeyColour(key), 0.9);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 300;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xE0120E1A), new Rect(cx - w / 2, cy - 46, w, h), 8);
        dc.DrawRectangle(null, Pen(0xFF8A7AB0, 2), new Rect(cx - w / 2, cy - 46, w, h), 8);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, msg == "GAME OVER" ? Red : 0xFFD8C8F0, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 24, White, center: true);
    }

    /// <summary>The title: the castle on its hill under the moon, and the list of castles to choose from with their best times.</summary>
    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        uint[] sky = [0xFF0A0818, 0xFF120E26, 0xFF1A1432, 0xFF221A3E];
        for (int i = 0; i < sky.Length; i++) dc.FillRectangle(Brush(sky[i]), new Rect(0, Field.Hud + i * 172, Field.Width, 173));
        for (int i = 0; i < 80; i++)
        {
            uint q = Hash(i, 4, 4);
            dc.FillRectangle(Brush(0x90FFFFFF), new Rect(q % 1024, Field.Hud + (q >> 10) % 400, 2, 2));
        }
        dc.DrawEllipse(Brush(0xFFF2EED8), null, new Point(900, 270), 50, 50);
        dc.DrawEllipse(Brush(0xFF120E26), null, new Point(920, 258), 45, 45);
        // The castle's silhouette.
        Poly(dc, 0xFF06040C, null, 0, new Point(560, 768), new Point(560, 420), new Point(590, 420), new Point(590, 380), new Point(610, 360), new Point(630, 380), new Point(630, 420),
            new Point(700, 420), new Point(700, 300), new Point(725, 270), new Point(750, 300), new Point(750, 420), new Point(820, 420), new Point(820, 360), new Point(845, 335),
            new Point(870, 360), new Point(870, 420), new Point(960, 420), new Point(960, 768));
        for (int i = 0; i < 5; i++)
            dc.FillRectangle(Brush((int)(_clock * 2 + i) % 7 == 0 ? 0xFF6A5A1Au : 0xFFE0C060u), new Rect(600 + i * 70, 470 + (i % 2) * 60, 10, 16));
        // Bats.
        for (int i = 0; i < 3; i++)
        {
            double bx = 200 + i * 230 + Math.Sin(_clock * 0.7 + i) * 60, by = 170 + Math.Cos(_clock * 0.9 + i * 2) * 30, f = Math.Sin(_clock * 12 + i) * 6;
            Poly(dc, 0xFF06040C, null, 0, new Point(bx - 16, by - f), new Point(bx - 6, by - 2), new Point(bx, by + 3), new Point(bx + 6, by - 2), new Point(bx + 16, by - f), new Point(bx, by - 4));
        }
        string title = "AVA CASTLES OF DR. CREEP";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 2.5 + i * 0.4) * 3;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 17.5 + 17.5 + i * 35, 126 + bob), 66, i % 2 == 0 ? 0xFFB89AF0 : 0xFFE8DEFF, center: true);
        }
        Text(dc, "AFTER THE 1984 COMMODORE 64 CLASSIC", new Point(cx, 178), 24, 0xFFB0A0D0, center: true);
        // The castles.
        Text(dc, "CHOOSE A CASTLE", new Point(250, 236), 28, Yellow, center: true);
        for (int i = 0; i < g.Castles.Count; i++)
        {
            var c = g.Castles[i];
            double y = 280 + i * 44;
            bool on = i == g.Choice;
            if (on) dc.FillRectangle(Brush(0x803A2A5A), new Rect(60, y - 18, 380, 36), 6);
            Text(dc, (on ? "> " : "  ") + c.Name.ToUpperInvariant(), new Point(76, y), 30, on ? Yellow : White);
            string best = g.Best.Of(c.Name) is { } b ? $"{(int)b / 60}:{b % 60:00.0}" : "--:--";
            Text(dc, best, new Point(430, y), 26, on ? Yellow : Dim, right: true);
            Text(dc, $"{c.Rooms.Count} ROOMS", new Point(300, y), 18, Dim, right: true);
        }
        double hy = 290 + g.Castles.Count * 44;
        Text(dc, "UP/DOWN: CHOOSE", new Point(250, hy), 22, 0xFFB0A0D0, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "ENTER OR 1: ONE PLAYER    2: TWO PLAYERS", new Point(cx, 600), 36, Yellow, center: true);
        Text(dc, "ARROWS: WALK, CLIMB, SLIDE    UP AT A DOOR: GO THROUGH    SPACE: PRESS, PULL, FIRE", new Point(cx, 650), 20, 0xFFB0A0D0, center: true);
        Text(dc, "TWO PLAYERS: P1 ARROWS + RIGHT CTRL, P2 W A S D + LEFT CTRL    TAB: THE MAP    P: PAUSE    M: SOUND", new Point(cx, 678), 18, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF120E1A), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE GREAT ESCAPERS", new Point(cx, 140), 64, 0xFFB89AF0, center: true);
        Text(dc, "RANK NAME    SCORE  CASTLES", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF120E1A), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
