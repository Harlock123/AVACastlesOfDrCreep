using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVACastlesOfDrCreep.Engine;
using AVACastlesOfDrCreep.Views;
using Controls = AVACastlesOfDrCreep.Engine.Controls;

namespace AVACastlesOfDrCreep.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set DRCREEP_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avacastlesofdrcreep-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("DRCREEP_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVACastlesOfDrCreep-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("DRC", 48300, 4);
        g.HighScores.Insert("AVA", 21500, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A castle walked by its way out until something worth showing.</summary>
    private static void Walk(int castle, string name, Func<GameEngine, bool> moment, int seconds = 200)
    {
        var (w, c, g) = Open();
        g.Go(castle);
        g.Pilot.Reset(g.Castle);
        Assert.True(PlayUntil(g, c, moment, seconds));
        Shot(w, c, name);
    }

    private static Player P(GameEngine g) => g.Players[0];

    [AvaloniaFact]
    public void The_gatehouse() => Walk(0, "gatehouse", g => P(g).Room == 0 && P(g).Pose == Pose.Climbing && P(g).Y < 12);

    [AvaloniaFact]
    public void The_trapdoor() => Walk(0, "trapdoor", g => P(g).Room == 2 && g.Monsters.Any(m => m.Fall > 0.1));

    [AvaloniaFact]
    public void The_machines() => Walk(0, "machines", g => P(g).Room == 3 && P(g).X > 15 && P(g).X < 17);

    [AvaloniaFact]
    public void A_mummy_on_your_heels() => Walk(1, "mummy", g => P(g).Room == 0 && g.Monsters.Count == 1 && P(g).Pose == Pose.Climbing && P(g).Y < 13);

    [AvaloniaFact]
    public void The_ray_gun() => Walk(2, "ray-gun", g => P(g).Room == 4 && g.Castle.Room(4).Things.Any(t => t.Kind == Kind.Ray && t.Beam > 0.15) && g.Effects.Any(e => e.Kind == EffectKind.Puff));

    [AvaloniaFact]
    public void The_transmitter_ledge() => Walk(2, "ledge", g => P(g).Room == 3 && P(g).Y < 6 && g.Monsters.Count == 1 && P(g).X > 22);

    [AvaloniaFact]
    public void The_laboratory() => Walk(4, "laboratory", g => P(g).Room == 4 && P(g).X > 10.5 && P(g).X < 12 && P(g).Pose == Pose.Walking && P(g).Y > 14, seconds: 300);

    [AvaloniaFact]
    public void The_cellar() => Walk(4, "cellar", g => P(g).Room == 0 && g.Monsters.Any(m => m.Y < 11) && P(g).X > 18);

    [AvaloniaFact]
    public void The_map()
    {
        var (w, c, g) = Open();
        g.Go(3);
        g.Pilot.Reset(g.Castle);
        Assert.True(PlayUntil(g, c, g => P(g).Room == 3, 200));
        g.ShowMap = true;
        Shot(w, c, "map");
    }

    [AvaloniaFact]
    public void Two_players_apart()
    {
        var (w, c, g) = Open();
        g.Go(1, players: 2);
        // Player two at the gallery's gun control, so the gun's theirs; player one off towards the ladder.
        g.Put(1, 1, 4.5, 10);
        for (int i = 0; i < 100; i++) { g.Tick(new Controls(i < 70 ? 1 : 0, 0), new Controls(0, i < 50 ? -1 : 0)); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "two-players");
    }

    [AvaloniaFact]
    public void Escaped() => Walk(0, "escaped", g => g.Phase == Phase.Clear && g.PhaseTime > 0.5);
}
