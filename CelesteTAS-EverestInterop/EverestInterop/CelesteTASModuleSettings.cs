using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using TAS.EverestInterop.Hitboxes;
using TAS.EverestInterop.InfoHUD;

namespace TAS.EverestInterop {
    public class CelesteTasModuleSettings : EverestModuleSettings {
        private static bool initialize;

        [Load]
        private static void Load() {
            initialize = true;
        }

        public bool Enabled { get; set; } = true;

        #region Hitboxes

        private bool showHitboxes;

        public bool ShowHitboxes {
            get => Enabled && showHitboxes || !ShowGameplay;
            set => showHitboxes = value;
        }

        [SettingIgnore] public Color EntityHitboxColor { get; set; } = HitboxColor.DefaultEntityColor;
        [SettingIgnore] public Color TriggerHitboxColor { get; set; } = HitboxColor.DefaultTriggerColor;
        [SettingIgnore] public Color PlatformHitboxColor { get; set; } = HitboxColor.DefaultPlatformColor;
        public bool ShowTriggerHitboxes { get; set; } = true;
        public bool SimplifiedHitboxes { get; set; } = true;
        public ActualCollideHitboxTypes ShowActualCollideHitboxes { get; set; } = ActualCollideHitboxTypes.Off;

        #endregion

        #region HotKey

        [SettingName("TAS_KEY_START_STOP")]
        [DefaultButtonBinding(0, Keys.RightControl)]
        public ButtonBinding KeyStart { get; set; } = new(0, Keys.RightControl);

        [SettingName("TAS_KEY_RESTART")]
        [DefaultButtonBinding(0, Keys.OemPlus)]
        public ButtonBinding KeyRestart { get; set; } = new(0, Keys.OemPlus);

        [SettingName("TAS_KEY_FAST_FORWARD")]
        [DefaultButtonBinding(0, Keys.RightShift)]
        public ButtonBinding KeyFastForward { get; set; } = new(0, Keys.RightShift);

        [SettingName("TAS_KEY_FRAME_ADVANCE")]
        [DefaultButtonBinding(0, Keys.OemOpenBrackets)]
        public ButtonBinding KeyFrameAdvance { get; set; } = new(0, Keys.OemOpenBrackets);

        [SettingName("TAS_KEY_PAUSE_RESUME")]
        [DefaultButtonBinding(0, Keys.OemCloseBrackets)]
        public ButtonBinding KeyPause { get; set; } = new(0, Keys.OemCloseBrackets);

        [SettingName("TAS_KEY_HITBOXES")]
        [ExtraDefaultKey(Keys.LeftControl)]
        [DefaultButtonBinding(0, Keys.B)]
        public ButtonBinding KeyHitboxes { get; set; } = new(0, Keys.LeftControl, Keys.B);

        [SettingName("TAS_KEY_TRIGGER_HITBOXES")]
        [ExtraDefaultKey(Keys.LeftAlt)]
        [DefaultButtonBinding(0, Keys.T)]
        public ButtonBinding KeyTriggerHitboxes { get; set; } = new(0, Keys.LeftAlt, Keys.T);

        [SettingName("TAS_KEY_SIMPLIFIED_GRAPHICS")]
        [ExtraDefaultKey(Keys.LeftControl)]
        [DefaultButtonBinding(0, Keys.N)]
        public ButtonBinding KeyGraphics { get; set; } = new(0, Keys.LeftControl, Keys.N);

        [SettingName("TAS_KEY_CENTER_CAMERA")]
        [ExtraDefaultKey(Keys.LeftControl)]
        [DefaultButtonBinding(0, Keys.M)]
        public ButtonBinding KeyCamera { get; set; } = new(0, Keys.LeftControl, Keys.M);

        [SettingName("TAS_KEY_SAVE_STATE")]
        [ExtraDefaultKey(Keys.RightAlt)]
        [DefaultButtonBinding(0, Keys.OemMinus)]
        public ButtonBinding KeySaveState { get; set; } = new(0, Keys.RightAlt, Keys.OemMinus);

        [SettingName("TAS_KEY_CLEAR_STATE")]
        [ExtraDefaultKey(Keys.RightAlt)]
        [DefaultButtonBinding(0, Keys.Back)]
        public ButtonBinding KeyClearState { get; set; } = new(0, Keys.RightAlt, Keys.Back);

        [SettingName("TAS_KEY_INFO_HUD")]
        [DefaultButtonBinding(0, Keys.LeftControl)]
        public ButtonBinding KeyInfoHud { get; set; } = new(0, Keys.LeftControl);

