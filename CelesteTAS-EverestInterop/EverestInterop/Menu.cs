using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TAS.EverestInterop {
	class Menu {

		private static TextMenu.Item moreOptionsTextMenu;
		private static TextMenu.Item keyConfigMenu;

		private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private static readonly TextMenu.Item[] normalOptions;
		private static readonly TextMenu.Item[] hiddenOptions;

		static Menu() {

			normalOptions = new TextMenu.Item[] {
				new TextMenu.OnOff("Show Hitboxes", Settings.ShowHitboxes).Change(b => Settings.ShowHitboxes = b),
				new TextMenu.OnOff("Simplified Graphics", Settings.SimplifiedGraphics).Change(b => Settings.SimplifiedGraphics = b),
				new TextMenu.OnOff("Center Camera", Settings.CenterCamera).Change(b => Settings.CenterCamera = b),
				new TextMenu.OnOff("Launch Studio At Boot", Settings.LaunchStudioAtBoot).Change(b => Settings.LaunchStudioAtBoot = b),
				new TextMenu.OnOff("Disable Achievements", Settings.DisableAchievements).Change(b => Settings.DisableAchievements = b),
			};

			hiddenOptions = new TextMenu.Item[] {
				new TextMenu.OnOff("Unix RTC",Settings.UnixRTC).Change(b => Settings.UnixRTC = b),
				new TextMenu.OnOff("Disable Grab Desync Fix", Settings.DisableGrabDesyncFix).Change(b => Settings.DisableGrabDesyncFix = b),
				new TextMenu.OnOff("Round Position",Settings.RoundPosition).Change(b => Settings.RoundPosition = b),
				new TextMenu.OnOff("Mod 9D Lighting",Settings.Mod9DLighting).Change(b => Settings.Mod9DLighting = b),
				new TextMenu.OnOff("Override Version Check", Settings.OverrideVersionCheck).Change(b => Settings.OverrideVersionCheck = b),
				new TextMenu.OnOff("Hide Gameplay", Settings.HideGameplay).Change(b => {
					Settings.HideGameplay = b;
					Settings.ShowHitboxes = b;
				}),
			};
	}

		public static void CreateMenu(EverestModule self, TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
			menu.Add(new TextMenu.OnOff("Enabled", Settings.Enabled).Change((b) => {
				Settings.Enabled = b;
				foreach (TextMenu.Item item in normalOptions) {
					item.Visible = b;
				}
				keyConfigMenu.Visible = b;
				moreOptionsTextMenu.Visible = b;
				foreach (TextMenu.Item item in hiddenOptions) {
					item.Visible = false;
				}
				if (!b)
					Settings.ShowHitboxes = false;
			}));

			foreach (TextMenu.Item item in normalOptions) {
				menu.Add(item);
				item.Visible = Settings.Enabled;
			}

			keyConfigMenu = new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => {
				menu.Focused = false;
				Engine.Scene.Add(new ModuleSettingsKeyboardConfigUI(self) {
					OnClose = () => menu.Focused = true
				});
				Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
			});

			moreOptionsTextMenu = new TextMenu.Button("modoptions_celestetas_moreoptions".DialogCleanOrNull() ?? "More Options").Pressed(() => {
				ToggleMoreOptionsMenuItem(menu, true);
				moreOptionsTextMenu.Visible = false;
				menu.Selection += 1;
			});

			menu.Add(keyConfigMenu);
			menu.Add(moreOptionsTextMenu);
			keyConfigMenu.Visible = Settings.Enabled;
			moreOptionsTextMenu.Visible = Settings.Enabled;

			foreach (TextMenu.Item item in hiddenOptions) {
				menu.Add(item);
				item.Visible = false;
			}

			menu.Add(new TextMenu.Button("modoptions_celestetas_reload".DialogCleanOrNull() ?? "Reload Settings")
			.Pressed(() => {
				CelesteTASModule.Instance.LoadSettings();
				Hotkeys.instance.OnInputInitialize();
			}));

		}

		private static void ToggleMoreOptionsMenuItem(TextMenu textMenu, bool visible) {
			foreach (TextMenu.Item item in hiddenOptions)
				item.Visible = true;
		}

	}
}
