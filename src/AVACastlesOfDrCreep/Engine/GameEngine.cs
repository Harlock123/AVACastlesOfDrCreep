namespace AVACastlesOfDrCreep.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where a castle has got to.</summary>
public enum Phase
{
    /// <summary>The castle's name, the players at the door.</summary>
    Start,
    /// <summary>Finding the way out.</summary>
    Play,
    /// <summary>Everyone still standing is out: the time, then the next castle.</summary>
    Clear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Door, Bell, Key, Unlock, Switch, Trap, Walkway, FieldOff, Zap, Ray, Teleport, MummyRise, MonsterWake, Fall, Die, Escape,
    Welcome, LevelStart, LevelClear,
    /// <summary>The music, looping.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and both players' controls; it runs
/// in fixed 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;

    private double _accumulator, _idle;
    private bool _startPrev, _start2Prev, _upPrev, _downPrev;
    private bool _welcomePending = true, _tunePending = true;
    /// <summary>How long the spoken welcome lasts before the title music starts.</summary>
    public const double WelcomeSeconds = 6.5;
    private bool _music;
    /// <summary>A new game opens on the spoken welcome; the castle music waits until it's said.</summary>
    private bool _greet;
    private double _musicAfter;

    /// <summary>A game of the castles given as text (the game's own, unless a test brings its own).</summary>
    public GameEngine(HighScoreTable? table = null, IReadOnlyList<string>? castles = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        CastleTexts = castles ?? CastleBook.Texts;
        Castles = CastleTexts.Select(Castle.Parse).ToList();
        Best = BestTimes.Load(HighScores.Folder);
        NewGame(0, 1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public BestTimes Best { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    /// <summary>The castle picked on the title screen to start from.</summary>
    public int Choice { get; private set; }
    /// <summary>The castle map is up (the game goes on behind it).</summary>
    public bool ShowMap { get; set; }

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls one, Controls two, bool start, bool start2)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(one, two, start, start2);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls one, Controls two = default, bool start = false, bool start2 = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev, start2Pressed = start2 && !_start2Prev;
        _startPrev = start;
        _start2Prev = start2;
        bool up = one.Dy < 0 && !_upPrev, down = one.Dy > 0 && !_downPrev;
        _upPrev = one.Dy < 0;
        _downPrev = one.Dy > 0;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_welcomePending) { _welcomePending = false; Play(Sfx.Welcome); }
                if (_tunePending && StateTime > WelcomeSeconds) { _tunePending = false; Play(Sfx.TitleTune); }
                if (up) { Choice = (Choice + Castles.Count - 1) % Castles.Count; _idle = 0; }
                if (down) { Choice = (Choice + 1) % Castles.Count; _idle = 0; }
                _idle += dt;
                if (startPressed) StartGame(Choice, 1);
                else if (start2Pressed) StartGame(Choice, 2);
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this), default);
                if (startPressed) { StartGame(Choice, 1); break; }
                if (start2Pressed) { StartGame(Choice, 2); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                if (_musicAfter > 0 && (_musicAfter -= dt) <= 0) Music(true);
                Simulate(dt, one, two);
                break;
            case GameState.GameOver:
                if (StateTime > 3.5)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Escaped);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame(Choice, 1);
                else if (start2Pressed) StartGame(Choice, 2);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>A game from a castle, for one player or two.</summary>
    public void StartGame(int castle, int players)
    {
        NewGame(castle, players);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        ShowMap = false;
        _greet = true;
        Enter(castle);
    }

    /// <summary>The demonstration: the computer walks the tutorial castle the way its solution says.</summary>
    private void StartAttract()
    {
        NewGame(0, 1);
        State = GameState.Attract;
        StateTime = 0;
        ShowMap = false;
        Pilot.Reset(Castles[0]);
        Enter(0);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _welcomePending = _tunePending = true;
        ShowMap = false;
        Music(false);
        Effects.Clear();
        Message = SubMessage = null;
        NewGame(Choice, 1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase is Phase.Play or Phase.Start) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        _musicAfter = 0;
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
