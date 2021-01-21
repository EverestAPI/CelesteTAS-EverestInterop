using System;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
    public class CelesteTASModuleSettings : EverestModuleSettings {
        public bool Enabled { get; set; } = true;

        [DefaultButtonBinding(0, Keys.RightControl)]
        public ButtonBinding KeyStart { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.RightShift)]
        public ButtonBinding KeyFastForward { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.OemOpenBrackets)]
        public ButtonBinding KeyFrameAdvance { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.OemCloseBrackets)]
        public ButtonBinding KeyPause { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.B)] public ButtonBinding KeyHitboxes { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.N)] public ButtonBinding KeyGraphics { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.M)] public ButtonBinding KeyCamera { get; set; } = new ButtonBinding();

        // Multiple keys are not supported, so we only set Keys.OemMinus
        [DefaultButtonBinding(0, Keys.OemMinus)]
        public ButtonBinding KeySaveState { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.OemPlus)]
        public ButtonBinding KeyLoadState { get; set; } = new ButtonBinding();

        [DefaultButtonBinding(0, Keys.None)]
        public ButtonBinding KeyClearState { get; set; } = new ButtonBinding();

        #region SimplifiedGraphics

        private bool _simplifiedGraphics = false;

        public bool SimplifiedGraphics {
            get => Enabled && _simplifiedGraphics;
            set => _simplifiedGraphics = value;
        }

        private bool _hideGamePlayer;
        public bool HideGameplay {
            get => _hideGamePlayer;
            set => _hideGamePlayer = ShowHitboxes = value;
        }

        public int? SimplifiedLighting { get; set; } = 10;
        public int? SimplifiedBloomBase { get; set; } = 0;
        public int? SimplifiedBloomStrength { get; set; } = 1;
        public SimplifiedGraphicsFeature.SpinnerColor SimplifiedSpinnerColor { get; set; } = SimplifiedGraphicsFeature.SpinnerColor.All[1];
        public Color? SimplifiedDustSpriteColor { get; set; } = Color.Transparent;
        public bool SimplifiedSpotlightWipe { get; set; } = true;
        public bool SimplifiedColorGrade { get; set; } = true;
        public bool SimplifiedBackdrop { get; set; } = true;
        public bool SimplifiedDecal { get; set; } = true;
        public bool SimplifiedParticle { get; set; } = true;
        public bool SimplifiedDistort { get; set; } = true;
        public bool SimplifiedDreamBlock { get; set; } = true;
        public bool SimplifiedLava { get; set; } = true;
        public bool SimplifiedLightning { get; set; } = true;

        #endregion

        public bool ShowHitboxes {
            get => GameplayRendererExt.RenderDebug;
            set => GameplayRendererExt.RenderDebug = value;
        }

        private bool _centerCamera = false;

        public bool CenterCamera {
            get => Enabled && _centerCamera;
            set => _centerCamera = value;
        }

        public bool DisableAchievements { get; set; } = false;

        [SettingNeedsRelaunch] public bool UnixRTC { get; set; } = false;

        [SettingNeedsRelaunch] public bool LaunchStudioAtBoot { get; set; } = false;

        [SettingIgnore] public string DefaultPath { get; set; } = null;

        public bool Mod9DLighting { get; set; } = false;

        public bool DisableGrabDesyncFix {
            get => Manager.grabButton != Buttons.Back;
            set => Manager.grabButton = value ? Buttons.LeftShoulder : Buttons.Back;
        }

        public bool RoundPosition { get; set; } = true;

        [SettingIgnore] public bool FastForwardCallBase { get; set; } = false;
        [SettingIgnore] public int FastForwardThreshold { get; set; } = 10;
        [SettingIgnore] public DateTime StudioLastModifiedTime { get; set; } = new DateTime();
        public bool AutoExtractNewStudio { get; set; } = true;
        public bool AutoMute { get; set; } = true;
        [SettingIgnore] public Color EntityHitboxColor { get; set; } = HitboxColor.DefaultEntityColor;
        [SettingIgnore] public Color TriggerHitboxColor { get; set; } = HitboxColor.DefaultTriggerColor;
        public bool HideTriggerHitboxes { get; set; } = false;
        public bool SimplifiedHitboxes { get; set; } = true;
        public ActualCollideHitboxTypes ShowActualCollideHitboxes { get; set; } = ActualCollideHitboxTypes.OFF;
        public InfoPositions InfoHUD { get; set; } = InfoPositions.OFF;
    }
}