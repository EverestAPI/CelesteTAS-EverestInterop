using System;
using System.IO;
using System.IO.Pipes;
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
        public NamedPipeServerStream UnixRtc;
        public StreamReader UnixRtcStreamIn;
        public StreamWriter UnixRtcStreamOut;

        public CelesteTasModule() {
            Instance = this;
            AttributeUtils.CollectMethods<LoadAttribute>();
            AttributeUtils.CollectMethods<UnloadAttribute>();
        }

        public static CelesteTasModule Instance { get; private set; }

        public override Type SettingsType => typeof(CelesteTasModuleSettings);
        public static CelesteTasModuleSettings Settings => (CelesteTasModuleSettings) Instance?._Settings;
        public static bool UnixRtcEnabled => (Environment.OSVersion.Platform == PlatformID.Unix) && Settings.UnixRtc;

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

            // Open unix IO pipe for interfacing with Linux / Mac Celeste Studio
            if (UnixRtcEnabled) {
                File.Delete("/tmp/celestetas");
                UnixRtc = new NamedPipeServerStream("/tmp/celestetas", PipeDirection.InOut);
                UnixRtc.WaitForConnection();
                UnixRtcStreamOut = new StreamWriter(UnixRtc);
                UnixRtcStreamIn = new StreamReader(UnixRtc);
                Logger.Log("CelesteTAS", "Unix socket is active on /tmp/celestetas");
            }

            // Open memory mapped file for interfacing with Windows Celeste Studio
            StudioCommunicationClient.Run();

#if DEBUG
            Benchmark.Load();
#endif
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

            UnixRtc?.Dispose();

#if DEBUG
            Benchmark.Unload();
#endif
        }

        public override void LoadContent(bool firstLoad) {
            if (firstLoad) {
                TasCommandAttribute.CollectMethods();
                InfoCustom.CollectAllTypeInfo();
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