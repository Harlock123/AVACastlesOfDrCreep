using AVACastlesOfDrCreep.Engine;

namespace AVACastlesOfDrCreep.Tests;

/// <summary>Castles built in code for the tests, the way tools/make_castles.py builds the game's.</summary>
public sealed class TestRoom(int id)
{
    private readonly char[,] _g = Blank();
    private readonly List<string> _things = [];

    private static char[,] Blank()
    {
        var g = new char[Field.Rows, Field.Cols];
        for (int r = 0; r < Field.Rows; r++) for (int c = 0; c < Field.Cols; c++) g[r, c] = '.';
        return g;
    }

    public TestRoom Floor(int row, int x0, int x1)
    {
        for (int x = x0; x <= x1; x++) if (_g[row, x] == '.') _g[row, x] = '=';
        return this;
    }

    public TestRoom Ladder(int col, int top, int bottom)
    {
        _g[top, col] = '#';
        for (int r = top + 1; r < bottom; r++) _g[r, col] = 'H';
        return this;
    }

    public TestRoom Pole(int col, int top, int bottom)
    {
        _g[top, col] = '!';
        for (int r = top + 1; r < bottom; r++) _g[r, col] = '|';
        return this;
    }

    public TestRoom Cells(int row, int x0, int x1, char ch)
    {
        for (int x = x0; x <= x1; x++) _g[row, x] = ch;
        return this;
    }

    public TestRoom Thing(char letter, int col, int floorRow, string kind, params object[] args)
    {
        _g[floorRow - 1, col] = letter;
        _things.Add(string.Join(' ', [letter.ToString(), kind, .. args.Select(a => a.ToString()!)]));
        return this;
    }

    public string Text()
    {
        var lines = new List<string> { $"room {id} {id * 4} 0 3 2 Room {id}" };
        for (int r = 0; r < Field.Rows; r++) lines.Add(new string(Enumerable.Range(0, Field.Cols).Select(c => _g[r, c]).ToArray()));
        lines.AddRange(_things);
        return string.Join('\n', lines);
    }

    public static string Castle(string start, params TestRoom[] rooms) =>
        $"castle Test\npar 100\nstart {start}\n\n{string.Join("\n\n", rooms.Select(r => r.Text()))}\nsolution\n";
}
