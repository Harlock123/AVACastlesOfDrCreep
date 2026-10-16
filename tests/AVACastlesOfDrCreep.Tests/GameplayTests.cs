using AVACastlesOfDrCreep.Engine;

namespace AVACastlesOfDrCreep.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avacastlesofdrcreep-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in one test castle (or more), past its name, the player at its start.</summary>
    private static GameEngine Game(params string[] castles) => Game(1, castles);

    private static GameEngine Game(int players, params string[] castles)
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), castles) { Silent = true };
        g.Go(0, players);
        foreach (var p in g.Players) p.Safe = 0;
        return g;
    }

    /// <summary>A plain room: a floor along the bottom, a spawn in the middle.</summary>
    private static TestRoom Plain(int id = 0) => new TestRoom(id).Floor(15, 0, 23);

    private static readonly Controls Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1), Down = new(0, 1), Button = new(0, 0, true);

    private static void Press(GameEngine g, Controls c, int player = 0)
    {
        if (player == 0) { g.Tick(c); g.Tick(default); }
        else { g.Tick(default, c); g.Tick(default); }
    }

    private static Player P(GameEngine g, int i = 0) => g.Players[i];

    // ───────────────────────────── castles ─────────────────────────────

    [Fact]
    public void A_castle_reads_its_rooms_things_trapdoors_and_walkways()
    {
        var text = TestRoom.Castle("0 s", Plain().Cells(15, 3, 5, 'T').Cells(15, 8, 10, '>').Cells(15, 12, 13, 'T').Thing('s', 20, 15, "spawn").Thing('t', 1, 15, "trapswitch", 2));
        var c = Castle.Parse(text);
        var r = c.Room(0);
        Assert.Equal(2, r.Traps.Count);
        Assert.Equal((12, 2), (r.Traps[1].X, r.Traps[1].Width));
        Assert.Equal((8, 3, 1), (r.Walkways[0].X, r.Walkways[0].Width, r.Walkways[0].Dir));
        Assert.Equal((20, 14, Kind.Spawn), (r.Find('s')!.X, r.Find('s')!.Y, r.Find('s')!.Kind));
        Assert.Equal("2", r.Find('t')!.Arg(0));
        Assert.True(r.Floor(4, 15));
        r.Traps[0].Open = true;
        Assert.False(r.Floor(4, 15));
    }

    [Fact]
    public void The_game_has_its_tutorial_and_four_castles_in_order()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(["Tutorial", "Ravenmoor", "Grimwald", "Mortlake", "Blackspire"], g.Castles.Select(c => c.Name));
        Assert.True(g.Castles.Zip(g.Castles.Skip(1)).All(p => p.Second.Par >= p.First.Par));
    }

    // ───────────────────────────── moving about ─────────────────────────────

    [Fact]
    public void The_player_walks_and_is_kept_inside_the_room()
    {
        var g = Game(TestRoom.Castle("0 s", Plain().Thing('s', 10, 15, "spawn")));
        g.Run(30, Right);
        Assert.Equal(10.5 + GameEngine.WalkSpeed / 2, P(g).X, 1);
        g.Run(60 * 5, Right);
        Assert.Equal(Field.Cols - Player.HalfWidth, P(g).X, 3);
        Assert.Equal(1, P(g).Facing);
    }

    [Fact]
    public void Ladders_go_up_and_down_and_a_pole_only_down()
    {
        // (A thing can't stand on a ladder square: it would take the rung's place.)
        var room = Plain().Floor(10, 0, 12).Ladder(4, 10, 15).Pole(9, 10, 15).Thing('s', 2, 15, "spawn");
        var g = Game(TestRoom.Castle("0 s", room));
        g.Put(0, 0, 4.5, 15);
        g.Run(60 * 2, Up);
        Assert.Equal((Pose.Walking, 10.0), (P(g).Pose, P(g).Y));
        g.Run(60, Right);
        g.Put(0, 0, 9.5, 10);
        g.Run(60, Down);
        Assert.Equal((Pose.Walking, 15.0), (P(g).Pose, P(g).Y));
        g.Run(60, Up);
        Assert.Equal(15.0, P(g).Y);
        g.Put(0, 0, 4.5, 15);
        g.Run(60 * 2, Up);
        g.Run(60 * 2, Down);
        Assert.Equal((Pose.Walking, 15.0), (P(g).Pose, P(g).Y));
    }

    [Fact]
    public void Walking_off_a_ledge_is_a_fall_short_ones_are_survived_and_long_ones_not()
    {
        var g = Game(TestRoom.Castle("0 s", Plain().Floor(13, 0, 6).Floor(5, 15, 23).Thing('s', 4, 13, "spawn")));
        g.Run(60, Right);
        Assert.Equal((Pose.Walking, 15.0), (P(g).Pose, P(g).Y));
        g.Put(0, 0, 18.5, 5);
        g.Run(60 * 2, Left);
        Assert.Equal(Pose.Dead, P(g).Pose);
        Assert.Equal("fell", g.Cause);
    }

    [Fact]
    public void A_shut_door_stays_shut_a_bell_opens_it_and_through_it_is_the_door_it_leads_to()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('a', 3, 15, "door", 1, "b").Thing('b', 7, 15, "bell", "a");
        var r1 = Plain(1).Thing('b', 20, 15, "door", 0, "a");
        var g = Game(TestRoom.Castle("0 s", r0, r1));
        g.Put(0, 0, 3.5, 15);
        Press(g, Up);
        Assert.Equal(0, P(g).Room);
        g.Put(0, 0, 7.5, 15);
        Press(g, Button);
        g.Put(0, 0, 3.5, 15);
        Press(g, Up);
        Assert.Equal((1, 20.5, 15.0), (P(g).Room, P(g).X, P(g).Y));
        Assert.True(g.Castle.Room(1).Find('b')!.Open);
        Assert.True(g.Castle.Room(1).Seen);
    }

    [Fact]
    public void A_lock_wants_its_key_and_walking_over_a_key_picks_it_up()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('l', 7, 15, "lock", "red", "d").Thing('d', 10, 15, "door", 0, "d").Thing('k', 14, 15, "key", "red");
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Put(0, 0, 7.5, 15);
        Press(g, Button);
        Assert.False(g.Castle.Room(0).Find('d')!.Open);
        g.Run(60 * 2, Right);
        Assert.Contains("red", g.Keys);
        Assert.Equal(GameEngine.KeyPoints, g.Score);
        g.Run(60 * 3, Left);
        g.Put(0, 0, 7.5, 15);
        Press(g, Button);
        Assert.True(g.Castle.Room(0).Find('d')!.Open);
    }

    [Fact]
    public void A_trapdoor_switch_opens_the_floor_and_whoever_walks_on_it_falls()
    {
        var r0 = Plain().Floor(10, 0, 23).Cells(10, 8, 10, 'T').Thing('t', 3, 10, "trapswitch", 1).Thing('s', 3, 15, "spawn");
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Put(0, 0, 9.5, 10);
        g.Tick(default);
        Assert.Equal(Pose.Walking, P(g).Pose);
        g.Put(0, 0, 3.5, 10);
        Press(g, Button);
        Assert.True(g.Castle.Room(0).Traps[0].Open);
        g.Run(150, Right);
        // Five squares down: too far to fall and live.
        Assert.Equal((Pose.Dead, "fell"), (P(g).Pose, g.Cause));
    }

    [Fact]
    public void A_walkway_carries_whoever_stands_on_it_and_its_control_stops_and_reverses_it()
    {
        var r0 = Plain().Cells(15, 6, 18, '>').Thing('c', 3, 15, "walkcontrol", 1).Thing('s', 10, 15, "spawn");
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Run(30, default);
        Assert.Equal(10.5 + GameEngine.CarrySpeed / 2, P(g).X, 1);
        g.Put(0, 0, 3.5, 15);
        Press(g, Button);
        Assert.Equal(0, g.Castle.Room(0).Walkways[0].Dir);
        Press(g, Button);
        Assert.Equal(-1, g.Castle.Room(0).Walkways[0].Dir);
    }

    [Fact]
    public void A_force_field_stops_the_player_until_its_button_drops_it_for_a_few_seconds()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('g', 3, 15, "fieldbutton", "f").Thing('f', 10, 15, "field", 3);
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Run(60 * 2, Right);
        Assert.True(P(g).X < 10.5);
        g.Run(60, Left);
        g.Put(0, 0, 3.5, 15);
        Press(g, Button);
        Assert.False(g.Castle.Room(0).Find('f')!.On);
        g.Run(60 * 2, Right);
        Assert.True(P(g).X > 10.5);
        g.Run((int)(GameEngine.FieldOffSeconds * 60), default);
        Assert.True(g.Castle.Room(0).Find('f')!.On);
    }

    [Fact]
    public void Lightning_kills_until_its_switch_turns_it_off()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('w', 3, 15, "boltswitch", "z").Thing('z', 10, 15, "bolt", 3);
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Run(60 * 2, Right);
        Assert.Equal((Pose.Dead, "zapped"), (P(g).Pose, g.Cause));
        g = Game(TestRoom.Castle("0 s", r0));
        g.Run(30, Left);
        g.Put(0, 0, 3.5, 15);
        Press(g, Button);
        g.Run(60 * 3, Right);
        Assert.True(P(g).Active && P(g).X > 12);
    }

    [Fact]
    public void A_ray_gun_finds_the_players_height_and_fires_but_from_its_control_it_is_yours()
    {
        var r0 = Plain().Thing('s', 10, 15, "spawn").Thing('x', 3, 15, "raybox", "y").Thing('y', 23, 15, "ray", 2, "left");
        var g = Game(TestRoom.Castle("0 s", r0));
        for (int i = 0; i < 60 * 8 && P(g).Active; i++) g.Tick(default);
        Assert.Equal((Pose.Dead, "rayed"), (P(g).Pose, g.Cause));

        g = Game(TestRoom.Castle("0 s", r0));
        g.Put(0, 0, 3.5, 15);
        var gun = g.Castle.Room(0).Find('y')!;
        g.Run(60 * 11, Up);
        Assert.True(P(g).Active);
        Assert.Equal(2, gun.Aim, 3);
        g.Run(60 * 11, Down);
        var m = g.PutMonster(MonsterKind.Monster, 0, 12.5, 15);
        Press(g, Button);
        Assert.True(m.Dead);
        Assert.True(P(g).Active);
    }

    [Fact]
    public void The_transmitter_sends_you_to_whichever_receiver_is_chosen()
    {
        var r0 = Plain().Floor(5, 15, 23).Thing('s', 5, 15, "spawn").Thing('m', 5, 15 - 0, "booth");
        // The booth and the spawn can't share a square: the booth at 5, the spawn next to it.
        r0 = Plain().Floor(5, 15, 23).Thing('s', 4, 15, "spawn").Thing('m', 5, 15, "booth").Thing('r', 20, 5, "receiver", "m", "red").Thing('q', 12, 15, "receiver", "m", "blue");
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Put(0, 0, 5.5, 15);
        Press(g, Button);
        Press(g, Up);
        Assert.Equal((12.5, 15.0), (P(g).X, P(g).Y));
        g.Put(0, 0, 5.5, 15);
        Press(g, Button);
        Press(g, Up);
        Assert.Equal((20.5, 5.0), (P(g).X, P(g).Y));
    }

    // ───────────────────────────── the monsters ─────────────────────────────

    [Fact]
    public void Touching_the_ankh_raises_the_mummy_which_walks_its_floor_after_the_player()
    {
        var r0 = Plain().Floor(10, 0, 23).Ladder(20, 10, 15).Thing('u', 2, 15, "tomb").Thing('s', 6, 15, "spawn").Thing('n', 9, 15, "ankh", "u");
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Run(60, Right);
        var m = Assert.Single(g.Monsters);
        Assert.Equal(MonsterKind.Mummy, m.Kind);
        g.Run(60 * 3, Right);
        g.Put(0, 0, 20.5, 10);
        g.Run(60 * 10, default);
        Assert.Equal(15.0, m.Y);
        Assert.True(P(g).Active);
        // And it catches whoever is on its floor.
        g.Put(0, 0, m.X + 2, 15);
        for (int i = 0; i < 60 * 5 && P(g).Active; i++) g.Tick(default);
        Assert.Equal("caught", g.Cause);
    }

    [Fact]
    public void The_monster_wakes_as_the_player_passes_follows_by_ladder_and_drops_through_an_open_trapdoor()
    {
        var r0 = Plain().Floor(10, 0, 23).Cells(10, 14, 16, 'T').Ladder(4, 10, 15).Thing('s', 6, 15, "spawn").Thing('c', 10, 15, "coffin", "left").Thing('t', 19, 10, "trapswitch", 1);
        var g = Game(TestRoom.Castle("0 s", r0));
        g.Tick(default);
        var m = Assert.Single(g.Monsters);
        g.Put(0, 0, 4.5, 15);
        g.Run(60 * 2, Up);
        g.Run(60, Right);
        g.Put(0, 0, 19.5, 10);
        for (int i = 0; i < 60 * 20 && !(m.X > 14.6 && m.X < 16.4 && m.Y < 11); i++) g.Tick(default);
        Assert.True(m.Y < 11);
        Press(g, Button);
        g.Run(60, default);
        Assert.True(m.Dead);
        Assert.Equal(GameEngine.MonsterPoints, g.Score);
    }

    // ───────────────────────────── lives, escaping, the game ─────────────────────────────

    [Fact]
    public void A_lost_life_starts_the_player_again_at_the_door_they_came_in_and_the_last_ends_the_game()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('z', 10, 15, "bolt", 3);
        var g = Game(TestRoom.Castle("0 s", r0));
        for (int life = 3; life > 0; life--)
        {
            g.Put(0, 0, 8.5, 15);
            for (int i = 0; i < 60 * 3 && P(g).Active; i++) g.Tick(Right);
            Assert.Equal(life - 1, P(g).Lives);
            g.Run((int)(GameEngine.DeadSeconds * 60) + 2, default);
            if (life > 1) Assert.Equal((Pose.Walking, 5.5), (P(g).Pose, P(g).X));
        }
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Out_through_the_way_out_is_the_castle_escaped_with_a_time_a_best_time_and_on_to_the_next()
    {
        var a = TestRoom.Castle("0 s", Plain().Thing('s', 5, 15, "spawn").Thing('e', 10, 15, "exit", "open"));
        var b = TestRoom.Castle("0 s", Plain().Thing('s', 3, 15, "spawn").Thing('e', 20, 15, "exit"));
        var g = Game(a, b);
        g.Put(0, 0, 10.5, 15);
        Press(g, Up);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.NotNull(g.Best.Of("Test"));
        Assert.True(g.Score >= GameEngine.CastlePoints);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal(1, g.CastleIndex);
        Assert.Equal((Pose.Walking, 3.5), (P(g).Pose, P(g).X));
    }

    [Fact]
    public void With_two_players_each_has_their_own_controls_and_both_must_get_out()
    {
        var room = Plain().Thing('s', 5, 15, "spawn").Thing('e', 10, 15, "exit", "open");
        var g = Game(2, TestRoom.Castle("0 s", room));
        Assert.Equal(2, g.Players.Count);
        g.Run(30, Right, default);
        Assert.True(P(g).X > 6.5 && P(g, 1).X < 6);
        g.Put(0, 0, 10.5, 15);
        Press(g, Up);
        Assert.Equal((Pose.Escaped, Phase.Play), (P(g).Pose, g.Phase));
        g.Put(1, 0, 10.5, 15);
        Press(g, Up, player: 1);
        Assert.Equal(Phase.Clear, g.Phase);
    }

    [Fact]
    public void The_title_chooses_a_castle_and_starts_one_or_two_players()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(Down);
        g.Tick(default);
        g.Tick(Down);
        Assert.Equal(2, g.Choice);
        g.Tick(default, default, start: false, start2: true);
        Assert.Equal((GameState.Playing, 2, 2), (g.State, g.CastleIndex, g.Players.Count));
    }

    [Fact]
    public void Left_alone_the_title_shows_the_tutorial_walked_and_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal((GameState.Attract, "Tutorial"), (g.State, g.Castle.Name));
        for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Null(g.Best.Of("Tutorial"));
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void A_high_score_asks_for_initials()
    {
        var r0 = Plain().Thing('s', 5, 15, "spawn").Thing('z', 10, 15, "bolt", 3);
        var g = Game(TestRoom.Castle("0 s", r0));
        g.SetScore(99999);
        for (int life = 0; life < 3; life++)
        {
            g.Put(0, 0, 8.5, 15);
            for (int i = 0; i < 60 * 3 && P(g).Active; i++) g.Tick(Right);
            g.Run((int)(GameEngine.DeadSeconds * 60) + 2, default);
        }
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "DRC") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(("DRC", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void One_player_has_every_key_two_players_their_own()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Avalonia.Input.Key.W);
        input.OnKeyDown(Avalonia.Input.Key.Space);
        Assert.Equal(new Controls(0, -1, true), input.Player1);
        input.TwoPlayers = true;
        Assert.Equal(new Controls(0, -1, true), input.Player2);
        Assert.Equal(default, input.Player1);
        input.OnKeyDown(Avalonia.Input.Key.Left);
        input.OnKeyDown(Avalonia.Input.Key.RightCtrl);
        Assert.Equal(new Controls(-1, 0, true), input.Player1);
    }
}
