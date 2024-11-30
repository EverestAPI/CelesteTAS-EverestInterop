using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using System.Collections.Generic;
using TAS.Communication;
using TAS.EverestInterop;
using TAS.SyncCheck;
using TAS.Utils;

namespace TAS.Module;

// ReSharper disable once ClassNeverInstantiated.Global
public class CelesteTasModule : EverestModule {
    public CelesteTasModule() {
        Instance = this;
        AttributeUtils.CollectMethods<LoadAttribute>();
        AttributeUtils.CollectMethods<UnloadAttribute>();
        AttributeUtils.CollectMethods<InitializeAttribute>();
    }

    public static CelesteTasModule Instance { get; private set; }

    public override Type SettingsType => typeof(CelesteTasSettings);

    public override void Initialize() {
        AttributeUtils.Invoke<InitializeAttribute>();

        // required run after TasCommandAttribute.CollectMethods()
        if (TasSettings.AttemptConnectStudio) {
            CommunicationWrapper.Start();
        }
    }

    public override void Load() {
        AttributeUtils.Invoke<LoadAttribute>();
        // avoid issues if center camera is enabled, hook at he end
        CenterCamera.Load();
    }

    public override void Unload() {
        AttributeUtils.Invoke<UnloadAttribute>();
        CenterCamera.Unload();
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);
        CelesteTasMenu.CreateMenu(this, menu, inGame);
    }

    public override bool ParseArg(string arg, Queue<string> args) {
        if (arg == "--sync-check") {
            if (args.TryDequeue(out string path)) {
                SyncChecker.AddFile(path);
            } else {
                "Expected file path after --sync-check CLI argument".Log(LogLevel.Error);
            }
            return true;
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal class LoadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class UnloadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class InitializeAttribute : Attribute { }
