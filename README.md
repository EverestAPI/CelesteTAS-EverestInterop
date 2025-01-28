# CelesteTAS

## TAS tools for Celeste / Everest

### License: MIT

---

- Install [Everest](https://everestapi.github.io/) if you haven't already.
- (Recommended) Use the 1-click installer [here](https://gamebanana.com/tools/6715). (Alternatively) [Download the latest auto-build](https://0x0a.de/twoclick/?nightly.link/EverestAPI/CelesteTAS-EverestInterop/workflows/Build/master/CelesteTAS.zip).
- Make sure the mod is enabled in the in-game mod options.
- Enable the mod-setting `Celeste TAS > More Options > Launch Studio at Boot`. Celeste Studio, our input editor, should now automatically launch. (You can find the Studio documentation [here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Celeste-Studio))
- Alternatively, you can start Celeste Studio directly. It'll be installed in the `CelesteStudio` directory inside your Celeste install. 
- You can find the most up-to-date input files [here](https://github.com/VampireFlower/CelesteTAS).

## Documentation

You can find documentation around CelesteTAS and Celeste Studio, as well as general TASing references on the [wiki](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki).  
If you want to contribute to tooling documentation or TASing references, feel free to edit the wiki!

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

## Other Useful Tools
- [TAS Recorder](https://gamebanana.com/tools/14085): High quality fixed framerate TAS encoder, cross-platform (use this instead of .kkapture or ldcapture) 
- [GhostMod](https://gamebanana.com/mods/500759): Compare new TASes with old ones.
- [Radeline](https://github.com/Kataiser/radeline): Chaos monkey that optimizes a Celeste TAS by randomly (or sequentially) changing inputs.
- [Lobby Router](https://jakobhellermann.github.io/trout/): Helps find the fastest route for a collab lobby
- [Featherline](https://github.com/tntfalle/featherline): Algorithm for analog feather movement in Celeste. (built-in into Studio)
- [.kkapture](https://github.com/DemoJameson/kkapture/wiki): High quality fixed framerate TAS encoder, Windows only.
- [ldcapture](https://github.com/psyGamer/ldcapture): High quality fixed framerate TAS encoder, Linux only.
