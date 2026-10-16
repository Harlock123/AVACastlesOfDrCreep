using System.Globalization;

namespace AVACastlesOfDrCreep.Engine;

/// <summary>The quickest escape from each castle, kept in the same folder as the high scores.</summary>
public sealed class BestTimes
{
    private readonly string? _path;
    private readonly Dictionary<string, double> _times = [];

    private BestTimes(string? path) => _path = path;

    public static BestTimes Load(string folder)
    {
        var b = new BestTimes(Path.Combine(folder, "besttimes.txt"));
        try
        {
            if (File.Exists(b._path))
                foreach (var line in File.ReadAllLines(b._path))
                {
                    int bar = line.LastIndexOf('|');
                    if (bar > 0 && double.TryParse(line[(bar + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) b._times[line[..bar]] = t;
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return b;
    }

    public double? Of(string castle) => _times.TryGetValue(castle, out double t) ? t : null;

    /// <summary>A time for a castle: kept (and saved) if it's the best yet, which is what it says.</summary>
    public bool Record(string castle, double seconds)
    {
        if (Of(castle) is { } best && best <= seconds) return false;
        _times[castle] = seconds;
        try
        {
            if (_path == null) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, _times.Select(kv => $"{kv.Key}|{kv.Value.ToString("0.0", CultureInfo.InvariantCulture)}"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }
}
