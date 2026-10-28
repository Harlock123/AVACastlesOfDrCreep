# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game and its controls;
this file covers how the code is put together and the rules that keep it working.

## What this is

A puzzle-platform game in the style of Ed Hobbs's 1984 Commodore 64 game The Castles of Dr. Creep, for one or two
players, in C# on .NET 10 and Avalonia 12, drawn with Avalonia's `DrawingContext`. One self-contained executable
per platform (Windows, macOS and Linux, x64 and ARM). The castles are built by `tools/make_castles.py`, and the
sounds, tunes and spoken welcome are synthesised by `tools/make_sounds.py` (with `tools/speech.py`).

## Commands

```bash
dotnet build AVACastlesOfDrCreep.slnx             # the SDK version is in global.json (10.0.400 or later)
dotnet test AVACastlesOfDrCreep.slnx              # every test runs headless: no window opens
dotnet run --project src/AVACastlesOfDrCreep      # play it; --no-audio for silence
DRCREEP_SHOTS=$PWD/Images dotnet test tests/AVACastlesOfDrCreep.Tests --filter ScreenshotTests   # refresh the README's pictures
python3 tools/make_castles.py                     # rebuild src/AVACastlesOfDrCreep/Castles (standard-library Python only)
python3 tools/make_sounds.py                      # rebuild src/AVACastlesOfDrCreep/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVACastlesOfDrCreep/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores).
  - `GameEngine.Play.cs` has the fixed-step loop and the rooms in play: walking, ladders, poles, trapdoors,
    walkways, the machines and the monsters.
  - `Entities.cs` has `Controls`, the room grid (`Field`, 24 by 16 squares) and the castle model, including the
    parser for `.castle` files (`Castle`, `Room`, `Thing`).
  - `CastleBook.cs` loads the castles, which are embedded resources, in order (the tutorial first).
  - `RoutePilot.cs` is the computer player: it follows the way out written at the end of each castle file. The
    demonstration and the tests use it.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads, two of them) produce one `Controls` per
    player.
  - `HighScoreTable.cs`, `BestTimes.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVACastlesOfDrCreep/Castles/`: the generated `.castle` files. Change the designs in
  `tools/make_castles.py`, not here.
- `src/AVACastlesOfDrCreep/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz
  timer.
- `src/AVACastlesOfDrCreep/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVACastlesOfDrCreep.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `CastleTests.cs` reads every castle and escapes it by its written way out.
  - `GameplayTests.cs` uses small castles built in code by `TestCastle.cs`.
  - `ScreenshotTests.cs` draws the README's pictures. `ShiftReleaseTests.cs` covers the Shift key quirk below.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the game in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate. The game uses no random numbers at all, so a castle always plays the same way. Keep it that way:
  the written ways out and `CastleTests` depend on it.
- **Every castle must stay escapable.** Each castle file ends with its way out. If you change a room in
  `tools/make_castles.py`, update its way out too, and run `CastleTests`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Keys, D-pad and stick must produce the same signs: `Dy` is -1 for up the
  screen and +1 for down. Test new controls through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
  - In a two-player game each player has their own keys (`TwoPlayers`); see the comments on `Player1` and
    `Player2`.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores and best times (`besttimes.txt`) live in the app-data
  folder (`SpecialFolder.ApplicationData/AVACastlesOfDrCreep`). Tests load them from a fresh temporary folder
  and set `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New castles and sounds go into the Python tools, not as hand-edited `.castle` or WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
