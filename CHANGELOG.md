# CelesteTAS v3.45.2, Studio v3.9.4

- Feature: Automatically fill-in intro animation and first room label for new files if possible
- Feature: Expose SkinMod desync fix to RTA gameplay
- Fix: Breakpoint speed being shown twice in Studio
- Fix: Prevent Frame Advance or Slow Forward from triggering auto-pause popup
- Fix: Account for potential failure of loading a savestate
- Fix: Incorrect classification of various triggers
- Fix: Unexpected editing behaviour when using `\n` in commands
- Fix: Not being able to bind number keys to actions in Studio
- Fix: Frame Operations not working with AZERTY keyboard layout
- Fix: Exception when using Read-command with blank file path
- Fix: Crash when running a TAS in a map which isn't structured properly
- Fix: Crash when starting game while TAS with errors / warnings is opened
- Fix: Crash when commenting-out a blank line
- Fix: Misalignment of inputs shown in the Info HUD
- Fix: SyncChecker overflowing Windows command line length
- Tweak: Display infinity instead of `int.MinValue` when time-rate is zero
- Tweak: Fully disable particles while fast-forwarding
- Tweak: Show warning when trying to use savestate breakpoints without Speedrun Tool being installed
- Tweak: Wait 0.1s before saving a file to allow for changes to accumulate and avoid changes not being properly saved when rapidly adjusting inputs
- Refactor: Upgrade .NET 8
- Refactor: Use Everest's `EntityData` / `Tracker` systems instead of own solutions

# CelesteTAS v3.45.1, Studio v3.9.3

