namespace AVACastlesOfDrCreep.Engine;

public sealed partial class GameEngine
{
    public const double WalkSpeed = 4.2, ClimbSpeed = 3.2, SlideSpeed = 7, Gravity = 40, MaxFall = 3.2, CarrySpeed = 2.4;
    public const double MummySpeed = 1.8, MonsterSpeed = 1.6, MonsterClimb = 1.8, RaySlide = 1.2, RayGap = 1.6, FieldOffSeconds = 6;
    public const double StartSeconds = 2.6, ClearSeconds = 4.5, DeadSeconds = 2.2, SafeSeconds = 1.2, Reach = 0.6;
    public const int KeyPoints = 100, MonsterPoints = 500, CastlePoints = 1000, SecondPoints = 10;

    /// <summary>The castles, as written, and the one being played (a fresh copy each time in).</summary>
    public IReadOnlyList<string> CastleTexts { get; private set; } = [];
    public IReadOnlyList<Castle> Castles { get; private set; } = [];
    public Castle Castle { get; private set; } = new();
    public int CastleIndex { get; private set; }
    public int Score { get; private set; }
    /// <summary>Castles escaped this game.</summary>
    public int Escaped { get; private set; }
    public List<Player> Players { get; } = [];
    public List<Monster> Monsters { get; } = [];
    /// <summary>The keys the players hold between them, by colour.</summary>
    public HashSet<string> Keys { get; } = [];
    /// <summary>How long they've been in this castle.</summary>
    public double Clock { get; private set; }
    public RoutePilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";

    private void NewGame(int castle, int players)
    {
        Score = 0;
        Escaped = 0;
        CastleIndex = castle;
        Players.Clear();
        for (int i = 0; i < players; i++) Players.Add(new Player { Index = i });
        Castle = Castle.Parse(CastleTexts[castle]);
        Phase = Phase.Start;
    }

