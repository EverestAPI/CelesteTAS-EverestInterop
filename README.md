# CelesteTAS

## TAS tools mod in Everest

### License: MIT

----
- Install [Everest](https://everestapi.github.io/) if you haven't already.
- Use the 1-click installer [here.](https://gamebanana.com/tools/6715)
- Download Celeste Studio, our input editor, further down on the same page. (Note that Studio is not supported for Mac, and old versions only run on Windows)
- Enable the mod in the in-game mod options.
- Enable `Unix RTC` in the mod settings and restart if on linux.

## Input File
Input file is called Celeste.tas and needs to be in the main Celeste directory (usually C:\Program Files (x86)\Steam\steamapps\common\Celeste\Celeste.tas) Celeste Studio will automatically create this file for you.

Format for the input file is (Frames),(Actions)

e.g. 123,R,J (For 123 frames, hold Right and Jump)

## Actions Available
- R = Right
- L = Left
- U = Up
- D = Down
- J = Jump / Confirm
- K = Jump Bind 2
- X = Dash / Talk
- C = Dash Bind 2
- G = Grab
- S = Pause
- Q = Quick Reset
- F = Feather Aim (Format: F,angle)
- O = Confirm
- N = Journal (Used only for Cheat Code)

## Controls
While in game or in Studio:
- Start/Stop Playback: RightControl
- Fast Forward / Frame Advance Continuously: RightShift
- Pause / Frame Advance: [
- Unpause: ]
- Toggle Hitboxes: B
- Toggle Simplified Graphics: N
- Toggle Center Camera: M
- Save State: LeftShift + F1 (Experimental)
- Load State: F1 (Experimental)

- These can be rebound in (Main Celeste Directory)\Saves\modsettings-CelesteTAS.celeste
  - You will have to rebind some of these if you are on a non-US keyboard layout.
  - Note that you may have to reload Mod Settings in Celeste for this file to appear.
  - You can also set hotkeys for modifying TAS options (e.g. showing hitboxes) in this file.
  - You can also set a default path for TAS files to be read from. (We recommend setting this to the LevelFiles folder in this repo.)
  
## Special Input
### Breakpoints
- You can create a breakpoint in the input file by typing *** by itself on a single line
- The program when played back from the start will fast forward until it reaches that line and then go into frame stepping mode
- You can specify the speed with ***X, where X is the speedup factor. e.g. ***10 will go at 10x speed
- ***! will force the TAS to pause even if there are breakpoints afterward in the file

### Commands
- Various commands exist to facilitate TAS playback. Documentation can be found [here.](https://github.com/ShootMe/CelesteTAS/blob/master/Game/Commands.md)
  
## Celeste Studio
Can be used instead of notepad or similar for easier editing of the TAS file. Is located in [Releases](https://github.com/ShootMe/CelesteTAS/releases) as well.

If Celeste.exe is running it will automatically open Celeste.tas if it exists. You can hit Ctrl+O to open a different file, which will automatically save it to Celeste.tas as well. Ctrl+Shift+S will open a Save As dialog as well.

