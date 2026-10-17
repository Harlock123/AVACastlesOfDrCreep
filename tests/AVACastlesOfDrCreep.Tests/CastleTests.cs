using AVACastlesOfDrCreep.Engine;

namespace AVACastlesOfDrCreep.Tests;

/// <summary>Every castle read, sound, and escaped by its written way out without losing a life.</summary>
public class CastleTests(ITestOutputHelper output)
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacastlesofdrcreep-tests-" + Guid.NewGuid().ToString("N"));

    public static TheoryData<int> Castles() => [.. Enumerable.Range(0, CastleBook.Texts.Count)];

    [Theory]
    [MemberData(nameof(Castles))]
    public void Every_castle_can_be_escaped_by_its_way_out(int index)
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(index);
        g.Pilot.Reset(g.Castle);
        var p = g.Players[0];
        int t = 0, step = -1;
        for (; t < 60 * 600 && g.Phase == Phase.Play; t++)
        {
            if (g.Pilot.StepIndex != step) { step = g.Pilot.StepIndex; output.WriteLine($"{t / 60.0,6:0.0}s room {p.Room} ({p.X:0.0},{p.Y:0.0}) {p.Pose}: {g.Pilot.Doing}"); }
            g.Tick(g.Pilot.Decide(g));
            Assert.True(p.Pose != Pose.Dead, $"{g.Castle.Name}: lost a life ({g.Cause}) in room {p.Room} at ({p.X:0.0},{p.Y:0.0}) doing \"{g.Pilot.Doing}\"");
        }
        Assert.True(g.Phase == Phase.Clear, $"{g.Castle.Name}: not out after {t / 60}s; stuck in room {p.Room} at ({p.X:0.0},{p.Y:0.0}) {p.Pose} doing \"{g.Pilot.Doing}\"");
        output.WriteLine($"{g.Castle.Name}: escaped in {g.Clock:0.0}s (par {g.Castle.Par})");
        foreach (var m in g.Monsters) output.WriteLine($"  {m.Kind} in room {m.Room}: {(m.Dead ? "dead" : "still about")} at ({m.X:0.0},{m.Y:0.0})");
        Assert.False(g.Pilot.TimedOut, $"{g.Castle.Name}: a monster it watched for never came");
        Assert.True(g.Clock < g.Castle.Par, $"{g.Castle.Name}: {g.Clock:0.0}s is over par");
    }

    [Theory]
    [MemberData(nameof(Castles))]
    public void Every_door_leads_to_a_door_that_leads_back(int index)
    {
        var c = Castle.Parse(CastleBook.Texts[index]);
        foreach (var room in c.Rooms)
        foreach (var d in room.Things.Where(t => t.Kind == Kind.Door))
        {
            var other = c.Room(int.Parse(d.Arg(0))).Find(d.Arg(1)[0]);
            Assert.True(other is { Kind: Kind.Door }, $"{c.Name}: room {room.Id} door {d.Letter} leads nowhere");
            Assert.Equal((room.Id, d.Letter), (int.Parse(other!.Arg(0)), other.Arg(1)[0]));
        }
        Assert.Contains(c.Rooms.SelectMany(r => r.Things), t => t.Kind == Kind.Exit);
        Assert.NotNull(c.Room(c.StartRoom).Find(c.StartAt));
    }
}
