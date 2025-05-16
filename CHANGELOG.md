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
