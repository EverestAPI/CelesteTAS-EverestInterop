<h1 align="center">Celeste TAS <img id="logo" src="https://raw.githubusercontent.com/EverestAPI/CelesteTAS-EverestInterop/refs/heads/master/Assets/icon.png" width="20"/></h1>
<h3 align="center">Advanced TAS tools for Celeste / Everest</h3>

## Quick Start

To get started with TASing Celeste, you first need to make sure the [Everest Mod Loader](https://everestapi.github.io) is installed and up-to-date.  
After that you want to install the CelesteTAS mod, either by searching for in Olympus under "Download Mods" or by simply using the [2-click-installer](https://0x0a.de/twoclick/?github.com/EverestAPI/CelesteTAS-EverestInterop/releases/latest/download/CelesteTAS.zip).

If you now launch the game, Celeste Studio, our own purpose-built TAS editor, should've been installed automatically. On the main menu, you can go into the Mod Settings and under `Celeste TAS > More Options` you will find an option called `Launch Studio at Boot`. If you enable that setting, it should automatically open now, and also every time alongside the game being opened.  
If you wish to manually open it, you can find the program in the `CelesteStudio` folder inside your Celeste install.  
You can find the documentation about Celeste Studio on the [wiki](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Celeste-Studio). It is **strongly recommended** to at least check out the available [keyboard shortcuts](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Key-Bindings) which Studio offers.

Now that both CelesteTAS and Celeste Studio are set up, you can immediately start with TASing, by writing a [`console load` command](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Commands#console-load) for your desired level, followed by your inputs (see [here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Input-File) on how inputs are written).  
To play back your TAS (which you want to do _while_ TASing, not after), check out the available [keyboard controls](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Controls) for CelesteTAS 

Alternatively, you can find a (non-exhaustive!) list of community projects to which you might want to contribute [here](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Community-Projects).

> [!important]
> If you want know how something works, please **first** check if it is documented on the [CelesteTAS Wiki](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki).
> It contains both information about the tooling with CelesteTAS and Celeste Studio, but also references on how certain mechanics work to better understand them while TASing.
> The wiki is free for _anyone_ (who has a GitHub account) to contribute to, so if you feel like something is missing, wrong or just worded weirdly, don't shy away from editing it!
>
> If you're stuck with a problem _which isn't described in the wiki_, consider joining the [Celeste Discord](https://discord.gg/celeste) and asking in the `#tas_general` channel in the TASing category.

## Building CelesteTAS yourself

If you just want to use CelesteTAS and not modify it, you can skip this section.

To build CelesteTAS yourself, you just need to (recursively!) clone this repository into your Mods folder and then inside the repository's root run `dotnet build`.
Alternatively you can open the solution in your favourite C# IDE.
Note that the .NET 9 SDK or higher is required.

## Contributing

If you want to contribute to CelesteTAS or Celeste Studio (not the wiki, that is separate!), simply open a Pull Request with your desired changes.  
If you can't code you can still contribute by, for example, translating CelesteTAS into your language. (See `CelesteTAS-EverestInterop/Dialog` for dialog files).

## Credits

Many people have helped to bring CelesteTAS and the tools surrounding it into the amazing state they currently are. This list just highlights a few which have played a big role, but that doesn't mean other smaller contributions aren't appreciated!

- [Kilaye](https://github.com/clementgallet) (`@kilaye` on Discord): Developer of [libTAS](https://clementgallet.github.io/libTAS/) and among the first TASers of the game.
- [DevilSquirrel](https://github.com/ShootMe) (`@devilsquirrel` on Discord): Started development of the CelesteTAS project in 2018 and maintained it for the first year.
- [0x0ade](https://github.com/0x0ade) (`@0x0ade` on Discord): Helped with porting the mod to the Everest mod-loader, to avoid having to manually patch the game and allow for improved mod compatibility
- [EuniverseCat](https://github.com/EuniverseCat) (`@eunidiscriminator0317` on Discord): Occasional but active contributor the project from 2019 to 2023
- [DemoJameson](https://github.com/DemoJameson) (`@demojameson` on Discord): Maintainer of the project from 2020 to 2024, massively pushing it forward with new features and improvements
- [psyGamer](https://github.com/psyGamer) (`@psyGamer` on Discord): Current maintainer of the project since 2024, reworking Studio to be cross-platform and in general trying to modernize and improve the project.

## Additional Tools
- [Speedrun Tool](https://gamebanana.com/tools/6597): Provides savestate functionally to CelesteTAS to easily get back to a certain spot in the TAS without having to wait.
- [TAS Recorder](https://gamebanana.com/tools/14085): Create high quality fixed framerate video recordings of your TAS.
- [TAS Helper](https://gamebanana.com/tools/12383): Additional features which don't fit into the main CelesteTAS mod, but are still very useful while TASing.
- [GhostModForTas](https://gamebanana.com/mods/500759): Compare new TASes with old ones.
- Jadderline (integrated into Studio under `Tools`): Simple tool to generate inputs for a [Jelly Ladder](https://github.com/EverestAPI/CelesteTAS-EverestInterop/wiki/Holdable-Movement#jelly-ladder) from a certain stating condition.
- Featherline (integrated into Studio under `Tools`): Genetic algorithm to attempt to generate optimal inputs for feather movement.
- Radeline Simulator (integrated into Studio under `Tools`): Attempts to get the player into a desired specific position by brute-forcing input combinations.
- [Radeline Optimizer](https://github.com/Kataiser/radeline): Chaos monkey that optimizes a Celeste TAS by randomly (or sequentially) changing inputs.
- [Lobby Router](https://jakobhellermann.github.io/trout/): Helps find the fastest route for a collab lobby
