using System;
using Celeste;
using Celeste.Mod;
using Monocle;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
	class Menu {
		private static TextMenu.Item moreOptionsTextMenu;
		private static TextMenu.Item keyConfigMenu;

		private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

		private static List<TextMenu.Item> normalOptions;
		private static List<TextMenu.Item> hiddenOptions;


		private static void CreateNormalOptions(TextMenu menu, bool inGame) {
			normalOptions = new List<TextMenu.Item> {
				new TextMenu.OnOff("Show Hitboxes", Settings.ShowHitboxes).Change(b => Settings.ShowHitboxes = b),
				new TextMenu.OnOff("Simplified Graphics", Settings.SimplifiedGraphics).Change(b => Settings.SimplifiedGraphics = b),
				new TextMenu.OnOff("Center Camera", Settings.CenterCamera).Change(b => Settings.CenterCamera = b),
				new TextMenu.OnOff("Launch Studio At Boot", Settings.LaunchStudioAtBoot).Change(b => Settings.LaunchStudioAtBoot = b).Apply(item => item.SetAction( () => { item.NeedsRelaunch(menu); })),
				new TextMenu.OnOff("Disable Achievements", Settings.DisableAchievements).Change(b => Settings.DisableAchievements = b),
			};
			if (!inGame) {
				normalOptions.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
				normalOptions.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
			}
		}

		private static void CreateHiddenOptions(TextMenu menu, bool inGame) {
			var itemInfoHUD = new TextMenu.Option<InfoPositions>("Info HUD");
			hiddenOptions = new List<TextMenu.Item> {
				new TextMenu.OnOff("Unix RTC",Settings.UnixRTC).Change(b => Settings.UnixRTC = b).Apply(item => item.SetAction( () => { item.NeedsRelaunch(menu); })),
				new TextMenu.OnOff("Disable Grab Desync Fix", Settings.DisableGrabDesyncFix).Change(b => Settings.DisableGrabDesyncFix = b),
				new TextMenu.OnOff("Round Position",Settings.RoundPosition).Change(b => Settings.RoundPosition = b),
				new TextMenu.OnOff("Mod 9D Lighting",Settings.Mod9DLighting).Change(b => Settings.Mod9DLighting = b).Apply(item => item.SetAction( () => { item.NeedsRelaunch(menu); })),
				new TextMenu.OnOff("Auto Extract New Studio", Settings.AutoExtractNewStudio).Change(b => Settings.AutoExtractNewStudio = b).Apply(item => item.SetAction( () => { item.NeedsRelaunch(menu); })),
				new TextMenu.OnOff("Hide Gameplay", Settings.HideGameplay).Change(b => {
					Settings.HideGameplay = b;
					if (b) {
						((TextMenu.OnOff) normalOptions.First()).RightPressed();
					} else {
						((TextMenu.OnOff) normalOptions.First()).LeftPressed();
					}
				}),
				new TextMenu.OnOff("Auto Mute on Fast Forward", Settings.AutoMute).Change(b => Settings.AutoMute = b),
				new TextMenu.OnOff("Hide Trigger Hitboxes", Settings.HideTriggerHitboxes).Change(b => Settings.HideTriggerHitboxes = b),
				new TextMenu.OnOff("Simplified Hitboxes", Settings.SimplifiedHitboxes).Change(b => Settings.SimplifiedHitboxes = b),
				new TextMenu.Option<LastFrameHitboxesTypes>("Show Actual Entity Collide Hitbox").Apply(option => {
						Array enumValues = Enum.GetValues(typeof(LastFrameHitboxesTypes));
						foreach (LastFrameHitboxesTypes value in enumValues) {
							option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.ShowActualEntityCollideHitbox));
						}
						option.Change(b => Settings.ShowActualEntityCollideHitbox = b);
						option.SetAction(() => {
							option.AddDescription(menu, "when checking for collisions with player");
							option.AddDescription(menu, "Show the actual hitbox of the entity");
						});
					}),
				new TextMenu.OnOff("Show Actual Player Collide Hitbox", Settings.ShowActualPlayerCollideHitbox).Change(b => Settings.ShowActualPlayerCollideHitbox = b).Apply(option => {
					option.SetAction(() => {
						option.AddDescription(menu, "when checking for collisions with entities");
						option.AddDescription(menu, "Show the actual hitbox of the player");
					});
				}),
				new TextMenu.Option<InfoPositions>("Info HUD").Apply(option => {
					Array enumValues = Enum.GetValues(typeof(InfoPositions));
					foreach (InfoPositions value in enumValues) {
						option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.InfoHUD));
					}
					option.Change(b => Settings.InfoHUD = b);
				})
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

				if (!b && Settings.ShowHitboxes) {
					((TextMenu.OnOff) normalOptions.First()).LeftPressed();
				}
			}));

			CreateNormalOptions(menu, inGame);
			foreach (TextMenu.Item item in normalOptions) {
				menu.Add(item);
				item.Visible = Settings.Enabled;
				item.InvokeAction();
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

			CreateHiddenOptions(menu, inGame);
			foreach (TextMenu.Item item in hiddenOptions) {
				menu.Add(item);
				item.Visible = false;
				item.InvokeAction();
			}
		}

		private static void ToggleMoreOptionsMenuItem(TextMenu textMenu, bool visible) {
			foreach (TextMenu.Item item in hiddenOptions)
				item.Visible = visible;
		}
	}
}
