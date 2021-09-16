using System;
using Celeste;
using Celeste.Mod;
using FMOD.Studio;
using TAS.Communication;
using TAS.Utils;

namespace TAS.EverestInterop {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CelesteTasModule : EverestModule {
        public CelesteTasModule() {
            Instance = this;
            AttributeUtils.CollectMethods<LoadAttribute>();
            AttributeUtils.CollectMethods<UnloadAttribute>();
            AttributeUtils.CollectMethods<LoadContentAttribute>();
        }

        public static CelesteTasModule Instance { get; private set; }

        public override Type SettingsType => typeof(CelesteTasModuleSettings);
        public static CelesteTasModuleSettings Settings => (CelesteTasModuleSettings) Instance?._Settings;

        public override void Initialize() {
            StudioHelper.Initialize();
        }

        public override void Load() {
            AttributeUtils.Invoke<LoadAttribute>();
        }

        public override void Unload() {
            AttributeUtils.Invoke<UnloadAttribute>();
            StudioCommunicationClient.Destroy();
        }

        public override void LoadContent(bool firstLoad) {
            if (firstLoad) {
                AttributeUtils.Invoke<LoadContentAttribute>();

                // Open memory mapped file for interfacing with Windows Celeste Studio
                StudioCommunicationClient.Run();
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

    [AttributeUsage(AttributeTargets.Method)]
    internal class LoadContentAttribute : Attribute { }
}