namespace AVACastlesOfDrCreep.Engine;

/// <summary>One player's controls: left and right, up and down (up the screen is -1), and the action button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The screen: a room of 24 by 16 squares under the score bar.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 80;
    public const int Cols = 24, Rows = 16;
    public const double Tile = 40;
    public const double Left = (Width - Cols * Tile) / 2, Top = Hud + 8;
}

/// <summary>Everything a room can hold besides its floors, ladders and poles.</summary>
public enum Kind
{
    Spawn, Door, Exit, Bell, Key, Lock, TrapSwitch, WalkControl, Field, FieldButton, Bolt, BoltSwitch,
    Ray, RayBox, Booth, Receiver, Tomb, Ankh, Coffin,
}

/// <summary>
/// A thing in a room, standing on the floor below the square it's marked in (so its feet are at <see cref="Y"/> + 1).
/// Doors, bells and switches name what they work by letter; trapdoors and walkways by number, in the order they
/// appear in the room.
/// </summary>
public sealed class Thing
{
    public Kind Kind;
    public char Letter;
    public int X, Y;
    public string[] Args = [];
    // Its state as the game goes on.
    public bool Open, On;
    public double Timer, Aim, Cooldown, Beam;
    public int Selected, Facing;
    public bool Used;

    public double Feet => Y + 1;
    public double Middle => X + 0.5;
    public string Arg(int i, string fallback = "") => i < Args.Length ? Args[i] : fallback;
}

/// <summary>A run of trapdoor squares in a floor, open (a hole) or shut.</summary>
public sealed class Trapdoor
{
    public int X, Y, Width;
    public bool Open;
}

/// <summary>A run of moving walkway: -1 carrying left, 1 right, 0 still.</summary>
public sealed class Walkway
{
    public int X, Y, Width, Dir, Last = 1;
}

public sealed class Room
{
    public int Id;
    public string Name = "";
    /// <summary>Where it sits on the castle map, in map squares.</summary>
    public int MapX, MapY, MapW = 3, MapH = 2;
    public char[,] Cells = new char[Field.Cols, Field.Rows];
    public List<Thing> Things { get; } = [];
    public List<Trapdoor> Traps { get; } = [];
    public List<Walkway> Walkways { get; } = [];
    public bool Seen;

    public char At(int c, int r) => c < 0 || r < 0 || c >= Field.Cols || r >= Field.Rows ? ' ' : Cells[c, r];

    public Trapdoor? TrapAt(int c, int r) => Traps.FirstOrDefault(t => t.Y == r && c >= t.X && c < t.X + t.Width);
    public Walkway? WalkAt(int c, int r) => Walkways.FirstOrDefault(w => w.Y == r && c >= w.X && c < w.X + w.Width);

    /// <summary>Whether there's something to stand on at the top of a square.</summary>
    public bool Floor(int c, int r) => At(c, r) switch
    {
        '=' or '#' or '!' or '<' or '>' or '-' => true,
        'T' => TrapAt(c, r) is { Open: false },
        _ => false,
    };

    public bool Ladder(int c, int r) => At(c, r) is 'H' or '#';
    public bool Pole(int c, int r) => At(c, r) is '|' or '!';

    public Thing? Find(char letter) => Things.FirstOrDefault(t => t.Letter == letter);
}

/// <summary>A castle: its rooms, where the players come in, and the way out (written down, for the demonstration and the tests).</summary>
public sealed class Castle
{
    public string Name = "";
    /// <summary>A target time, in seconds: escaping quicker than this earns a bonus.</summary>
    public int Par = 120;
    public int StartRoom;
    public char StartAt;
    public List<Room> Rooms { get; } = [];
    public List<string> Solution { get; } = [];

    public Room Room(int id) => Rooms.First(r => r.Id == id);