- Feature: Allow adjusting repeat count slightly, just like how frame counts can be adjusted (with Ctrl+Shift+Up/Down and Shift+MouseWheel)
- Feature: Allow zooming in/out of Studio with Ctrl+Plus/Minus keyboard bindings
- Tweak: Only show auto-pause toast message when playing TAS back normally. Also reduce duration from 5s to 2s
- Tweak: Change default hotkey for Frame Step Back to Ctrl+[ in order to match the regular Frame Advance default hotkey
- Tweak: Limit Repeat command at 10 million iterations to avoid accidentally running out of memory
- Tweak: Use TAS command parsing for `get`/`set`/`invoke` debug commands to support spaces in arguments
- Fix: Re-saving frame after clearing when paused on savestate breakpoint
- Fix: Invalid playback state being set after loading a savestate
- Fix: Slightly incorrect logic for determining save point for savestate breakpoint

# CelesteTAS v3.45.0, Studio v3.9.2

## Multiple Savestate Slots

With the release of multiple savestate slots for Speedrun Tool, by Lozen, CelesteTAS follows with exposing that functionally to regular TASes.  
Simply use as many savestate-breakpoints as you desire, and it'll use the most appropriate when restarting the TAS.

While there is no hard upper limit, for your own computers sake, try to keep the amount reasonable and only in the area you're currently working in.

**NOTE:** For backwards compatibility reasons, the minimum required SpeedrunTool version is _not_ v3.25.0. However, you will need it (or later), to be able to use multiple savestates at once, so make sure it's up-to-date.

---

## Frame Step Back

Previously a feature of TAS Helper, this now has been moved over to CelesteTAS itself.  
By pressing the hotkey (`Ctrl+I` by default), you'll be able to step in time to previously executed inputs.

This an accumulative action, allowing you to specify the amount of frames by repeatedly pressing the hotkey (and hotkeys which would usually forward to TAS), before the action is performed.  
Since the game does not support going back in time, it has to play back to the target frame from a breakpoint before or the start if none is available. Since that is an expensive operation, it is best to specify the desired frame count once, to avoid re-running the TAS multiple times.

---

## Force-Stop Breakpoints

By placing an exclamation mark after a breakpoint (`***!`), it will **always** cause the TAS to be stopped at the desired location.  
This can be useful when wanting to go back without having to comment-out / delete all breakpoints after it.

---

## Improved Runtime Validation

While playing a TAS back, the current level and room will be validated, based on available information such as a `console load` command or room labels.  
This is useful to identify desyncs in the TAS early.

**NOTE:** Since some projects currently have incorrect room labels, it's only a warning right now. However, it is intended to become an error later on. 

---

- Feature: Multiple Savestate Slots support
- Feature: Migrate Frame Step Back from TAS Helper
- Feature: Force-Stop Breakpoints
- Feature: Live-update simplified spinner color when changing the setting
- Feature: Validate active level and room labels
- Fix: Crashes / Desyncs caused by attempting to fix desyncs caused by SkinMods
- Fix: Custom Info Template displaying instance instead of value for arrays
- Fix: Specifying a `@ModName` suffix on target-queries returning duplicate results
- Fix: Command separator placeholder not being replaced when inserted from command menu in Studio
- Fix: Not being able to undo past implicit formatting changes
- Fix: Studio auto-installer having wrong checksum hardcoded on Linux
- Fix: Desync caused by Simplified Graphics with some custom FrostHelper spinners
- Fix: Info HUD not accounting delta-time for frame count calculations
- Fix: Debug Console commands not allowing same separators, like respective TAS commands
- Fix: Crash when launching Studio v3 for the first time
- Tweak: Display popup-message for various actions during TAS playback

# CelesteTAS v3.44.1, Studio v3.9.1

- Fix: Crash on first start-up on Windows

# CelesteTAS v3.44.0, Studio v3.9.0

## Radeline Simulator

The Radeline Simulator is a tool created by Kataiser, which allows for brute-forcing inputs to get the player into a specific subpixel position.  
With it now being integrated into Studio, it can easily access the game's data and provide fast results.

It can be found under `Tools -> Radeline Simulator`.

---

## Better Accessibility Tools

Some maps choose to prevent user from using certain accessibility tools (like the Debug Map), to avoid the users from accidentally ruining their experience while playing the map.  
However these accessibility tools are often useful when playing the map not as a casual playthrough, such as routing a TAS/Speedrun.

By default, these features will now be forcefully re-enabled while Studio is connected to the game, to give users the option back.  
It can also be always enabled or disabled.

This also includes annoying events, like crashing / restarting the game.

---

## Improved SkinMod Support

SkinMods are known to sometimes change the length of certain animations to better fit the artists intention.  
However these changes can cause desyncs and confusion, because the TAS expects a different length than it actually is.

Now, it will apply vanilla animation lengths and metadata, while maintaining the SkinMod's visual style as best as it can.  
If a map requires a SkinMod, this fix will not be applied.

Please report any cases of this feature not working as intended.

---

## `RealTime` Timing Command

With the rise in interest for some to time TASes using real-time, this commands now provides an easy way to do that.  
It'll count everything from the point 'Begin' is pressed, only excluded real loading times.

It will be filled in, when the TAS ends, just like the `FileTime` command works currently.

When a `RealTime` command is detected in the file, an additional 'Real Timer' will appear in the Info HUD to display the current real time.

---

## Reworked Target-Query System

Target-Queries are the system used by `Set`/`Invoke`-Commands and Custom Info.  
They already supported limiting to specific Entity IDs with `Query[Room:ID]` and querying components with `Query:ComponentType`, 
however these only worked on the base type.  
Now they can be specified anywhere, even on members.

Additionally, lists can now be accessed in "parallel", meaning you could now do the following for example:  
`Set, Player:Leader.Followers.Entity.Golden, True` to set all carried berries by the player to be golden berries.

You can now also get auto-complete for parameter values of commands, for example:  
`Set, Player.StateMachine.State, Player.StNormal`

---

## Breakpoint Editing

Similar to the editing experience with regular inputs, breakpoints are now treated the same way.  
It is now impossible to have an invalid breakpoint in a TAS file; it either needs to be valid or not there.

This might break a bit of muscle memory at first, but it should be quick to get used to.  
Additionally, these changes sound more drastic than they actually are.

---

- Feature: Add Radeline Simulate to Studio
- Feature: Add `(Midway)RealTime` timing command
- Feature: Prevent desyncs caused by SkinMods
- Feature: Support accessing modded sessions and savedata with target-queries
- Feature: Add auto-complete support to command parameters
- Feature: Restrict breakpoint editing in Studio
- Feature: Add option to disable auto-pausing when TAS ends and is considered a draft
- Feature: Add `get(query)`/`set(query, value)`/`invoke(query, parameters)` Lua commands to perform the same as the commands do
- Feature: Force-allow accessibility tools while Studio is connected
- Feature: Provide sync-checking system for automated testing of TAS files
- Refactor: Changelog system to allow for Markdown
- Refactor: Target-query system to be game-independent
- Fix: Inaccurate hitboxes for Badeline laser
- Fix: Inaccurate hitboxes for FrostHelper Arbitrary-Shape colliders
- Fix: Inaccurate hitboxes for CrystallineHelper Force Fields
- Fix: `ExportGameInfo`/`ExportRoomInfo` commands not supporting auto-complete
- Fix: Timestamps being uncommented with Ctrl+K
- Fix: Some visual Extended Variants not being account for in Simplified Graphics
- Fix: Modded bindings being unusable after stopping TAS from breakpoint
- Fix: Floating-point inaccuracies with subpixel indicator
- Tweak: Improve dialog when discarding changes in Studio

# CelesteTAS v3.43.8, Studio v3.8.4

- Fix: CRLF line endings being inserted when auto-completing some commands
- Tweak: Allow breakpoints / comments between inputs when stealing frames with Frame Operations
- Fix: Always prevent TAS execution fully inside the crash handler
- Fix: Crash when using Set/Invoke command on entity which no current instances
- Fix: Rare crash when updating timing commands of a file

# CelesteTAS v3.43.7, Studio v3.8.3

- Fix: Extended Camera Dynamics zoom affecting gameplay
- Fix: Extended Camera Dynamics zoom not being carried properly into different levels
- Fix: Multiple buttons not being properly bound with Set-command
- Fix: Incorrect default hotkeys / display names for some Studio actions

# CelesteTAS v3.43.6, Studio v3.8.2

- Fix: Argument parsing for EvalLua-Command
- Feature: Improved hotkey support for Studio
- Fix: Crash when pressing enter while having a selection active
- Fix: Better instance resolving for target-queries
- Fix: Don't apply formatting to unrelated TAS files
- Feature: Apply label refactors across entire repository instead of just submodule
- Feature: Allow editing .studioconfig.toml file with Project File Formatter
- Fix: Incorrect argument formatting for some commands
- Fix: Concurrency issues with file formatting
- Fix: Incorrect total frame count in Studio
- Fix: Disable input editing while TAS is running (if setting is enabled)
- Fix: Dash count getting changed when restoring settings
- Tweak: Don't start empty TAS files
- Fix: Crash when providing too many arguments to target-query
- Fix: Not being able to bind multiple keys to custom binding
- Tweak: Comment/Uncomment using '# ' instead of '#' if possible
- Fix: Right-aligned line numbers being incorrectly positioned

# CelesteTAS v3.43.5, Studio v3.8.1

- Fix: Playback while recording a TAS
- Feature: Add Portuguese language translation
- Feature: Add German language translation
- Tweak: Update language translations for more grammatically correct / informative wording

# CelesteTAS v3.43.4, Studio v3.8.1

- Fix: Free-camera interactions with center-camera
- Fix: Center camera zooming and rectangle selection not working as intended
- Fix: Collab lobbies not working with the SelectCampaign command
- Fix: Various issues related to save-states
- Fix: SelectCampaign command not accounting for variants menu item

# CelesteTAS v3.43.3, Studio v3.8.1

- Fix: Target-query members being treated as static when no instances were found
- Fix: Crash when connecting Studio to Celeste on macOS
- Fix: Set commands not working properly
- Fix: "playtas" command not stopping previous TAS
- Fix: Potential crash when migrating from Studio v2
- Tweak: Include non-vanilla core messages in Simplified Graphics

# CelesteTAS v3.43.2, Studio v3.8.0

- Fix: FileTime being incorrect when TAS was started from another file slot
- Fix: Hotkeys being processed even while CelesteTAS was disabled
- Fix: Audio sometimes being incorrectly muted
- Fix: Debug Console in TAS immediately reopening after closing with ToggleDebugConsole binding
- Fix: Report errors when trying to use instance members in a static context
- Fix: Previous comments not being clear after updating TAS

# CelesteTAS v3.43.1, Studio v3.8.0

- Fix: Crash on start-up with CrystallineHelper installed
- Fix: Interrupted Studio installation not causing 2nd attempt to crash

# CelesteTAS v3.43.0, Studio v3.8.0

- Fix: Center Camera movement with Upside-Down Extended Variant
- Fix: SpirialisHelper stopwatches causing hitboxes to not properly render
- Feature: Limit line number with to currently visible lines
- Fix: Opening debug console in TAS not respecting debug mode setting
- Fix: Debug console in TAS not being able to open outside a level
- Fix: SaveAndQuitReenter command desyncing for debug save file if debug mode is disabled
- Fix: Limit default query for search dialog to current line
- Tweak: Move Studio version to beginning of title
- Fix: Slowed-down TAS playback with Motion Smoothing enabled
- Feature: Allow rebinding / unbinding characters for frame operations
- Feature: Jump to next line when (un)commenting a line
- Feature: Support nested / unlimited Repeat commands (only use them if absolutely necessary!)
- Feature: Add option to show current file in File Explorer
- Fix: Restoring settings changing window size
- Feature: Restore Extended Variants after TAS ends
- Tweak: Enable "Restore Settings after TAS Stops" by default
- Fix: Stopping TAS during StIntroWakeUp causing player to be stuck
- Refactor: Center Camera and Offscreen Hitbox implementation
- Fix: Zoom-Level Extended Variant causing rendering issues with Center Camera
- Fix: Extended Camera Dynamics causing rendering issues with Center Camera
- Feature: Automatically enable Extended Camera Dynamics with Center Camera to provide a fully rendered zoom
- Fix: Apply color grade to offscreen hitboxes (if you considered this a feature, use Simplified Graphics instead)
- Fix: "Entity.Position.X" not being able to be set
- Fix: Hotkeys being triggered while inputting text
- Remove: "Mod 9D Lighting" option

# CelesteTAS v3.42.2, Studio v3.7.1

- Fix: Assert commands not being able to run Lua code during EnforceLegal
- Fix: SelectCampaign command not properly handling EnforceLegal

# CelesteTAS v3.42.1, Studio v3.7.1

- Fix: Unsafe actions not aborting the TAS
- Fix: SelectCampaign command causing desync with SaveAndQuitReenter
- Fix: Game Info rendering on Windows
- Fix: Commands after last input not being executed
- Fix: "/tas/playtas" DebugRC endpoint not stopping previous TAS
- Tweak: Remove "level.StartedFromBeginning" requirement from MidwayChapterTime

# CelesteTAS v3.42.0, Studio v3.7.0

- Feature: Unified project styling with .studioconfig.toml (See "Studio Config" wiki for more details)
- Feature: Allow targeting components in target queries with "EntityName:ComponentName.Member" (See "Info HUD" wiki for more details)
- Feature: Add Entity Tables to Custom Info (See "Info HUD" wiki for more details)
- Feature: Add "SelectCampaign,LevelSet,OptionalFileName" to automatically prepare a new save file with the specified options
- Feature: Allow pressing input while a TAS is fast-forwarding
- Feature: Auto-pause TAS when reaching end of draft
- Feature: Prevent frame-advancing into end of TAS while drafting
- Feature: Allow usage of target queries as values (example: "Set,Player.StateMachine.State,Player.StNormal"
- Feature: Add "Better Invincible" mode, which prevents desyncs caused by "Set,Invincible,true" (previously part of TAS Helper)
- Feature: Allow opening debug console while TAS is running (previously part of TAS Helper)
- Feature: Automatically hide unimportant triggers (previously part of TAS Helper)
- Feature: Automatically hide camera triggers while camera hitboxes are disabled (previously part of TAS Helper)
- Feature: Allow repeating hotkeys (such as frame-advance) by holding them down
- Feature: Show the Info HUD in the overworld
- Feature: Require "?forceAllowCodeExecution=true" query parameter to evaluate code in DebugRC requests
- Fix: Potential crash caused by auto-multilining comments
- Fix: Not being able to comment-out inputs by writing "#" before them
- Fix: Studio not rendering updates unless resized (Requires "WPFSkiaHack" config to be set to "true")
- Fix: Some issues with Studio auto-install
- Fix: Not escaping HTML content in DebugRC
- Fix: Auto-complete for Set/Invoke command
- Fix: Rare crash caused by rendering the editor mid-update
- Optimize: Rendering of In-Studio game info
- Remove: Celeste v1.3.1.2 legacy support
- Refactor: TAS playback
- Refactor: TAS document parsing
- Refactor: Set/Invoke/Assert command
- Refactor: Custom Info templating
- Refactor: Entity Watching

# CelesteTAS v3.41.12, Studio v3.6.2

- Fix: Occasional crash while editing file
- Fix: Exclude starting label while indexing room labels including Read-commands
- Fix: Game Info Panel not accounting for horizontal scroll bar on Windows
- Fix: Not refocusing the editor after editing info template on Windows

# CelesteTAS v3.41.11, Studio v3.6.1

- Fix: "Force Combine Inputs" not being available
- Fix: Issues / crashes with collapsable sections
- Fix: Unable to uncomment breakpoints with Ctrl+K
- Fix: Drag n' Drop support on Windows

# CelesteTAS v3.41.10, Studio v3.6.0

- Feature: Disable input-editing while a TAS is running (configurable under "Preferences -> Input Sending -> Disable while Running")
- Fix: Uncommenting inputs not working
- Fix: Typo in "Simplified Graphics" game setting
- Fix: Potential crash when combining inputs
- Fix: Not correctly closing other open Studio instances on Windows
- Fix: Popup-menus sometimes not being shown
- Fix: Potential race-condition while booting Celeste after a Studio update
- Fix: Jadderline not considering DeltaTime
- Tweak: Slightly smarter mouse selection logic

# CelesteTAS v3.41.9, Studio v3.5.1

- Fix: Caret positioning after auto-completing command / snippet
- Fix: Issues with auto-multilining comments
- Fix: Find-dialog returning wrong locations for multiple matches in a single line
- Fix: Entire line being deleted when pressing backspace to the left of the frame count
- Fix: Scrolling not working on high-DPI macOS
- Tweak: Apply regular enter logic on first non-whitespace character instead of first character of line
- Tweak: Better error messages

# CelesteTAS v3.41.8, Studio v3.5.0

- Feature: New preference for toggling auto-multilining comments
- Feature: Clear empty comments when pressing enter (intended for avoiding auto-multilining by pressing enter twice)
- Tweak: Formatting style for saved TAS-files
- Fix: Not being able to uncomment lines with comments using Ctrl+K
- Fix: "Insert Other Command" menu being broken
- Fix: DPI-awareness on macOS
- Fix: Auto-installation issues on macOS

# CelesteTAS v3.41.7, Studio v3.4.2

- Fix: Regressions caused by recent backend changes, including higher latency, lower text-quality, desync between the inputs shown in-Studio / in-game
- Fix: Auto-installation issues on macOS
- Fix: Always providing the x86_64 version for macOS, instead of the ARM64 version for Apple Silicon macOS

# CelesteTAS v3.41.6, Studio v3.4.1

- Fix: Inputs flickering / disapperaing on Windows
- Fix: Rendering issues with wrapped comments

# CelesteTAS v3.41.5, Studio v3.4.0

- Feature: Auto-split lines on first and last column
- Feature: Remember previous search query in Find-dialog
- Feature: Restrict multiline comments to "# "
- Optimize: Use SkiaSharp for improved rendering performance, fixing lag issues on Windows
- Optimize: Reduce unnecessary calculations
- Fix: Crashes related to non-default Studio locations and editing actions around quick-edits
- Fix: Not auto-saving file after undo/redo
- Fix: "Insert Current Player Speed" having wrong entry name
- Fix: Built-in font on macOS
