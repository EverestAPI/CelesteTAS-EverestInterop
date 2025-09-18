using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TAS.Module;

namespace TAS.Input.Commands;

internal static class RequireDependencyCommand {

    internal const string CommandName = "RequireDependency";
    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Mod Name]{CommandInfo.Separator}[1;(Version)]";

        public bool HasArguments => true;
    }

    // "RequireDependency, StrawberryJam2021",
    // "RequireDependency, StrawberryJam2021, 1.0.9"


    [TasCommand(CommandName, ExecuteTiming = ExecuteTiming.Runtime, LegalInFullGame = false, CalcChecksum = false, MetaDataProvider = typeof(Meta))]
    private static void RequireDependency(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        // if not found, then we goto mod options menu, and let MODOPTIONS_COREMODULE_DOWNLOADDEPS appear, ask Everest to download the dependency as described in the tas file
        // it seems this needs Everest.Flags.SupportRuntimeMods = true
        string[] args = commandLine.Arguments;
        if (args.Length == 0) {
            return;
        }

        EverestModuleMetadata dependency = new() {
            Name = args[0],
            VersionString = args.Length > 1 ? args[1] : "0.0.1"
        };

        if (Everest.Modules.Where(module => module.Metadata.Name == dependency.Name) is { } matches && matches.Any()) {
            if (args.Length == 1) {
                return;
            }
            foreach (EverestModule installed in matches) {
                if (Everest.Loader.VersionSatisfiesDependency(dependency.Version, installed.Metadata.Version)) {
                    return;
                }
            }
        }

        string dependencyNameWithVersion = args.Length > 1 ? $"{dependency.Name} {args[1]}" : dependency.Name;
        InstallDependencyUI.Create(dependency, dependencyNameWithVersion);

        AbortTas($"{dependencyNameWithVersion} is not loaded.", true);
    }

    internal static class InstallDependencyUI {

        // there will be some issue when RequireDependency wants to install and Console load command succeeds in same frame
        // luckily this only happens when their parameters are not the same map

        private const string dummyName = "CelesteTAS - DependencyRequestor";

        private static int dummyCount = 0;
        internal static void Create(EverestModuleMetadata dependency, string modName) {
            if (Engine.Scene is not Level level) {
                GotoInstall(dependency);
                return;
            }

            level.wasPaused = true;
            if (!level.Paused) {
                level.StartPauseEffects();
            }
            else if (level.Entities.FindFirst<TextMenu>() is TextMenu textMenu) {
                textMenu.Close();
            }
            level.Paused = true;
            level.PauseMainMenuOpen = false;
            TextMenu menu = new TextMenu {
                Justify = new Vector2(0.5f, 0.8f)
            };
            menu.OnESC = menu.OnCancel = menu.OnPause = () => {
                menu.RemoveSelf();
                level.Paused = false;
                Audio.Play("event:/ui/game/unpause");
                level.unpauseTimer = 0.15f;
            };
            menu.Add(new TextMenu.Header("Install Dependency".ToDialogText()));
            menu.Add(new TextMenu.SubHeader(modName, false));
            menu.Add(new TextMenuExt.SubHeaderExt("") { HeightExtra = 30f });
            menu.Add(new TextMenu.Button(Dialog.Clean("menu_return_continue")).Pressed(() => {
                GotoInstall(dependency);
            }));
            menu.Add(new TextMenu.Button(Dialog.Clean("menu_return_cancel")).Pressed(menu.OnCancel));
            level.Add(menu);
        }

        internal static void GotoInstall(EverestModuleMetadata metadata) {
            if (ToInstalledMods.TryGetValue(metadata.Name, out Version version) && metadata.Version <= version) {
                Engine.Scene.OnEndOfFrame += GotoOuiModOptions;
                return;
            }

            ToInstalledMods[metadata.Name] = metadata.Version;

            // Everest will not load it if its name already exists, so we make sure its name is unique
            dummyCount++;
            EverestModuleMetadata dummy = new() {
                Name = $"{dummyName}({dummyCount})",
                Dependencies = [metadata],
                VersionString = "1.0.0"
            };
            Everest.Loader.LoadModDelayed(dummy, null);
            Engine.Scene.OnEndOfFrame += GotoOuiModOptions;
        }

        internal static void GotoOuiModOptions() {
            Engine.Scene = OverworldLoaderExt.FastGoto<OuiModOptions>();
        }

        private static readonly Dictionary<string, Version> ToInstalledMods = new();
    }
}

internal class OverworldLoaderExt : OverworldLoader {

    public Action<Overworld> overworldFirstAction;
    public OverworldLoaderExt(Overworld.StartMode startMode, HiresSnow snow = null) : base(startMode, snow) {
        Snow = null;
        fadeIn = false;
    }

    public static OverworldLoaderExt FastGoto<T>() where T : Oui {
        return new OverworldLoaderExt(Overworld.StartMode.MainMenu, null).SetOverworldAction(x => x.Goto<T>());
    }

    public override void Begin() {
        Add(new HudRenderer());
        /*
        Add(Snow);
        if (fadeIn) {
            ScreenWipe.WipeColor = Color.Black;
            new FadeWipe(this, wipeIn: true);
        }
        */
        base.RendererList.UpdateLists();
        Session session = null;
        if (SaveData.Instance != null) {
            session = SaveData.Instance.CurrentSession_Safe;
        }
        Entity entity = new Entity {
            new Coroutine(Routine(session))
        };
        Add(entity);
        activeThread = Thread.CurrentThread;
        activeThread.Priority = ThreadPriority.Lowest;
        RunThread.Start(LoadThreadExt, "OVERWORLD_LOADER_EXT", highPriority: true);
    }

    private void LoadThreadExt() {
        base.LoadThread();
        overworldFirstAction?.Invoke(overworld);
    }

    public OverworldLoaderExt SetOverworldAction(Action<Overworld> action) {
        overworldFirstAction = action;
        return this;
    }
}
