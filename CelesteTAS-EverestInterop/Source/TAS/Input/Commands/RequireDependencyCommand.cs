using System;
using System.Linq;
using System.Threading;
using Monocle;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using StudioCommunication;

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

        dummyCount++;
        // Everest will not load it if its name already exists
        EverestModuleMetadata dummy = new() {
            Name = $"{dummyName}({dummyCount})",
            Dependencies = [dependency],
            VersionString = "1.0.0"
        };
        Everest.Loader.LoadModDelayed(dummy, null);
        Engine.Scene.OnEndOfFrame += GotoOuiModOptions;

        AbortTas($"{dependency.Name} {(args.Length > 1 ? args[1] + " " : "")}is not loaded.", true);
    }

    private const string dummyName = "CelesteTAS - DependencyRequestor";

    private static int dummyCount = 0;

    public static void GotoOuiModOptions() {
        Engine.Scene = OverworldLoaderExt.FastGoto<OuiModOptions>();
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
