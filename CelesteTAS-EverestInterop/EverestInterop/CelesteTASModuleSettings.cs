using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TAS.EverestInterop {
	public class CelesteTASModuleSettings : EverestModuleSettings {

		public bool Enabled { get; set; } = true;

		public ButtonBinding KeyStart { get; set; } = new ButtonBinding();
		public ButtonBinding KeyFastForward { get; set; } = new ButtonBinding();
		public ButtonBinding KeyFrameAdvance { get; set; } = new ButtonBinding();
		public ButtonBinding KeyPause { get; set; } = new ButtonBinding();

		public bool SimplifiedGraphics { get; set; } = false;
		public ButtonBinding KeyGraphics { get; set; } = new ButtonBinding();

		public bool ShowHitboxes {
			get => GameplayRendererExt.RenderDebug;
			set => GameplayRendererExt.RenderDebug = value;
		}
		public ButtonBinding KeyHitboxes { get; set; } = new ButtonBinding();

		public bool CenterCamera { get; set; } = false;
		public ButtonBinding KeyCamera { get; set; } = new ButtonBinding();

		public ButtonBinding KeySaveState { get; set; } = new ButtonBinding();
		public ButtonBinding KeyLoadState { get; set; } = new ButtonBinding();

		public bool DisableAchievements { get; set; } = false;

		[SettingNeedsRelaunch]
		public bool UnixRTC { get; set; } = false;

		[SettingNeedsRelaunch]
		public bool LaunchStudioAtBoot { get; set; } = false;

		[SettingIgnore]
		public string DefaultPath { get; set; } = null;

		[SettingIgnore]
		public bool Mod9DLighting { get; set; } = false;

		[SettingIgnore]
		public bool FastForwardCallBase { get; set; } = false;
		[SettingIgnore]
		public int FastForwardThreshold { get; set; } = 10;
		[SettingIgnore]
		public string Version { get; set; } = null;
		[SettingIgnore]
		public bool OverrideVersionCheck { get; set; } = false;

    }
}