    /// <summary>Into a castle: a fresh copy of it, the keys gone, everyone still in the game at the door.</summary>
    private void Enter(int index)
    {
        CastleIndex = index;
        Castle = Castle.Parse(CastleTexts[index]);
        Keys.Clear();
        Monsters.Clear();
        Clock = 0;
        foreach (var p in Players.Where(p => p.Pose != Pose.Out))
        {
            p.EntryRoom = Castle.StartRoom;
            p.EntryAt = Castle.StartAt;
            Arrive(p, Castle.StartRoom, Castle.StartAt);
        }
        SetPhase(Phase.Start);
        Say(Castle.Name.ToUpperInvariant(), "FIND THE WAY OUT", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    /// <summary>A player set down at a thing in a room (a door, say), standing.</summary>
    private void Arrive(Player p, int room, char at)
    {
        var r = Castle.Room(room);
        var t = r.Find(at)!;
        p.Room = room;
        p.X = t.Middle + (Players.Count > 1 ? (p.Index == 0 ? -0.35 : 0.35) * (t.Kind is Kind.Door or Kind.Exit or Kind.Spawn ? 1 : 0) : 0);
        p.Y = t.Feet;
        p.Pose = Pose.Walking;
        p.Vy = 0;
        p.Safe = SafeSeconds;
        r.Seen = true;
    }

    private void Simulate(double dt, Controls one, Controls two)
    {
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Clock += dt;
                for (int i = 0; i < Players.Count; i++) Move(Players[i], i == 0 ? one : two, dt);
                foreach (int room in Players.Where(p => p.Active).Select(p => p.Room).Distinct().ToList()) Work(Castle.Room(room), dt);
                foreach (var p in Players.Where(p => p.Active && p.Safe <= 0)) Hazards(p);
                Monsters.RemoveAll(m => m.Dead);
                if (Players.All(p => p.Pose is Pose.Out)) { Say("GAME OVER", null, 3); GameOver(); return; }
                if (Players.All(p => p.Pose is Pose.Escaped or Pose.Out)) Out();
                break;
            case Phase.Clear:
                if (PhaseTime >= ClearSeconds)
                {
                    if (CastleIndex + 1 < CastleTexts.Count) Enter(CastleIndex + 1);
                    else { Say("EVERY CASTLE ESCAPED!", null, 4); GameOver(); return; }
                }
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the players ─────────────────────────────

    private void Move(Player p, Controls c, double dt)
    {
        bool fire = c.Fire && !p.FirePrev, up = c.Dy < 0 && !p.UpPrev;
        p.Dy = c.Dy;
        p.FirePrev = c.Fire;
        p.UpPrev = c.Dy < 0;
        if (p.Pose == Pose.Dead)
        {
            if ((p.DeadTime -= dt) > 0) return;
            if (p.Lives <= 0) { p.Pose = Pose.Out; return; }
            Arrive(p, p.EntryRoom, p.EntryAt);
            return;
        }
        if (!p.Active) return;
        p.Safe = Math.Max(0, p.Safe - dt);
        var room = Castle.Room(p.Room);
        switch (p.Pose)
        {
            case Pose.Walking:
                Walk(p, room, c, fire, up, dt);
                break;
            case Pose.Climbing:
                ClimbLadder(p, room, c, dt);
                break;
            case Pose.Sliding:
                SlidePole(p, room, dt);
                break;
            case Pose.Falling:
                Drop(p, room, dt);
                break;
        }
    }

    private void Walk(Player p, Room room, Controls c, bool fire, bool up, double dt)
    {
        int r = (int)Math.Round(p.Y);
        p.Y = r;
        if (!room.Floor(p.Col, r)) { Fall(p); return; }
        int col = p.Col;
        double mid = col + 0.5;
        // Onto a ladder or pole, when near enough the middle of it.
        if (c.Dy != 0 && Math.Abs(p.X - mid) < 0.45 && !Manning(p, room))
        {
            if (c.Dy < 0 && room.Ladder(col, r - 1) || c.Dy > 0 && room.Ladder(col, r)) { p.X = mid; p.Pose = Pose.Climbing; return; }
            if (c.Dy > 0 && room.Pole(col, r)) { p.X = mid; p.Pose = Pose.Sliding; Play(Sfx.Fall); return; }
        }
        if (up && Through(p, room)) return;
        if (fire) Act(p, room);
        double dx = c.Dx * WalkSpeed * dt;
        if (room.WalkAt(col, r) is { Dir: not 0 } w) dx += w.Dir * CarrySpeed * dt;
        if (c.Dx != 0) { p.Facing = c.Dx; p.Walk += dt; }
        if (dx != 0) p.X = Step(room, p.X, r, dx, Player.HalfWidth, Player.Height);
        Touch(p, room);
        if (!room.Floor(p.Col, r)) Fall(p);
    }

    /// <summary>Across a floor by dx, kept inside the room and stopped by any force field that's up.</summary>
    private static double Step(Room room, double x, double feet, double dx, double half, double height)
    {
        double nx = Math.Clamp(x + dx, half, Field.Cols - half);
        foreach (var f in room.Things.Where(t => t.Kind == Kind.Field && t.On))
        {
            double top = f.Y - int.Parse(f.Arg(0, "3")) + 1, bottom = f.Y + 1;
            if (feet - height >= bottom || feet <= top) continue;
            double wall = f.Middle;
            if (x <= wall && nx + half > wall) nx = wall - half - 0.01;
            else if (x >= wall && nx - half < wall) nx = wall + half + 0.01;
        }
        return nx;
    }

    private void ClimbLadder(Player p, Room room, Controls c, double dt)
    {
        int col = p.Col;
        int at = (int)Math.Round(p.Y);
        bool onRung = Math.Abs(p.Y - at) < 0.05;
        // Off the ladder at a floor, sideways.
        if (c.Dx != 0 && onRung && room.Floor(col, at)) { p.Y = at; p.Pose = Pose.Walking; return; }
        if (c.Dy == 0) return;
        double ny = p.Y + c.Dy * ClimbSpeed * dt;
        if (c.Dy < 0)
        {
            // Up: the feet can only rise into a square with ladder in it.
            for (int k = (int)Math.Floor(p.Y - 1e-9); k > ny - 1e-9; k--)
                if (!room.Ladder(col, k - 1)) { ny = k; break; }
        }
        else
        {
            for (int k = (int)Math.Ceiling(p.Y + 1e-9); k <= ny + 1e-9; k++)
                if (!room.Ladder(col, k)) { ny = k; break; }
        }
        p.Walk += dt;
        p.Y = ny;
        // At the top or bottom, standing on a floor: off the ladder.
        int now = (int)Math.Round(p.Y);
        if (Math.Abs(p.Y - now) < 1e-6 && room.Floor(col, now) && (c.Dy < 0 ? !room.Ladder(col, now - 1) : !room.Ladder(col, now))) p.Pose = Pose.Walking;
    }

    private void SlidePole(Player p, Room room, double dt)
    {
        double ny = p.Y + SlideSpeed * dt;
        for (int k = (int)Math.Ceiling(p.Y - 1e-9); k <= ny; k++)
        {
            if (k < p.Y - 1e-9 || room.Pole(p.Col, k)) continue;
            p.Y = k;
            if (room.Floor(p.Col, k)) p.Pose = Pose.Walking;
            else Fall(p);
            return;
        }
        p.Y = ny;
    }

    private static void Fall(Player p)
    {
        p.Pose = Pose.Falling;
        p.Vy = 0;
        p.FallFrom = p.Y;
    }

    private void Drop(Player p, Room room, double dt)
    {
        p.Vy += Gravity * dt;
        double ny = p.Y + p.Vy * dt;
        for (int k = (int)Math.Floor(p.Y) + 1; k <= ny; k++)
            if (room.Floor(p.Col, k))
            {
                p.Y = k;
                if (k - p.FallFrom > MaxFall) Die(p, "fell");
                else p.Pose = Pose.Walking;
                return;
            }
        p.Y = ny;
        if (p.Y > Field.Rows + 2) Die(p, "fell");
    }

    /// <summary>A door the player is at, or the booth: up goes through (if it's open).</summary>
    private bool Through(Player p, Room room)
    {
        var t = room.Things.FirstOrDefault(t => t.Kind is Kind.Door or Kind.Exit or Kind.Booth && Near(p, t));
        if (t == null) return false;
        if (t.Kind == Kind.Booth)
        {
            var to = Receivers(room, t).ElementAtOrDefault(t.Selected);
            if (to == null) return false;
            p.X = to.Middle;
            p.Y = to.Feet;
            p.Safe = SafeSeconds * 0.5;
            Effects.Add(new Effect { Kind = EffectKind.Zap, Room = room.Id, X = to.Middle, Y = to.Feet - 1, Life = 0.5 });
            Play(Sfx.Teleport);
            return true;
        }
        if (!t.Open) return false;
        if (t.Kind == Kind.Exit)
        {
            p.Pose = Pose.Escaped;
            Play(Sfx.Escape);
            return true;
        }
        int to2 = int.Parse(t.Arg(0));
        char at = t.Arg(1)[0];
        var other = Castle.Room(to2).Find(at)!;
        other.Open = true;
        p.EntryRoom = to2;
        p.EntryAt = at;
        Arrive(p, to2, at);
        Play(Sfx.Door);
        return true;
    }

    public static IEnumerable<Thing> Receivers(Room room, Thing booth) =>
        room.Things.Where(r => r.Kind == Kind.Receiver && r.Arg(0) == booth.Letter.ToString());

    private static bool Near(Player p, Thing t) => Math.Abs(p.X - t.Middle) < Reach && Math.Abs(p.Y - t.Feet) < 0.1;

    /// <summary>Whether the player is standing at a ray gun's control (so up and down work the gun, not ladders).</summary>
    private static bool Manning(Player p, Room room) => room.Things.Any(t => t.Kind == Kind.RayBox && Near(p, t));

    /// <summary>The action button: whatever control the player is standing at.</summary>
    private void Act(Player p, Room room)
    {
        var t = room.Things.FirstOrDefault(t => Near(p, t) && t.Kind is Kind.Bell or Kind.Lock or Kind.TrapSwitch or Kind.WalkControl or Kind.FieldButton or Kind.BoltSwitch or Kind.RayBox or Kind.Booth);
        if (t == null) return;
        switch (t.Kind)
        {
            case Kind.Bell:
                OpenDoor(room.Find(t.Arg(0)[0])!);
                Play(Sfx.Bell);
                break;
            case Kind.Lock:
                if (Keys.Contains(t.Arg(0)))
                {
                    OpenDoor(room.Find(t.Arg(1)[0])!);
                    Play(Sfx.Unlock);
                }
                else Effects.Add(new Effect { Kind = EffectKind.Word, Room = room.Id, X = t.Middle, Y = t.Y - 0.5, Text = $"NEEDS THE {t.Arg(0).ToUpperInvariant()} KEY", Life = 1.4, Colour = 0xFFFFB0A0 });
                break;
            case Kind.TrapSwitch:
                var trap = room.Traps[int.Parse(t.Arg(0)) - 1];
                trap.Open = !trap.Open;
                Play(Sfx.Trap);
                break;
            case Kind.WalkControl:
                var w = room.Walkways[int.Parse(t.Arg(0)) - 1];
                if (w.Dir != 0) { w.Last = w.Dir; w.Dir = 0; }
                else w.Dir = -w.Last;
                Play(Sfx.Walkway);
                break;
            case Kind.FieldButton:
                var f = room.Find(t.Arg(0)[0])!;
                f.On = false;
                f.Timer = FieldOffSeconds;
                Play(Sfx.FieldOff);
                break;
            case Kind.BoltSwitch:
                var b = room.Find(t.Arg(0)[0])!;
                b.On = !b.On;
                Play(Sfx.Switch);
                break;
            case Kind.RayBox:
                var ray = room.Find(t.Arg(0)[0])!;
                if (ray.Cooldown <= 0) Fire(room, ray, p);
                break;
            case Kind.Booth:
                int n = Receivers(room, t).Count();
                if (n > 0) t.Selected = (t.Selected + 1) % n;
                Play(Sfx.Switch);
                break;
        }
    }

    private void OpenDoor(Thing door)
    {
        if (door.Open) return;
        door.Open = true;
        Play(Sfx.Door);
    }

    /// <summary>What a player picks up by walking over it: a key; and an ankh, which wakes the mummy in its tomb.</summary>
    private void Touch(Player p, Room room)
    {
        foreach (var t in room.Things.Where(t => !t.Used && Near(p, t)))
        {
            if (t.Kind == Kind.Key)
            {
                t.Used = true;
                Keys.Add(t.Arg(0));
                Award(KeyPoints, room.Id, t.Middle, t.Y);
                Play(Sfx.Key);
            }
            else if (t.Kind == Kind.Ankh && room.Find(t.Arg(0)[0]) is { } tomb && !tomb.Used)
            {
                t.Used = tomb.Used = true;
                Monsters.Add(new Monster { Kind = MonsterKind.Mummy, Room = room.Id, X = tomb.Middle, Y = tomb.Feet, Facing = p.X < tomb.Middle ? -1 : 1 });
                Play(Sfx.MummyRise);
            }
        }
    }

    // ───────────────────────────── the rooms at work ─────────────────────────────

    /// <summary>A room with a player in it: force fields coming back on, ray guns, the monster waking, monsters moving.</summary>
    private void Work(Room room, double dt)
    {
        foreach (var t in room.Things)
        {
            switch (t.Kind)
            {
                case Kind.Field when !t.On:
                    if ((t.Timer -= dt) <= 0) t.On = true;
                    break;
                case Kind.Ray:
                    Gun(room, t, dt);
                    break;
                case Kind.Coffin when !t.Used:
                    if (Players.Any(p => p.Active && p.Room == room.Id && Math.Abs(p.Y - t.Feet) < 0.1 && Math.Abs(p.X - t.Middle) < 7))
                    {
                        t.Used = true;
                        Monsters.Add(new Monster { Kind = MonsterKind.Monster, Room = room.Id, X = t.Middle, Y = t.Feet, Facing = t.Facing });
                        Play(Sfx.MonsterWake);
                    }
                    break;
            }
        }
        foreach (var m in Monsters.Where(m => m.Room == room.Id && !m.Dead).ToList()) Stalk(room, m, dt);
    }

    /// <summary>
    /// A ray gun on its rail: worked by whoever stands at its control (up and down move it, the button fires);
    /// left alone it slides towards the nearest player's height and fires when it's level.
    /// </summary>
    private void Gun(Room room, Thing gun, double dt)
    {
        gun.Cooldown -= dt;
        gun.Beam = Math.Max(0, gun.Beam - dt);
        double top = int.Parse(gun.Arg(0)), bottom = gun.Y;
        var box = room.Things.FirstOrDefault(b => b.Kind == Kind.RayBox && b.Arg(0) == gun.Letter.ToString());
        var man = box == null ? null : Players.FirstOrDefault(p => p.Active && p.Room == room.Id && p.Pose == Pose.Walking && Near(p, box));
        if (man != null)
        {
            gun.Aim = Math.Clamp(gun.Aim + man.Dy * RaySlide * dt, top, bottom);
            return;
        }
        var target = Players.Where(p => p.Active && p.Room == room.Id && (p.X - gun.Middle) * gun.Facing > 0)
            .OrderBy(p => Math.Abs(p.Y - 1 - gun.Aim)).FirstOrDefault();
        if (target == null) return;
        double want = Math.Clamp(target.Y - 1.4, top, bottom);
        gun.Aim += Math.Clamp(want - gun.Aim, -RaySlide * dt, RaySlide * dt);
        if (Math.Abs(gun.Aim - want) < 0.3 && gun.Cooldown <= 0) Fire(room, gun);
    }

    /// <summary>The ray: straight across the room the way the gun faces, at its height. It kills whoever it meets (but not whoever fired it).</summary>
    private void Fire(Room room, Thing gun, Player? shooter = null)
    {
        gun.Cooldown = RayGap;
        gun.Beam = 0.25;
        double y = gun.Aim + 0.5;
        Play(Sfx.Ray);
        foreach (var p in Players.Where(p => p != shooter && p.Active && p.Room == room.Id && p.Safe <= 0 && (p.X - gun.Middle) * gun.Facing > 0 && y > p.Y - Player.Height && y < p.Y))
            Die(p, "rayed");
        foreach (var m in Monsters.Where(m => m.Room == room.Id && !m.Dead && (m.X - gun.Middle) * gun.Facing > 0 && y > m.Y - Monster.Height && y < m.Y))
            Kill(m);
    }

    /// <summary>Lightning and monsters: what kills a player where they stand.</summary>
    private void Hazards(Player p)
    {
        var room = Castle.Room(p.Room);
        if (Zapped(room, p.X, p.Y, Player.HalfWidth, Player.Height)) { Die(p, "zapped"); return; }
        if (Monsters.Any(m => m.Room == p.Room && !m.Dead && Math.Abs(m.X - p.X) < 0.55 && Math.Abs(m.Y - p.Y) < 1.2)) Die(p, "caught");
    }

    private static bool Zapped(Room room, double x, double feet, double half, double height) =>
        room.Things.Any(b => b.Kind == Kind.Bolt && b.On && Math.Abs(x - b.Middle) < half + 0.25 && feet > b.Y - int.Parse(b.Arg(0, "3")) + 1 && feet - height < b.Y + 1);

    // ───────────────────────────── the monsters ─────────────────────────────

    /// <summary>
    /// The mummy walks the floor it rose on, towards the nearest player, and no further than the floor goes. The
    /// monster goes anywhere a player can, by ladder and pole too, after whoever is nearest. Both stop at force
    /// fields; lightning, the ray guns and an open trapdoor put an end to them.
    /// </summary>
    private void Stalk(Room room, Monster m, double dt)
    {
        if (Zapped(room, m.X, m.Y, Monster.HalfWidth, Monster.Height)) { Kill(m); return; }
        int r = (int)Math.Round(m.Y);
        // A trapdoor opened under it: down it goes, and that's the end of it.
        if (m.Climb == 0 && m.Fall == 0 && !room.Floor((int)m.X, r)) m.Fall = 0.01;
        if (m.Fall > 0)
        {
            m.Fall += dt;
            m.Y += Gravity * m.Fall * dt;
            if (m.Fall > 0.45) Kill(m);
            return;
        }
        var target = Players.Where(p => p.Active && p.Room == room.Id).OrderBy(p => Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y) * 2).FirstOrDefault();
        if (target == null) return;
        if (m.Kind == MonsterKind.Mummy)
        {
            int dir = Math.Sign(target.X - m.X);
            if (dir == 0 || Math.Abs(target.X - m.X) < 0.1) return;
            m.Facing = dir;
            double nx = Step(room, m.X, r, dir * MummySpeed * dt, Monster.HalfWidth, Monster.Height);
            // Not off the end of its floor.
            if (room.Floor((int)Math.Floor(nx + dir * 0.3), r)) { m.X = nx; m.Walk += dt; }
            return;
        }
        if (m.Climb != 0)
        {
            int col = (int)m.X;
            int dir = Math.Sign(m.Climb);
            double ny = m.Y + dir * (m.Climb == 2 ? SlideSpeed * 0.6 : MonsterClimb) * dt;
            bool Can(int k) => m.Climb == 2 ? room.Pole(col, k) : dir < 0 ? room.Ladder(col, k - 1) : room.Ladder(col, k);
            for (int k = dir < 0 ? (int)Math.Floor(m.Y - 1e-9) : (int)Math.Ceiling(m.Y + 1e-9); dir < 0 ? k > ny - 1e-9 : k <= ny + 1e-9; k += dir)
                if (!Can(k)) { ny = k; m.Climb = 0; break; }
            m.Y = ny;
            m.Walk += dt;
            return;
        }
        var next = Route(room, (int)m.X, r, target.Col, (int)Math.Round(target.Y));
        // No way round to the player: it comes straight on anyway (and may walk into an open trapdoor).
        if (next is not { } go)
        {
            if (Math.Abs(target.Y - m.Y) > 0.5 || Math.Abs(target.X - m.X) < 0.1) return;
            go = (Math.Sign(target.X - m.X), 0);
        }
        if (go.Climb != 0)
        {
            double mid = (int)m.X + 0.5;
            if (Math.Abs(m.X - mid) > 0.05) { m.X += Math.Clamp(mid - m.X, -MonsterSpeed * dt, MonsterSpeed * dt); m.Walk += dt; return; }
            m.X = mid;
            m.Climb = go.Climb;
            return;
        }
        m.Facing = go.Dx;
        m.X = Step(room, m.X, r, go.Dx * MonsterSpeed * dt, Monster.HalfWidth, Monster.Height);
        m.Walk += dt;
    }

    /// <summary>
    /// The first move on the shortest way from one floor square to another, going the ways a player can: along a
    /// floor (not through a force field that's up), up or down a ladder, down a pole. Climb is -1 up, 1 down a
    /// ladder, 2 down a pole.
    /// </summary>
    public static (int Dx, int Climb)? Route(Room room, int c0, int r0, int c1, int r1)
    {
        if (c0 == c1 && r0 == r1) return null;
        var first = new Dictionary<(int, int), (int, int)>();
        var todo = new Queue<(int C, int R)>();
        first[(c0, r0)] = (0, 0);
        todo.Enqueue((c0, r0));
        while (todo.Count > 0)
        {
            var (c, r) = todo.Dequeue();
            foreach (var (nc, nr, move) in Moves(room, c, r))
            {
                if (first.ContainsKey((nc, nr))) continue;
                first[(nc, nr)] = (c, r) == (c0, r0) ? move : first[(c, r)];
                if ((nc, nr) == (c1, r1)) return first[(nc, nr)];
                todo.Enqueue((nc, nr));
            }
        }
        return null;
    }

    private static IEnumerable<(int C, int R, (int Dx, int Climb) Move)> Moves(Room room, int c, int r)
    {
        foreach (int d in new[] { -1, 1 })
        {
            int nc = c + d;
            // A force field that's up stands in the middle of its square: nothing goes into or out of it.
            if (room.Floor(nc, r) && !room.Things.Any(f => f.Kind == Kind.Field && f.On && (f.X == nc || f.X == c) && r > f.Y - int.Parse(f.Arg(0, "3")) + 1 && r - 2 <= f.Y))
                yield return (nc, r, (d, 0));
        }
        if (room.Ladder(c, r - 1))
        {
            int k = r;
            while (room.Ladder(c, k - 1)) k--;
            if (room.Floor(c, k)) yield return (c, k, (0, -1));
        }
        if (room.Ladder(c, r))
        {
            int k = r;
            while (room.Ladder(c, k)) k++;
            if (room.Floor(c, k)) yield return (c, k, (0, 1));
        }
        if (room.Pole(c, r))
        {
            int k = r;
            while (room.Pole(c, k)) k++;
            if (room.Floor(c, k)) yield return (c, k, (0, 2));
        }
    }

    private void Kill(Monster m)
    {
        if (m.Dead) return;
        m.Dead = true;
        Effects.Add(new Effect { Kind = EffectKind.Puff, Room = m.Room, X = m.X, Y = m.Y - 1, Life = 0.8 });
        Award(MonsterPoints, m.Room, m.X, m.Y - 2);
        Play(Sfx.Zap);
    }

    // ───────────────────────────── losing and escaping ─────────────────────────────

    private void Die(Player p, string cause)
    {
        if (!p.Active) return;
        Cause = cause;
        p.Pose = Pose.Dead;
        p.Lives--;
        p.DeadTime = DeadSeconds;
        Effects.Add(new Effect { Kind = EffectKind.Puff, Room = p.Room, X = p.X, Y = p.Y - 1, Life = 0.9 });
        Play(Sfx.Die);
    }

    /// <summary>Everyone still in the game is out of the castle: the time, the best time, the points.</summary>
    private void Out()
    {
        Escaped++;
        bool best = State == GameState.Playing && Best.Record(Castle.Name, Clock);
        int bonus = CastlePoints * (CastleIndex + 1) + Math.Max(0, (int)(Castle.Par - Clock)) * SecondPoints;
        Score += bonus;
        Play(Sfx.LevelClear);
        Say("ESCAPED!", $"{Clock:0.0} SECONDS{(best ? " - A NEW BEST!" : "")}   BONUS {bonus}", ClearSeconds);
        Music(false);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, int room, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, Room = room, X = x, Y = y, Text = points.ToString(), Life = 0.9 });
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls one, Controls two = default)
    {
        for (int i = 0; i < ticks; i++) Tick(one, two);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a castle, past its name.</summary>
    internal void Go(int castle = 0, int players = 1)
    {
        StartGame(castle, players);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>A player set down in a room, standing at a square.</summary>
    internal void Put(int player, int room, double x, double y)
    {
        var p = Players[player];
        p.Room = room;
        p.X = x;
        p.Y = y;
        p.Pose = Pose.Walking;
        p.Safe = 0;
        Castle.Room(room).Seen = true;
    }

    internal Monster PutMonster(MonsterKind kind, int room, double x, double y)
    {
        var m = new Monster { Kind = kind, Room = room, X = x, Y = y };
        Monsters.Add(m);
        return m;
    }

    internal void GiveKey(string colour) => Keys.Add(colour);
    internal void SetScore(int n) => Score = n;
}
