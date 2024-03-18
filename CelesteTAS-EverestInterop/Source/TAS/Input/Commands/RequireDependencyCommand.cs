using System;
using System.Linq;
using System.Threading;
using Monocle;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.UI;
using Celeste.Mod.Meta;
using TAS.Utils;
using System.Collections.Generic;

namespace TAS.Input.Commands;

public static class RequireDependencyCommand {

    public const string CommandName = "RequireDependency";
    // "RequireDependency, StrawberryJam2021",
    // "RequireDependency, StrawberryJam2021, 1.0.9"


    [TasCommand(CommandName, ExecuteTiming = ExecuteTiming.Runtime, LegalInMainGame = false, CalcChecksum = false)]
    private static void RequireDependency(string[] args) {
        // if not found, then we goto mod options menu, and let MODOPTIONS_COREMODULE_DOWNLOADDEPS appear, ask Everest to download the dependency as described in the tas file
        // it seems this needs Everest.Flags.SupportRuntimeMods = true
        if (args.IsEmpty()) return;
        EverestModuleMetadata dependency = new();
        dependency.Name = args[0];
        dependency.VersionString = args.Length > 1 ? args[1] : "0.0.1";

        if (Everest.Modules.Where(module => module.Metadata.Name == dependency.Name) is { } matches && matches.Count() > 0) {
            if (args.Length == 1) {
                return;
            }
            foreach (EverestModule installed in matches) {
                if (Everest.Loader.VersionSatisfiesDependency(dependency.Version, installed.Metadata.Version)) {
                    return;
                }
            }
        }

        EverestModuleMetadata dummy = new();
        // Everest will not load it if its name already exists
        dummyCount++;
        dummy.Name = $"{dummyName}({dummyCount})";
        dummy.Dependencies = new List<EverestModuleMetadata> { dependency };
        dummy.VersionString = "1.0.0";
        Everest.Loader.LoadModDelayed(dummy, null);
        Engine.Scene.OnEndOfFrame += GotoOuiModOptions;

        AbortTas($"{dependency.Name} {(args.Length > 1 ? args[1] + " " : "")}is not loaded.", true);
    }

    private static string dummyName = "CelesteTAS - DependencyRequestor";

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