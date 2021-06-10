# CelesteTAS

## TAS tools mod in Everest

### License: MIT

----

- Install [Everest](https://everestapi.github.io/) if you haven't already.
- (Recommended) Use the 1-click installer [here](https://gamebanana.com/tools/6715). (Alternatively) [Download the latest autobuild](https://0x0ade.ga/twoclick/?nightly.link/EverestAPI/CelesteTAS-EverestInterop/workflows/NetFramework.Legacy.CI/master/CelesteTAS.zip)
  and put it in the game_path/mods folder.
- Enable the mod in the in-game mod options.
- Open `Celeste Studio.exe`, our input editor. It should be in your main Celeste directory, if not please extract it from the `Mods/CelesteTAS.zip` yourself. (Note that Studio only works on Windows) [Studio documentation can be found here.](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/Docs/Studio.md)
- If on Linux or macOS, check the working TAS file path in the `Mod Options -> Enabled` menu item, edit it with your favorite text editor, and enable `Info HUD` to show the auxiliary information

## Input File

The input file should be called Celeste.tas and needs to be in the main Celeste directory. Celeste Studio will automatically create this file for you. The tools
will not work if there are no inputs in this file.

Format for the input file is (Frames),(Actions)

e.g. 123,R,J (For 123 frames, hold Right and Jump)

## Actions Available

- R = Right
- L = Left
- U = Up
- D = Down
- J = Jump / Confirm
- K = Jump Bind 2
- X = Dash / Talk / Cancel
- C = Dash Bind 2 / Cancel
- Z = Crouch Dash (Celeste beta versions only)
- G = Grab
- S = Pause
- Q = Quick Reset
- F = Feather Aim
<br>Format: F, angle, optional upper limit of single axis (default value is 1, range is 0.5 - 1, only works in precise [AnalogMode](Docs/Commands.md#analoguemode)
- O = Confirm
- N = Journal (Used only for Cheat Code)

## Controls

While in game or in Studio:

- Start/Stop Playback: RightControl
- Restart Playback: Equals
- Fast Forward / Frame Advance Continuously: RightShift
- Pause / Frame Advance: [
- Unpause: ]
- Toggle Hitboxes: B
- Toggle Simplified Graphics: N
- Toggle Center Camera: M
- Save State: RightAlt + Minus ([Experimental](#savestate))
- Clear State: RightAlt + Back ([Experimental](#savestate))

- These can be rebound in Mod Options (Note that controller is not supported.)
    - You will have to rebind some of these if you are on a non-US keyboard layout.
    - Binding multiple keys to a control will cause those keys to act as a keycombo.

## Special Input

### Breakpoints

- You can create a breakpoint in the input file by typing `***` by itself on a single line
- The program when played back from the start will fast forward until it reaches that line and then go into frame stepping mode
- `***S` will make a savestate, which can reduce TAS playback time. ([Experimental](#savestate))
- You can specify the speed with `***X`, where `X` is the speedup factor. e.g. `***10` will go at 10x speed

### Commands

- Various commands exist to facilitate TAS
  playback. [Documentation can be found here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/master/Docs/Commands.md).

## Savestate

- Savestates are experimental and require the [SpeedrunTool](https://gamebanana.com/tools/6597) mod.
- Reliable in vanilla maps.
- Savestates may not work properly in custom maps that use code mods. Placing a savestate right before leaving a room can help with this.
- Currently cannot savestate while skipping a cutscene or during the death animation.
- Crashes due to running out of memory are possible, although uncommon.

## Misc

### Inspect Entity

Enable `Info HUD`, holding left ctrl then left-click to add the entity to be inspected, right-click to clear the inspecting entities. Supports exporting inspecting entities info via
the `StartExport` command.

### Custom Info

The contents of the curly brackets will be converted to actual data, here are some examples:

- `{EntityClassName.field...}` find the first entity. e.g. `{Strawberry.ExactPosition}`
- `{EntityClassName[entityId].field...}` Find the entity with the specified entityId. e.g. `{Strawberry[1:12].Position}` means 1A gold berry. You can get the entityId by opening the console and clicking on the entity with the mouse
- `{ClassName.staticField.field...}`
- `Wind: {Level.Wind}`
- `AutoJump: {Player.AutoJump} ({Player.AutoJumpTimer.toFrame()})`
- `ForceMoveX: {Player.forceMoveX} ({Player.forceMoveXTimer.toFrame()})`
- `Theo: {TheoCrystal.ExactPosition}`
- `TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}`
- `CustomSpinner: {FrostHelper.CustomSpinner@FrostTempleHelper.Position}`
