### Read
- `Read, File Name, Starting Line, (Optional Ending Line)`
- Will read inputs from the specified file.
- If a custom path to read files from has been specified, it tries to find the file there. Otherwise, it will look for the file in the main Celeste directory.
- e.g. `Read, 1A - Forsaken City.tas, 6` will read all inputs after line 6 from the `1A - Forsaken City.tas` file
- This will also work if you shorten the file name, i.e. `Read, 1A, 6` will do the same 
- It's recommended to use labels instead of line numbers, so `Read, 1A, lvl_1` would be the preferred format for this example.

### Play
- `Play, Starting Line, (Optional Frames to Wait)`
- A simplified `Read` command which skips to the starting line in the current file.
- Useful for splitting a large level into smaller chunks.

### Repeat and EndRepeat
- Repeat the inputs between `Repeat` and `EndRepeat` several times, nesting is not supported.
- `Repeat, Count`
- `EndRepeat`

### Labels
- Prefixing a line with `#` will comment out the line
- A line beginning with `#` can be also be used as the starting point or ending point of a Read instruction.
- You can comment highlighted text in Celeste Studio by hitting `Ctrl+K`

### Console
- `Console (command)`
- Enters the command into the Celeste console.
- Useful commands include:
  - `p_dreamdash` (enables dream dashing)
  - `p_twodashes` (enables two dashes)
  - `core (int)` (Set core mode to none(0), fire(1) or ice(2))
  - `givekey` (gives a key)
  - `giveberry` (gives a berry)
  - `hearts (int default all) (string default current level set)` (sets the amount of obtained hearts for the specified level set to a given number)
  - `summitgem (string)` (gives summit gem, either 0-6 or "all")
  - `sd_clearflags` (clears all save data flags)
  - `unlock_doors` (unlocks all key doors)
  - `flag (string)` (set/remove a session flag)
  - e.g. `flag oshiro_clutter_cleared_0/1/2` (clear towels/books/chests)
  
### Console load
- `load` (for A-Sides) is used in these examples, but this also applies to `hard` (B-Sides) and `rmx2` (C-Sides).
- `load` can be used to in place of a reset to start a chapter. However, `load` can be used to start playback from any location in the game without risk of desyncing.
- `load` command requires an additional frame compared to the normal start/restart chapter.
- `loadN` N is for loading the Nth save file (0 means debug save file). e.g. `load1`, `load2`, `load3`...
- Takes the following formats:
  - `console load (ID or SID)`
  - `console load (ID or SID) screen`
  - `console load (ID or SID) screen spawnpoint`
  - `console load (ID or SID) positionX positionY speedX speedY`
  - `ID` is just the level ID (e.g. Old Site = 2).
  - `SID` is the path to the map from the Celeste or from a mod's Maps folder (e.g. Celeste/2-OldSite). Can be found by opening the debug console.
  - `screen` is the name of the screen you want you load (Note that if the screen name is a number you have to prepend "lvl_", so lvl_00 instead of 00).
  - `spawnpoint` is the # of the spawnpoint in the room you want to load, as most rooms have multiple spawnpoints (starts at 0).
  - Alternatively, `positionX` and `positionY` are the position you want to load at, `speedX` and `speedY` are the speed after respawning.
  - `console load` w/o any parameters is used to load a s&q save file
- So the following all do the same thing:
  - `console load 2 3x`
  - `console load 2 lvl_3x`
  - `console load Celeste/2-OldSite 3x`
  - `console load 2 3x 0`
  - `console load 2 376 -176`
  - `console load 2 376 -176 0 0`
  
