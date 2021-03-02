using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
    public class CelesteTasModuleSettings : EverestModuleSettings {
        private bool centerCamera = false;
        public bool Enabled { get; set; } = true;

        [SettingName("TAS_KEY_START")]
        [DefaultButtonBinding(0, Keys.RightControl)]
        public ButtonBinding KeyStart { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_RESTART")]
        [DefaultButtonBinding(0, Keys.OemPlus)]
        public ButtonBinding KeyRestart { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_FAST_FORWARD")]
        [DefaultButtonBinding(0, Keys.RightShift)]
        public ButtonBinding KeyFastForward { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_FRAME_ADVANCE")]
        [DefaultButtonBinding(0, Keys.OemOpenBrackets)]
        public ButtonBinding KeyFrameAdvance { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_PAUSE")]
        [DefaultButtonBinding(0, Keys.OemCloseBrackets)]
        public ButtonBinding KeyPause { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_HITBOXES")]
        [DefaultButtonBinding(0, Keys.B)]
        public ButtonBinding KeyHitboxes { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_TRIGGER_HITBOXES")]
        [DefaultButtonBinding(0, Keys.LeftAlt)]
        public ButtonBinding KeyTriggerHitboxes { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_GRAPHICS")]
        [DefaultButtonBinding(0, Keys.N)]
        public ButtonBinding KeyGraphics { get; set; } = new ButtonBinding();

        [SettingName("TAS_KEY_CAMERA")]
        [DefaultButtonBinding(0, Keys.M)]
        public ButtonBinding KeyCamera { get; set; } = new ButtonBinding();

        // Multiple default keys are not supported, handled by Hotkeys.ModReload()
        [SettingName("TAS_KEY_SAVE_STATE")]
        [DefaultButtonBinding(0, Keys.OemMinus)]
        public ButtonBinding KeySaveState { get; set; } = new ButtonBinding();

        // Multiple default keys are not supported, handled by Hotkeys.ModReload()
        [SettingName("TAS_KEY_CLEAR_STATE")]
        [DefaultButtonBinding(0, Keys.Back)]
        public ButtonBinding KeyClearState { get; set; } = new ButtonBinding();

        public bool ShowHitboxes {
            get => GameplayRendererExt.RenderDebug;
            set => GameplayRendererExt.RenderDebug = value;
        }

        public bool CenterCamera {
            get => Enabled && centerCamera;
            set => centerCamera = value;
        }

        public bool DisableAchievements { get; set; } = false;

        [SettingNeedsRelaunch] public bool UnixRtc { get; set; } = false;

        [SettingNeedsRelaunch] public bool LaunchStudioAtBoot { get; set; } = false;

        public bool Mod9DLighting { get; set; } = false;

        public bool RoundPosition { get; set; } = true;

        [SettingIgnore] public bool FastForwardCallBase { get; set; } = false;
        [SettingIgnore] public int FastForwardThreshold { get; set; } = 10;
        [SettingIgnore] public DateTime StudioLastModifiedTime { get; set; } = new DateTime();
        public bool AutoExtractNewStudio { get; set; } = true;
        [SettingIgnore] public Color EntityHitboxColor { get; set; } = HitboxColor.DefaultEntityColor;
        [SettingIgnore] public Color TriggerHitboxColor { get; set; } = HitboxColor.DefaultTriggerColor;
        [SettingIgnore] public Color SolidTilesHitboxColor { get; set; } = HitboxColor.DefaultSolidTilesColor;
        public bool HideTriggerHitboxes { get; set; } = false;
        public bool SimplifiedHitboxes { get; set; } = true;
        public ActualCollideHitboxTypes ShowActualCollideHitboxes { get; set; } = ActualCollideHitboxTypes.Off;
        public InfoPositions InfoHud { get; set; } = InfoPositions.Off;
        public bool PauseAfterLoadState { get; set; } = true;

        public bool RestoreSettings { get; set; } = false;
        [SettingIgnore] public bool FirstLaunch { get; set; } = true;

        #region SimplifiedGraphics

        private bool simplifiedGraphics = false;

        public bool SimplifiedGraphics {
            get => Enabled && simplifiedGraphics;
            set => simplifiedGraphics = value;
        }

        private bool hideGamePlayer;

        public bool HideGameplay {
            get => hideGamePlayer;
            set => hideGamePlayer = ShowHitboxes = value;
        }

        public int? SimplifiedLighting { get; set; } = 10;
        public int? SimplifiedBloomBase { get; set; } = 0;
        public int? SimplifiedBloomStrength { get; set; } = 1;
        public SimplifiedGraphicsFeature.SpinnerColor SimplifiedSpinnerColor { get; set; } = SimplifiedGraphicsFeature.SpinnerColor.All[1];
        public Color SimplifiedDustSpriteColor { get; set; } = Color.Transparent;
        public bool SimplifiedSpotlightWipe { get; set; } = true;
        public bool SimplifiedColorGrade { get; set; } = true;
        public bool SimplifiedBackdrop { get; set; } = true;
        public bool SimplifiedDecal { get; set; } = true;
        public bool SimplifiedParticle { get; set; } = true;
        public bool SimplifiedDistort { get; set; } = true;
        public bool SimplifiedMiniTextbox { get; set; } = true;
        public bool SimplifiedDreamBlock { get; set; } = true;
        public bool SimplifiedLava { get; set; } = true;
        public bool SimplifiedLightning { get; set; } = true;

        #endregion
    }
}