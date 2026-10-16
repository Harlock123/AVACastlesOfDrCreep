using System.Reflection;

namespace AVACastlesOfDrCreep.Engine;

/// <summary>The game's castles, read from the castle files built into the program, in order (the tutorial first).</summary>
public static class CastleBook
{
    public static IReadOnlyList<string> Texts { get; } = Load();

    private static List<string> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames().Where(n => n.EndsWith(".castle", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var reader = new StreamReader(asm.GetManifestResourceStream(n)!);
                return reader.ReadToEnd();
            }).ToList();
    }
}
