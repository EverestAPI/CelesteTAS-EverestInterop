using System;
using Celeste;
using Celeste.Mod;
using Monocle;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
	class Menu {
		private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private static List<TextMenu.Item> normalOptions;

		private static void CreateNormalOptions(EverestModule everestModule, TextMenu menu, bool inGame) {
			normalOptions = new List<TextMenu.Item> {
				new TextMenuExt.SubMenu("Show Hitboxes", false).Apply(subMenu => {
					subMenu.Add(new TextMenu.OnOff("Enabled", Settings.ShowHitboxes).Change(b => Settings.ShowHitboxes = b));
					subMenu.Add(new TextMenu.Option<ActualCollideHitboxTypes>("Show Actual Collide Hitboxes").Apply(option => {
						Array enumValues = Enum.GetValues(typeof(ActualCollideHitboxTypes));
						foreach (ActualCollideHitboxTypes value in enumValues) {
							option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.ShowActualCollideHitboxes));
						}
						option.Change(b => Settings.ShowActualCollideHitboxes = b);
					}));
					subMenu.Add(new TextMenu.OnOff("Hide Trigger Hitboxes", Settings.HideTriggerHitboxes).Change(b => Settings.HideTriggerHitboxes = b));
					subMenu.Add(new TextMenu.OnOff("Simplified Hitboxes", Settings.SimplifiedHitboxes).Change(b => Settings.SimplifiedHitboxes = b));
					subMenu.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
					subMenu.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
				}),
				SimplifiedGraphics.CreateSimplifiedGraphicsOption(),
				new TextMenuExt.SubMenu("Relaunch Required", false).Apply(subMenu => {
					subMenu.Add(new TextMenu.OnOff("Launch Studio At Boot", Settings.LaunchStudioAtBoot).Change(b => Settings.LaunchStudioAtBoot = b));
					subMenu.Add(new TextMenu.OnOff("Auto Extract New Studio", Settings.AutoExtractNewStudio).Change(b => Settings.AutoExtractNewStudio = b));
					subMenu.Add(new TextMenu.OnOff("Unix RTC", Settings.UnixRTC).Change(b => Settings.UnixRTC = b));
				}),
				new TextMenuExt.SubMenu("More Options", false).Apply(subMenu => {
					subMenu.Add(new TextMenu.OnOff("Center Camera", Settings.CenterCamera).Change(b => Settings.CenterCamera = b));
					subMenu.Add(new TextMenu.Option<InfoPositions>("Info HUD").Apply(option => {
						Array enumValues = Enum.GetValues(typeof(InfoPositions));
						foreach (InfoPositions value in enumValues) {
							option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.InfoHUD));
						}
						option.Change(b => Settings.InfoHUD = b);
					}));
					subMenu.Add( new TextMenu.OnOff("Disable Achievements", Settings.DisableAchievements).Change(b => Settings.DisableAchievements = b));
					subMenu.Add(new TextMenu.OnOff("Disable Grab Desync Fix", Settings.DisableGrabDesyncFix).Change(b => Settings.DisableGrabDesyncFix = b));
					subMenu.Add(new TextMenu.OnOff("Round Position",Settings.RoundPosition).Change(b => Settings.RoundPosition = b));
					subMenu.Add(new TextMenu.OnOff("Auto Mute on Fast Forward", Settings.AutoMute).Change(b => Settings.AutoMute = b));
					subMenu.Add(new TextMenu.OnOff("Mod 9D Lighting",Settings.Mod9DLighting).Change(b => Settings.Mod9DLighting = b));
				}),
				new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => {
					menu.Focused = false;
					Engine.Scene.Add(new ModuleSettingsKeyboardConfigUI(everestModule) {
						OnClose = () => menu.Focused = true
					});
					Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
				})
			};
		}

		public static void CreateMenu(EverestModule everestModule, TextMenu menu, bool inGame) {
			menu.Add(new TextMenu.OnOff("Enabled", Settings.Enabled).Change((b) => {
				Settings.Enabled = b;
				foreach (TextMenu.Item item in normalOptions) {
					item.Visible = b;
				}
			}));

			CreateNormalOptions(everestModule, menu, inGame);
			foreach (TextMenu.Item item in normalOptions) {
				menu.Add(item);
				item.Visible = Settings.Enabled;
			}
		}
	}
}
