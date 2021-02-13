### Read
- `Read, File Name, Starting Line, (Optional Ending Line)`
- Will read inputs from the specified file.
- If a custom path to read files from has been specified, it tries to find the file there. Otherwise, it will look for the file in the main Celeste directory.
- e.g. `Read, 1A - Forsaken City.tas, 6` will read all inputs after line 6 from the `1A - Forsaken City.tas` file
- This will also work if you shorten the file name, i.e. `Read, 1A, 6` will do the same 
- It's recommended to use labels instead of line numbers, so `Read, 1A, lvl_1` would be the preferred format for this example.

### Play
- `Play, Starting Line`
- A simplified Read command which skips to the starting line in the current file.
- Useful for splitting a large level into larger chunks.

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
  - `core (int)` (Set core mode to ice or fire)
  - `givekey` (gives a key)
  - `giveberry` (gives a berry)
  - `hearts` (Gives all hearts)
  - `summitgem (string)` (gives summit gem, either 0-6 or "all")
  - `sd_clearflags` (Clears all save data flags)
  - `unlock_doors` (Unlocks all key doors)
  
### Console load
- `load` (for A-Sides) is used in these examples, but this also applies to `hard` (B-Sides) and `rmx2` (C-Sides).
- `load` can be used to in place of a reset to start a chapter. However, `load` can be used to start playback from any location in the game without risk of desyncing.
- Takes the following formats:
  - `console load (SID or ID)`
  - `console load (SID or ID) screen`
  - `console load (SID or ID) screen checkpoint`
  - `console load (SID or ID) x y`
  - `ID` is just the level ID (e.g. Old Site = 2).
  - `SID` is the path to the map from the Celeste or from a mod's Maps folder (e.g. Celeste/2-OldSite). Can be found by opening the debug console.
  - `Screen` is the name of the screen you want you load (Note that if the screen name is a number you have to prepend "lvl_", so lvl_00 instead of 00).
  - `Checkpoint` is the # of the checkpoint in the room you want to load, as most rooms have multiple checkpoints (starts at 0).
  - Alternatively, `x` and `y` are the position you want to load at.
- So the following all do the same thing:
  - `console load 2 3x`
  - `console load 2 lvl_3x`
  - `console load Celeste/2-OldSite 3x`
  - `console load 2 3x 0`
  - `console load 2 376 -176`
  
### Set
- `Set, (Optional Mod).Setting, Value`
- Sets the specified setting to the specified value.
- Defaults to Celeste if no mod specified.
- Everest settings use the mod name Everest.
- Note that setting names/values may be unintuitive.
- To find the mod and setting names, go to the saves folder in your Celeste directory. The mod name should be `modsettings-(name).celeste`.
- Open the settings file in a text editor to look for the setting's name.
- Names are case sensitive.
- Make sure the value entered matches the type of the setting (if it is a boolean in the settings file, make sure you're inputting a boolean into the Set command).
- Most vanilla variants (except for mirror mode and gamespeed) are not currently functional.
- Examples:
  - `Set, ExtendedVariantMode.Dashcount, 3`
  - `Set, CelesteTAS.CenterCamera, true`

### RestoreSettings
- `RestoreSettings`
- Restore all settings when tas finished or cancelled
- The settings modified before this command will not be restored

### Unsafe
- The TAS will normally only run inside levels.
- Console load normally forces the TAS to load the debug save.
- Unsafe allows the TAS to run anywhere, on any save.

### EnforceLegal
- This is used at the start of fullgame files.
- It prevents the use of commands which would not be legal in a run.
  
### StartExport and FinishExport
- `StartExport (Optional File Path) (Optional Entities)`
- `FinishExport`
- Dumps data to a file, which can be used to analyze desyncs.
- Default filepath is dump.txt
- Keeps track of any additional entities specified - e.g. `StartExport holdables.txt TheoCrystal Glider FrostHelper.CustomSpinner@FrostTempleHelper` will keep track of Theo and Jellyfish and custom spinner. You can get the name of the entity by opening the console and clicking on it, the entity name will be displayed in the top left corner and output to log.txt.

### AnalogueMode
- `AnalogueMode, (Type)`
- `AnalogMode, (Type)` also works
- Types are `Ignore` (no check), `Circle`, `Square` and `Precise`.
- `Circle`, `Square` and `Precise` are make sure the analogue inputs sent to the game are actually possible, locking it to a circular or square deadzone, or calculating the closest position possible on a controller. Odds are you don't need to worry about this.

### Add and Skip
- These commands appear in overworld menuing and do not do anything in-game.
- Rather, they serve as instructions to the libTAS converter.
- `Add, (input line)` adds a line to the libTAS output.
- `Skip, (frames)` skips the next however many frames.
- Odds are you don't need to worry about this.
