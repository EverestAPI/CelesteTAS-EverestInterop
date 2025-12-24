# CelesteTAS v3.47.0, Studio v3.10.0

## Improved Auto-Completion
<!-- IMAGE right 300 204 Assets/v3.47.0/ImprovedAutoComplete.png -->

Auto-complete has been has been a big feature of the initial Studio v3 release,
however now with over a year of user feedback, there have been some improvements:

### Importancy Ranking

All entries are now ranked, such that the most relevant ones are more likely to be at the top.
This is achived by placed them into 4 different categories, which are listed in the following order:
1. **Favourites:** These are manually chosen by clicking on the heart icon and always appead on top
2. **Frequently Used:** These are your top-5 most used entries (note that an entry is only counted when it is actually selected)
3. **Suggestions:** These are suggestions from the game, based on what could be useful in the current situation
4. **Uncategorized:** This is everything remaining, which doesn't fall into another category

### Reduced Clutter

Entries which have seen basically no usage have been removed to reduce visual clutter.
Additionally, the popup window now attempts to take up less space.

### Feedback

Please continue to share feedback about the auto-complete feature - but of course everything else too! - so that they can further be improved.

---

## Custom-Info Editor
<!-- IMAGE right 350 282 Assets/v3.47.0/ImprovedCustomInfoEditor.png -->

The Custom-Info editor has now been upgraded from a simple textbox into a full proper editor!

That includes **auto-completion**, **syntax-highlighting** and a **live preview**.
With this, it should now be considerably easier to quickly create a Custom-Info template for something specific.

Additionally, the Game Info displayed in Studio is now a proper textfield, meaing you can **select and copy** anything from it with ease.

---

## New Commands

### `RequireDependency`

This command declares dependencies required for the TAS to run, which can easily be installed with a respective dialog box.
**Examples:**
- `RequireDependency, StrawberryJam2021` for any version
- `RequireDependency, StrawberryJam2021, 1.10.0` for specifically v1.10.0 or higher

### `ActivatedLobbyWarps`

This command tracks which warp points have been activated during the TAS' execution,
which can be useful for lobby routing or sync-checking.
**Example:** `ActivatedLobbyWarps: [4, 7]`

---

- Feature: Add accurate hitbox rendering for CommunalHelper Melvins
- Feature: Allow other mods to open a 3rd-party text window in Studio
- Feature: Add `RequireDepenency` command
- Feature: Add `ActivatedLobbyWarps` command
- Feature: Display popup when generated TAS file contains errors
- Feature: Add Favourite/Suggestion/Frequently Used entries to auto-complete menus
- Feature: Rework Custom-Info Editor into own popup
- Feature: Allow for selection of text from the Game Info box
- Featuer: Add auto-complete support for Entity IDs
- Feature: Add auto-complte support for StateMachine states of Player/Seeker/Oshiro
- Feature: Allow setting VirtualButton fields/properties directly
- Feature: Add BossesHelper support to `SeedRandom` command
- Tweak: Hide JungleHelper fireflies with Simplified Graphics
- Tweak: Hide light beams with Simplified Graphics
- Tweak: Hide KoseiHelper Debug Renderer with Simplified Graphics
- Tweak: Enable appended Actual Collide Hitboxes by default
- Tweak: Clear discovered lobby map when 'console load'ing
- Tweak: Always allow for tab-completion in the auto-complete menu
- Fix: Inaccuracies in RTA timer
- Fix: Deadlock when stepping back into freeze frames
- Fix: Inactive timers showing up in Info HUD when TimeRate is zero
- Fix: Properly save altered Crouch Dash and Grab modes in savestates
- Fix: Desync caused by inconsistant timings for exiting options menu
- Fix: Folder path with spaces not being correctly opened Finder on macOS
- Fix: `Integrate Read Commands` incorrectly skipping some lines at the start/end
- Fix: Light/Dark title bar not being properly applied on Windows
- Fix: Properly style bottom-right corner tile of scrollbars on Windows (R.I.P. ugly white square)
- Fix: Undo-state getting corruped by fix-up actions
- Fix: Don't register mouse inputs while window isn't focused
- Fix: Crash if entity has a `null` scene
- Fix: Use quotes if generated `console` command uses spaces
- Fix: Avoid starting TAS when manually starting recording with TAS Recorder
- Fix: Certain entities not being watchable in the Info HUD
- Remove: Unused clutter in parameter auto-complete for `Set`/`Invoke` commands