### Set
- `Set, (Optional Mod).Setting, Values`
- `Set, Entity.Field, Values` (set all entities)
- `Set, Entity[roomName:id].Field, Values` (set specific entity)
- `Set, Level.Field, Values`
- `Set, Session.Field, Values`
- `Set, Type.StaticField, Values`
- Sets the specified setting to the specified value.
- Defaults to Celeste if no mod specified.
- Everest settings use the mod name `Everest`.
- Note that setting names/values may be unintuitive.
- To find the mod and setting names, go to the saves folder in your Celeste directory. The mod name should be `modsettings-(name).celeste`.
- Open the settings file in a text editor to look for the setting's name.
- Names are case sensitive.
- Make sure the value entered matches the type of the setting (if it is a boolean in the settings file, make sure you're inputting a boolean into the Set command).
- Examples:
  - `Set, VariantMode, false`
  - `Set, CheatMode, true`
  - `Set, DashMode, Infinite` or `Set, DashMode, 2`
  - `Set, Player.Position, 123.123, -1028`
  - `Set, Player.Position.X, 123.123`
  - `Set, Player.Speed, 325, -52.5`
  - `Set, Player.Ducking, true`
  - `Set, Everest.ShowModOptionsInGame, false`
  - `Set, ExtendedVariantMode.Dashcount, 3`
  - `Set, CelesteTAS.CenterCamera, true`
  - `Set, AnarchyCollab2022.LeftButton, Q, W`

    Set helper's button, supporting keyboard and mouse. Then you can use the `P` custom key press modifier or the [Press](#press) command to press keys, use the [Mouse](#mouse) command to press and move mouse (available mouse buttons `Left`, `Right`, `Middle`, `X1`, `X2`).
    The setting is only valid when tas is running, it will be restored automatically when tas is stopped.

### Invoke
- `Invoke, Entity.Method, Parameter1, Parameter2...` (all entities)
- `Invoke, Entity[roomName:id].Method, Parameter1, Parameter2..` (specific entity)
- `Invoke, Level.Method, Parameter1, Parameter2..`
- `Invoke, Session.Method, Parameter1, Parameter2..`
- `Invoke, Type.StaticMethod, Parameter1, Parameter2..`
- Examples:
    - `Invoke, Level.Pause`
    - `Invoke, Player.Die`
    - `Invoke, Session.SetFlag, whatever`

### Unsafe and Safe
- The TAS will normally only run inside levels and stop when entering the Options/ModOptions menu.
- Console load normally forces the TAS to load the debug save.
- `Unsafe` allows the TAS to run anywhere, on any save.
- `Safe` makes the TAS only run inside levels and debug save.

### EnforceLegal
- This is used at the start of fullgame files.
- It prevents the use of [Console](#console), [Set](#set), [Invoke](#invoke), [StunPause simulate mode](#stunpause-and-endstunpause) commands which would not be legal in a run.

### Assert
- If the condition being tested is not met, tas will be stopped. Usually used to make tas stop as soon as possible, when tas desync.
- `Assert, Condition, Expected, Actual`
- Conditions are `Equal`, `NotEqual`, `Contain`, `NotContain`, `StartWith`, `NotStartWith`, `EndWith`, `NotEndWith`
- Examples:
  ```
  # Ensure that you are currently in the save file selection screen and the save file is selected
  Assert EndWith OuiFileSelect Engine.Scene.Current
  Assert Equal True Engine.Scene.Current.SlotSelected
  ```
  ```
  # Ensure that you are currently in the chapter 2
  Assert EndWith 2-OldSite Session.Area.SID
  ```

### StunPause and EndStunPause
- ```
  StunPause, (optional mode, Simulate or Input)
  (inputs）
  EndStunPause
  ```
- Simulate mode is illegal and should only be used to test routes, which will pause every other frame without doing the actual pause inputs.
- Input mode is legal, it is actually [AutoInput](#autoinput-startautoinput-endautoinput-skipinput) but with predefined pause inputs and cycle length. It would hold jump automatically if the inputs before the pause have a jump button pressed.

  e.g.
  ```
  1,J
  StunPause, Input
  SkipInput, 3, 1
  7,R,X
  4,R,J
  EndStunPause
  ```
  would produce
  ```
   1,J
   1,S,N
  10,J,O
   5,R,X
   1,S,N
  10,O
   2,R,X
   1,S,N
  10,O
   2,R,J
   1,S,N
  10,J
   2,R,J
  ```

- The command's mode is determined by several things, the priority from high to low are:
  1. [EnforceLegal](#enforcelegal) command force all the commands use `Input` mode
  2. Mode specified by the `StunPause` command
  3. Mode specified by the `StunPauseMode` command
  4. Default mode is `Input`

### StunPauseMode
Specify the default mode for `StunPause` command.
- `StunPauseMode, Simulate/Input`

### AutoInput, StartAutoInput, EndAutoInput, SkipInput
- Inserts the auto inputs every cycle length frames that is played through inputs.
- `SkipInput` Prevents the next input from being calculated in the `AutoInput` cycle. Usually used to mark the freeze frames.
- `SkipInput, (skip frames), (optional waiting frames)` Wait specified frames then prevents the next specified frames from being calculated in the `AutoInput` cycle. Usually used to mark the freeze frames.
- ```
  AutoInput, (cycle length)
  (auto inputs)
  StartAutoInput
  (inputs)
  SkipInput
  (skipped input)
  (inputs)
  EndAutoInput
  ```
- e.g.
  ```
  AutoInput, 2
   1,S,N
  10,O
  StartInput
  SkipInput, 3, 1
   7,R,X
   4,R,J
  EndAutoInput
  ```
  would produce
  ```
   1,S,N
  10,O
   5,R,X
   1,S,N
  10,O
   2,R,X
   1,S,N
  10,O
   2,R,J
   1,S,N
  10,O
   2,R,J
  ```

### Press
- Press the specified keys with the next input, [the available keys can be found here](https://docs.monogame.net/api/Microsoft.Xna.Framework.Input.Keys.html).
- `Press, Key1, Key2, ...`
- e.g. Press Q and W and Right 10 frames.
  ```
  Press, Q, W
  10,R
  ```

### Mouse
- Move mouse and press mouse buttons with the next input.
- `Mouse, X, Y, [ScrollWheel], [L/R/M/X1/X2], [L/R/M/X1/X2]...`
- X and Y are integers, X from 0 to 319, y from 0 to 179.
- e.g. Press mouse left button at (50, 100) and jump 10 frames.
  ```
  Mouse, 50, 100, L
  10,J
  ```

### ExportGameInfo and EndExportGameInfo
- `ExportGameInfo (Optional File Path) (Optional Entities Names)`
- `EndExportGameInfo`
- Dumps data to a file, which can be used to analyze desyncs.
- Default filepath is `dump.txt`.
- Keeps track of any additional entities specified - e.g. `ExportGameInfo additional.txt TheoCrystal Glider CustomSpinner@FrostTempleHelper` will keep track of Theo, Jellyfish, and custom spinners from the FrostHelper mod.
- You can get the name of the entity by opening the console and clicking on it, the entity name will be displayed in the top left corner and output to log.txt.

### ExportRoomInfo and EndExportRoomInfo
- `ExportRoomInfo (Optional File Path)`
- `EndExportRoomInfo`
- Dumps the elapsed time of each room to a file. which can be used to compare improvements.
- Default filepath is `dump_room_info.txt`.

### CompleteInfo
- `CompleteInfo, Side, SID/ID`
- The successive comments immediately following this command will be displayed to the specified chapter complete screen.
- Sides are `A`, `B` and `C`
- e.g. `CompleteInfo A 1` display info on 1A complete screen.
- e.g. `CompleteInfo` If neither side nor sid/id is specified, the info will be displayed on all chapters, if the other CompleteInfo command specifies a chapter, both will be displayed.

### RecordCount
- e.g. `RecordCount: 1`
- Every time you run tas after modifying the current input file, the record count auto increases by one.

### FileTime
- e.g. `FileTime: 0:51.170(3010)`
- Auto update the file time when TAS has finished running, the file time is equal to the elapsed time during the TAS run.

### ChapterTime
- e.g. `ChapterTime: 0:49.334(2902)`
- After completing the whole level from the beginning, auto updating the chapter time.

### AnalogueMode
- `AnalogueMode, (Mode)`
- `AnalogMode, (Mode)` also works
- Modes are `Ignore` (no check), `Circle`, `Square` and `Precise`.
- `Circle`, `Square` and `Precise` are make sure the analogue inputs sent to the game are actually possible, locking it to a circular or square deadzone with the maximum amplitude, or calculating the closest position possible on a controller within the possible amplitude.
- Odds are you don't need to worry about this.

### ExportLibTAS and EndExportLibTAS
- `ExportLibTAS, (Optional File Path)`
- Converts the TAS to the inputs portion of a LibTAS movie file.
- Default filepath is `libTAS_inputs.txt`
- Odds are you don't need to worry about this.

### Add and Skip
- These commands appear in overworld menuing and do not do anything in-game.
- Rather, they serve as instructions to the libTAS converter.
- `Add, (input line)` adds a line to the libTAS output.
- `Skip, (frames)` skips the next however many frames.
- Odds are you don't need to worry about this.

### ExitGame
- Used to force close the game when recording video with [.kkapture](https://github.com/DemoJameson/kkapture/wiki) to finish recording.
