using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using TAS.Communication;
using TAS.EverestInterop.Hitboxes;
using TAS.EverestInterop.InfoHUD;
using TAS.Input;
using TAS.Utils;

namespace TAS.EverestInterop {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CelesteTasModule : EverestModule {
        public CelesteTasModule() {
            Instance = this;
            AttributeUtils.CollectMethods<LoadAttribute>();
            AttributeUtils.CollectMethods<UnloadAttribute>();
        }

        public static CelesteTasModule Instance { get; private set; }

        public override Type SettingsType => typeof(CelesteTasModuleSettings);
        public static CelesteTasModuleSettings Settings => (CelesteTasModuleSettings)Instance?._Settings;

        public override void Initialize() {
            StudioHelper.Initialize();
        }

        public override void Load() {
            Hotkeys.Load();
            Core.Load();
            FastForwardBoost.Load();
            DisableAchievements.Load();
            GraphicsCore.Load();
            SimplifiedGraphicsFeature.Load();
            CenterCamera.Load();
            AutoMute.Load();
            HideGameplay.Load();
            HitboxTweak.Load();
            InfoHud.Load();
            ConsoleEnhancements.Load();

            AttributeUtils.Invoke<LoadAttribute>();

            // Open memory mapped file for interfacing with Windows Celeste Studio
            StudioCommunicationClient.Run();
        }

        public override void Unload() {
            Hotkeys.Unload();
            Core.Unload();
            FastForwardBoost.Unload();
            DisableAchievements.Unload();
            GraphicsCore.Unload();
            SimplifiedGraphicsFeature.Unload();
            CenterCamera.Unload();
            AutoMute.Unload();
            HideGameplay.Unload();
            HitboxTweak.Unload();
            InfoHud.Unload();
            ConsoleEnhancements.Unload();
            StudioCommunicationClient.Destroy();

            AttributeUtils.Invoke<UnloadAttribute>();

#if DEBUG
            Benchmark.Unload();
#endif
        }

        public override void LoadContent(bool firstLoad) {
            if (firstLoad) {
                TasCommandAttribute.CollectMethods();
                InfoCustom.CollectAllTypeInfo();
                SimplifiedGraphicsFeature.OnLoadContent();
            }
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, EventInstance snapshot) {
            CreateModMenuSectionHeader(menu, inGame, snapshot);
            Menu.CreateMenu(this, menu, inGame);
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class LoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    internal class UnloadAttribute : Attribute { }
}