    /// <summary>
    /// Reads a castle from its text. A room is a header line, sixteen lines of twenty-four squares, then a line per
    /// thing; the solution comes last.
    /// <code>
    /// castle NAME / par SECONDS / start ROOM LETTER
    /// room ID MAPX MAPY MAPW MAPH NAME...
    ///   . empty  = floor  H ladder  # ladder through a floor  | pole  ! pole through a floor
    ///   T trapdoor  &lt; &gt; - walkway (carrying left, right, still)  a-z a thing, standing on the floor below
    /// LETTER KIND ARGS...
    /// solution / STEP...
    /// </code>
    /// </summary>
    public static Castle Parse(string text)
    {
        var c = new Castle();
        var lines = text.Replace("\r", "").Split('\n');
        Room? room = null;
        int row = -1;
        bool solving = false;
        foreach (var raw in lines)
        {
            string line = raw.TrimEnd();
            int hash = line.IndexOf("  #", StringComparison.Ordinal);
            if (row < 0 && hash >= 0) line = line[..hash].TrimEnd();
            if (row >= 0)
            {
                // A row of the room's squares.
                for (int x = 0; x < Field.Cols; x++)
                {
                    char ch = x < line.Length ? line[x] : '.';
                    room!.Cells[x, row] = ch == '.' ? ' ' : ch;
                }
                if (++row == Field.Rows) { Read(room!); row = -1; }
                continue;
            }
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (solving) { c.Solution.Add(line.Trim()); continue; }
            switch (w[0])
            {
                case "castle": c.Name = string.Join(' ', w[1..]); break;
                case "par": c.Par = int.Parse(w[1]); break;
                case "start": c.StartRoom = int.Parse(w[1]); c.StartAt = w[2][0]; break;
                case "room":
                    room = new Room { Id = int.Parse(w[1]), MapX = int.Parse(w[2]), MapY = int.Parse(w[3]), MapW = int.Parse(w[4]), MapH = int.Parse(w[5]), Name = string.Join(' ', w[6..]) };
                    c.Rooms.Add(room);
                    row = 0;
                    break;
                case "solution": solving = true; break;
                default:
                    if (w[0].Length != 1 || room == null) throw new FormatException($"{c.Name}: can't read \"{line}\"");
                    var t = room.Find(w[0][0]) ?? throw new FormatException($"{c.Name} room {room.Id}: no '{w[0]}' in the room");
                    t.Kind = Enum.Parse<Kind>(w[1], ignoreCase: true);
                    t.Args = w[2..];
                    Init(t);
                    break;
            }
        }
        return c;
    }

    /// <summary>A room's squares read: the things marked in it, and its trapdoors and walkways, in order.</summary>
    private static void Read(Room room)
    {
        for (int r = 0; r < Field.Rows; r++)
        for (int x = 0; x < Field.Cols; x++)
        {
            char ch = room.Cells[x, r];
            if (ch is >= 'a' and <= 'z')
            {
                room.Things.Add(new Thing { Letter = ch, X = x, Y = r });
                room.Cells[x, r] = ' ';
            }
            else if (ch == 'T' && room.At(x - 1, r) != 'T')
            {
                int n = 0;
                while (room.At(x + n, r) == 'T') n++;
                room.Traps.Add(new Trapdoor { X = x, Y = r, Width = n });
            }
            else if (ch is '<' or '>' or '-' && room.At(x - 1, r) is not ('<' or '>' or '-'))
            {
                int n = 0;
                while (room.At(x + n, r) is '<' or '>' or '-') n++;
                room.Walkways.Add(new Walkway { X = x, Y = r, Width = n, Dir = ch == '<' ? -1 : ch == '>' ? 1 : 0, Last = ch == '<' ? -1 : 1 });
            }
        }
    }

    /// <summary>A thing's starting state from its arguments.</summary>
    private static void Init(Thing t)
    {
        switch (t.Kind)
        {
            case Kind.Door or Kind.Exit: t.Open = t.Args.Contains("open"); break;
            case Kind.Bolt: t.On = !t.Args.Contains("off"); break;
            case Kind.Field: t.On = true; break;
            case Kind.Ray: t.Facing = t.Args.Contains("left") ? -1 : 1; t.Aim = t.Y; break;
            case Kind.Coffin: t.Facing = t.Args.Contains("left") ? -1 : 1; break;
        }
    }
}

public enum Pose { Walking, Climbing, Sliding, Falling, Dead, Out, Escaped }

/// <summary>A player: where, how, and what they've got.</summary>
public sealed class Player
{
    public int Index, Room, Lives = 3, Facing = 1;
    /// <summary>Across, in squares (the middle of the body), and down (the feet), in squares.</summary>
    public double X, Y, Vy, FallFrom, Walk, DeadTime, Safe;
    public Pose Pose;
    /// <summary>Where they last came in, to start again from after losing a life.</summary>
    public int EntryRoom;
    public char EntryAt;
    public bool FirePrev, UpPrev;
    /// <summary>Up or down on the stick this step (it works a ray gun from its control).</summary>
    public int Dy;
    public int Col => (int)Math.Floor(X);
    public bool Active => Pose is not (Pose.Dead or Pose.Out or Pose.Escaped);
    public const double HalfWidth = 0.3, Height = 1.8;
}

public enum MonsterKind { Mummy, Monster }

/// <summary>A mummy out of its tomb, or the monster out of its coffin.</summary>
public sealed class Monster
{
    public MonsterKind Kind;
    public int Room;
    public double X, Y, Walk;
    public int Facing = 1;
    /// <summary>Climbing (the monster only): moving up or down a ladder, or sliding down a pole.</summary>
    public int Climb;
    public double Fall;
    public bool Dead;
    public const double HalfWidth = 0.32, Height = 1.9;
}

public enum EffectKind { Score, Word, Puff, Zap }

public sealed class Effect
{
    public EffectKind Kind;
    public int Room;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
