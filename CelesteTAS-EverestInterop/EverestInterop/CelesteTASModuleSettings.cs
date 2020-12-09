using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;

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

		private bool _simplifiedGraphics = false;
		public bool SimplifiedGraphics {
			get => Enabled && _simplifiedGraphics;
			set => _simplifiedGraphics = value;
		}
		[DefaultButtonBinding(0, Keys.N)]
		public ButtonBinding KeyGraphics { get; set; } = new ButtonBinding();

		public bool ShowHitboxes {
			get => GameplayRendererExt.RenderDebug;
			set => GameplayRendererExt.RenderDebug = value;
		}
		[DefaultButtonBinding(0, Keys.B)]
		public ButtonBinding KeyHitboxes { get; set; } = new ButtonBinding();

		private bool _centerCamera = false;
		public bool CenterCamera {
			get => Enabled && _centerCamera;
			set => _centerCamera = value;
		}
		[DefaultButtonBinding(0, Keys.M)]
		public ButtonBinding KeyCamera { get; set; } = new ButtonBinding();

		// Multiple keys are not supported, so we only set Keys.OemMinus
		[DefaultButtonBinding(0, Keys.OemMinus)]
		public ButtonBinding KeySaveState { get; set; } = new ButtonBinding();
		[DefaultButtonBinding(0, Keys.OemPlus)]
		public ButtonBinding KeyLoadState { get; set; } = new ButtonBinding();

		public bool DisableAchievements { get; set; } = false;

		[SettingNeedsRelaunch]
		public bool UnixRTC { get; set; } = false;

		[SettingNeedsRelaunch]
		public bool LaunchStudioAtBoot { get; set; } = false;

		[SettingIgnore]
		public string DefaultPath { get; set; } = null;

		[SettingIgnore]
		[SettingNeedsRelaunch]
		public bool Mod9DLighting { get; set; } = false;

		[SettingIgnore]
		public bool DisableGrabDesyncFix {
			get => Manager.grabButton != Buttons.Back;
			set => Manager.grabButton = value ? Buttons.LeftShoulder : Buttons.Back;
		}

		[SettingIgnore]
		public bool RoundPosition { get; set; } = true;

		[SettingIgnore]
		public bool FastForwardCallBase { get; set; } = false;
		[SettingIgnore]
		public int FastForwardThreshold { get; set; } = 10;
		[SettingIgnore]
		public string Version { get; set; } = null;
		[SettingIgnore]
		public bool OverrideVersionCheck { get; set; } = false;
		[SettingIgnore]
		public bool HideGameplay { get; set; } = false;
		[SettingIgnore]
		public bool AutoMute { get; set; } = true;
		[SettingIgnore]
		public Color EntityHitboxColor { get; set; } = Color.Red;
		[SettingIgnore]
		public Color TriggerHitboxColor { get; set; } = Color.Red;
    }
}