        #endregion

        #region SimplifiedGraphics

        private bool simplifiedGraphics;

        public bool SimplifiedGraphics {
            get => Enabled && simplifiedGraphics;
            set => simplifiedGraphics = value;
        }

        private bool showGameplay = true;

        public bool ShowGameplay {
            get => showGameplay || !SimplifiedGraphics;
            set => showGameplay = value;
        }

        public int? SimplifiedLighting { get; set; } = 10;
        public int? SimplifiedBloomBase { get; set; } = 0;
        public int? SimplifiedBloomStrength { get; set; } = 1;
        public SimplifiedGraphicsFeature.SpinnerColor SimplifiedSpinnerColor { get; set; } = SimplifiedGraphicsFeature.SpinnerColor.All[1];
        public bool SimplifiedDustSpriteEdge { get; set; } = true;
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
        public bool SimplifiedLightningStrike { get; set; } = true;

        #endregion

        #region Info HUD

        public bool InfoHud { get; set; } = false;
        public bool InfoGame { get; set; } = true;
        public bool InfoTasInput { get; set; } = true;
        public bool InfoSubPixelIndicator { get; set; } = true;
        public HudOptions InfoCustom { get; set; } = HudOptions.Off;
        public HudOptions InfoWatchEntity { get; set; } = HudOptions.Both;
        public WatchEntityTypes InfoWatchEntityType { get; set; } = WatchEntityTypes.Position;

        [SettingIgnore]
        public string InfoCustomTemplate { get; set; } =
            "Wind: {Level.Wind}\n" +
            "AutoJump: {Player.AutoJump} ({Player.AutoJumpTimer.toFrame()})\n" +
            "ForceMoveX: {Player.forceMoveX} ({Player.forceMoveXTimer.toFrame()})\n" +
            "Theo: {TheoCrystal.ExactPosition}\n" +
            "TheoCantGrab: {TheoCrystal.Hold.cannotHoldTimer.toFrame()}";

        [SettingIgnore] public Vector2 InfoPosition { get; set; } = Vector2.Zero;
        [SettingIgnore] public int InfoTextSize { get; set; } = 10;
        [SettingIgnore] public int InfoSubPixelIndicatorSize { get; set; } = 10;
        [SettingIgnore] public int InfoOpacity { get; set; } = 6;
        [SettingIgnore] public int InfoMaskedOpacity { get; set; } = 4;

        #endregion

        #region Round Values

        private bool roundPosition = true;
        private bool roundSpeed = true;
        private bool roundVelocity = true;
        private bool roundCustomInfo = true;
        private SpeedUnit speedUnit = SpeedUnit.PixelPerSecond;

        public bool RoundPosition {
            get => roundPosition;
            set {
                roundPosition = value;
                GameInfo.Update();
            }
        }

        public bool RoundSpeed {
            get => roundSpeed;
            set {
                roundSpeed = value;
                GameInfo.Update();
            }
        }

        public bool RoundVelocity {
            get => roundVelocity;
            set {
                roundVelocity = value;
                GameInfo.Update();
            }
        }

        public bool RoundCustomInfo {
            get => roundCustomInfo;
            set {
                roundCustomInfo = value;
                GameInfo.Update();
            }
        }

        public SpeedUnit SpeedUnit {
            get => speedUnit;
            set {
                speedUnit = value;
                GameInfo.Update();
            }
        }

        #endregion

        #region Relaunch Required

        [SettingNeedsRelaunch] public bool LaunchStudioAtBoot { get; set; } = false;
        public bool AutoExtractNewStudio { get; set; } = true;

        #endregion

        #region More Options

        private bool centerCamera;

        public bool CenterCamera {
            get => Enabled && centerCamera;
            set => centerCamera = value;
        }

        public bool PauseAfterLoadState { get; set; } = true;
        public bool RestoreSettings { get; set; } = false;
        public bool DisableAchievements { get; set; } = false;
        public bool Mod9DLighting { get; set; } = false;

        #endregion

        # region Ignore

        [SettingIgnore] public bool FastForwardCallBase { get; set; } = false;
        [SettingIgnore] public int FastForwardThreshold { get; set; } = 10;
        [SettingIgnore] public DateTime StudioLastModifiedTime { get; set; } = new();

        #endregion
    }

    public enum SpeedUnit {
        PixelPerSecond,
        PixelPerFrame
    }
}