# CelesteTAS v3.46.2, Studio v3.9.7

## Repository Cloning

You can now easily clone Git repositories directly from Studio!
Simply goto `File -> Clone Git Repository...` and enter the appropriate URL and target directory.

This has the advantage of always cloning the respective submodules as well, unlike GitHub's "Download .zip" option.

--- 

- Feature: Utility to clone Git repositories from Studio
- Fix: Commands like `Set,Player.Speed.X,300` not working (any `Set` command referencing value types in value types)
- Fix: Exception when setting element of collection with `[]` indexing syntax
- Fix: Disable hotkey inputs in modded binding GUIs
- Fix: Trim room labels before validating
- Fix: Incorrectly rendering various SkinModHelper+ features on skin

# CelesteTAS v3.46.1, Studio v3.9.6

- Feature: Ignore breakpoints inside files accessed with `Read` commands
- Feature: Add `*` spread operator to target-queries, to flatten out collections (e.g. `Set,Player.ChaserStates*.TimeStamp,0`)
- Feature: Add `[]` index operator to access individual elements of collections (e.g. `Get,Player.Sprite.animations[idle].Delay`)
- Feature: Allow specifying custom file name for `StartRecording,FileName`
- Tweak: Adjust wording / duration of auto-pause toast message
- Fix: Avali SkinMod not working with "Prevent Skin-Mod Gameplay Changes" option
- Fix: Changes in breakpoint playback speed not immediately being respected
- Fix: RNG seeding being applied outside of TAS
- Fix: Not being able to set statemachine states with names (e.g. `Set,Player.StateMachine.State,Player.StNormal`)
- Fix: Auto-complete with `Invoke` commands not being avaiable when mods have unloaded optional dependencies
- Fix: "Game Settings" menu in Studio not reflecting changes to numeric inputs
- Fix: Apply correct theme for changelog popup dialog

# CelesteTAS v3.46.0, Studio v3.9.5

## Universal RNG Seeding

Even though most things in Celeste are deterministic, some are not!  
To solve this issue, CelesteTAS now changes all randomness to have deterministic behavior.

Additionally, all (technically) undeterministic randomness can now be seeded with the `SeedRandom,[Target],[Seed]` command.  
You can refer to [the wiki page](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Randomness) to read about all supported cases and the ethics surrounding them.

**Please report any remaining cases of undeterministic behaviour or randomness!**  
That also includes deterministic behavior which breaks with seemingly unrelated changes.

---

- Feature: Consistent behaviour of randomness when running a TAS
- Feature: Universal RNG seeding system with `SeedRandom` command
- Fix: Maintain correct `Calc.Random` state while fast-forwarding
- Fix: Dust sprites not being created while fast-forwarding

# CelesteTAS v3.45.4, Studio v3.9.5

- Feature: Allow force-enabling accessibility tools only after doing a casual playthrough
- Tweak: Better communicate warnings / risks about force-enabling accessibility tools in options

# CelesteTAS v3.45.3, Studio v3.9.5

- Feature: Allow force-enabling accessibility tools only for current session
- Feature: Add `--validate-room-labels` CLI option to promote room label validation from a warning to an error
- Fix: Crash when removing last line in Studio
- Fix: `Project File Formatter` tool not working correctly for some cased on Windows
- Fix: Studio requiring the user to manually install .NET 8 on Linux
- Tweak: Hide in-game popup messages while recording with TAS Recorder
- Tweak: Require each changelog page to be viewed for at least 1 second, to avoid users skipping over them
- Tweak: Avoid showing "Unsaved Changes" indicator when using `Auto Save File`

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
