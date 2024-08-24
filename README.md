# CelesteTAS

## TAS tools for Celeste / Everest

### License: MIT

---

- Install [Everest](https://everestapi.github.io/) if you haven't already.
- (Recommended) Use the 1-click installer [here](https://gamebanana.com/tools/6715). (Alternatively) [Download the latest auto-build](https://0x0a.de/twoclick/?nightly.link/EverestAPI/CelesteTAS-EverestInterop/workflows/Build/rewrite/CelesteTAS.zip).
- Make sure the mod is enabled in the in-game mod options.
- Enable the mod-setting `CelesteTAS > More Options > Launch Studio at Boot`. Celeste Studio, our input editor, should now automatically launch.
- Alternatively, you can start Celeste Studio directly. It'll be installed in the `CelesteStudio` directory inside your Celeste install. 
- You can find the most up-to-date input files [here](https://github.com/VampireFlower/CelesteTAS).

## Input File
The input file is a text file with `tas` as a suffix, e.g. `1A.tas`.

Format for the input file is (Frames),(Actions)

e.g. `123,R,J` (For `123` frames, hold `Right` and `Jump`)

## Available Actions
- `R` = Right
- `L` = Left
- `U` = Up
- `D` = Down
- `J` = Jump / Confirm
- `K` = Jump Bind 2
- `X` = Dash / Talk / Cancel
- `C` = Dash Bind 2 / Cancel Bind 2
- `Z` = Crouch Dash
- `V` = Crouch Dash Bind 2
- `G` = Grab
- `H` = Grab Bind 2
- `S` = Pause
- `Q` = Quick Restart
- `F` = Feather Aim
  * Format: F, angle, optional upper limit of single axis (default value is 1, range is 0.26 to 1, works in all [analog modes](Docs/Commands.md#analoguemode))
- `O` = Confirm Bind 2
- `N` = Journal / Talk Bind 2
- `A` = Dash Only Directional Modifier (generally used to manipulate camera with binocular control storage. eg: `15,R,X,ALU`)
- `M` = Move Only Directional Modifier (eg: `15,X,AL,MR`)
- `P` = Custom Button Press Modifier (used to press inputs added by mods after binding them using the [Set command](Docs/Commands.md#set), e.g. `15,R,X,PA` after binding A to a custom input)

## Controls
While in game or in Studio:
- Start/Stop Playback: `RightControl`
- Restart Playback: `Equals`
- Fast Forward / Frame Advance Continuously: `RightShift` or `Controller Right Analog Stick`
- Fast Forward to Next Comment: `RightAlt + RightShift`
- Slow Forward: `\`
- Pause / Frame Advance: `[`
- Pause / Resume: `]`
- Toggle Hitboxes: `LeftControl + B`
- Toggle Simplified Graphics: `LeftControl + N`
- Toggle Center Camera: `LeftControl + M`
- Save State: `RightAlt + Minus`
- Clear State: `RightAlt + Back`
- Info HUD:
  * While holding the Info HUD hotkey, left-click to move the HUD around
  * Double press the Info HUD hotkey to toggle it
  * While Holding the Info HUD hotkey, left-click on entity to watch the entity
- These can be rebound in Mod Options
  * You will have to rebind some of these if you are on a non-US keyboard layout.
  * Binding multiple keys to a control will cause those keys to act as a key-combo.

## Special Input

### Breakpoints
- You can create a breakpoint in the input file by typing `***` by itself on a single line
- The TAS, when played back from the start will fast-forward until it reaches that line and will then pause the TAS
- `***S` will make a [savestate](#savestate), which can reduce TAS playback time. 
- You can specify the speed with `***X`, where `X` is the speedup factor. e.g. `***10` will go at 10x speed, `***0.5` will go at 0.5x speed.

### Commands
- Various commands exist to facilitate TAS playback. [Documentation can be found here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/Docs/Commands.md).

## Savestate
- Savestates require the [SpeedrunTool](https://gamebanana.com/tools/6597) mod.
- Reliable in vanilla maps.
- Savestates may not work properly in custom maps that use code mods. Placing a savestate right before leaving a room can help with this.
- Currently, cannot savestate when paused.
- Crashes due to running out of memory are possible, although uncommon.

## Misc

### Move Camera
When center camera is enabled, free camera hotkey + holding mouse right button or free camera hotkey + arrow move canvas, when zooming out holding mouse right button or info hud hotkey + arrow move camera.

### Zoom Camera
When center camera is enabled, scroll wheel or free camera hotkey + home/end zoom camera.

### Reset Camera
When center camera is enabled, double press mouse right button or double press free camera hotkey reset camera.

### Rectangle Selection Info
Hold the Info HUD hotkey and the mouse right down to select a rectangle. Copies the position of the top left and bottom right corners when the mouse button is released. This helps to define checkpoints for [Featherline](https://github.com/tntfalle/featherline).

### Watch Entity
Enable `Info HUD`, holding info hud hotkey then left-click to add the entity to be watched, while holding watch trigger hotkey to watch trigger, right-click to clear the watching entities. Supports exporting watching entities info via
the `StartExportGameInfo` command.

### Custom Info
The contents of the curly brackets will be converted to actual data, here are some examples:
- `{EntityName.field...}` Find all entities. e.g. `{Strawberry.Position}`
- `{EntityName[entityId].field...}` Find the entity with the specified entityId. e.g. `{Strawberry[1:12].Position}` means 1A gold berry. You can get the entityId by opening the console and left-clicking on the entity.
- `{EntityName@AssemblyName.field...}` Add the assembly name, if the simple name exists in multiple helpers and you want to specify the helper. e.g. `{CustomSpinner@FrostTempleHelper.Position}` and `{CustomSpinner@VivHelper.Position}`. You can get the assembly name by opening the console and left-clicking on the entity.
- `{Level.field...}` Get the value of level field. e.g. `Wind: {Level.Wind}`.
- `{Session.field...}` Get the value of session field. e.g. `Room: {Session.Level}`.
- `{ClassName.staticField.field...}` Non-entity and non-level types that can get the value of a static field.
- `{Player.Position.Length()}` Invoke method is supported, but must be parameterless and return a non-void type. Be careful not to invoke method that change the game state, as this will cause tas desync.
- `{Player.AutoJumpTimer.toFrame()}` add `toFrame()` to the end can change the float value to frames.
- `{Player.Speed.toPixelPerFrame()}` add `toPixelPerFrame()` to the end can change the float/vector2 speed unit to pixel/frame.
- `{Player.Position:}` add `:` or `=` to the end will add label before the value. e.g. `{Player.Position:}` is the same as `Player.Position: {Player.Position}`.
- `AutoJump: {Player.AutoJump} ({Player.AutoJumpTimer.toFrame()})`
- `Theo: {TheoCrystal.Position}`
- `TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}`
- `KeyCycle: {Key.sprite.CurrentAnimationFrame}`
- `CustomSpinner: {CustomSpinner.Position}` or `CustomSpinner: {FrostHelper.CustomSpinner@FrostTempleHelper.Position}`

The contents wrapped in double center brackets will be executed as lua code, [check here for how to write lua](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/Docs/Commands.md#evallua).
Be careful not to change the game state, as this will cause tas desync.
Here are some examples:
- `[[return player.Position]]`
- `[[return player.Position, player.Speed]]` Return multiple results. 

## Other Useful Tools
- [TAS Recorder](https://gamebanana.com/tools/14085): High quality fixed framerate TAS encoder, cross-platform (use this instead of .kkapture or ldcapture) 
- [GhostMod](https://gamebanana.com/mods/500759): Compare new TASes with old ones.
- [Radeline](https://github.com/Kataiser/radeline): Chaos monkey that optimizes a Celeste TAS by randomly (or sequentially) changing inputs.
- [Lobby Router](https://jakobhellermann.github.io/trout/): Helps find the fastest route for a collab lobby
- [Featherline](https://github.com/tntfalle/featherline): Algorithm for analog feather movement in Celeste. (built-in into Studio)
- [.kkapture](https://github.com/DemoJameson/kkapture/wiki): High quality fixed framerate TAS encoder, Windows only.
- [ldcapture](https://github.com/psyGamer/ldcapture): High quality fixed framerate TAS encoder, Linux only.
