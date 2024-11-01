using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using JetBrains.Annotations;
using TAS.Communication;
using TAS.EverestInterop;
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
}

/// Invokes the target method when the module is loaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class LoadAttribute : Attribute;

/// Invokes the target method when the module is unloaded
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class UnloadAttribute : Attribute;

/// Invokes the target method when the module is initialized
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
internal class InitializeAttribute : Attribute;
