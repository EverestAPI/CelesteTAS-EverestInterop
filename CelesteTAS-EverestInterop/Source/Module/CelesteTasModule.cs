using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.Module;

// ReSharper disable once ClassNeverInstantiated.Global
public class CelesteTasModule : EverestModule {
    public CelesteTasModule() {
        Instance = this;
        AttributeUtils.CollectMethods<LoadAttribute>();
        AttributeUtils.CollectMethods<UnloadAttribute>();
        AttributeUtils.CollectMethods<LoadContentAttribute>();
        AttributeUtils.CollectMethods<InitializeAttribute>();
    }

    public static CelesteTasModule Instance { get; private set; }

    public override Type SettingsType => typeof(CelesteTasSettings);

    public override void Initialize() {
        AttributeUtils.Invoke<InitializeAttribute>();
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

    public override void LoadContent(bool firstLoad) {
        if (firstLoad) {
            AttributeUtils.Invoke<LoadContentAttribute>();
        }
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
        CreateModMenuSectionHeader(menu, inGame, snapshot);
        CelesteTasMenu.CreateMenu(this, menu, inGame);
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal class LoadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class UnloadAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class LoadContentAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
internal class InitializeAttribute : Attribute { }