using System;
using Celeste;
using Celeste.Mod;
using Monocle;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
    internal static class Menu {
        private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        private static List<TextMenu.Item> options;
        private static TextMenu.Item showHitboxesSubmenu;

        private static void CreateOptions(EverestModule everestModule, TextMenu menu, bool inGame) {
            options = new List<TextMenu.Item> {
                new TextMenuExt.SubMenu("Show Hitboxes", false).Apply(subMenu => {
                    showHitboxesSubmenu = subMenu;
                    subMenu.Add(new TextMenu.OnOff("Enabled", Settings.ShowHitboxes).Change(value => Settings.ShowHitboxes = value));
                    subMenu.Add(new TextMenu.Option<ActualCollideHitboxTypes>("Actual Collide Hitboxes").Apply(option => {
                        Array enumValues = Enum.GetValues(typeof(ActualCollideHitboxTypes));
                        foreach (ActualCollideHitboxTypes value in enumValues) {
                            option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.ShowActualCollideHitboxes));
                        }

                        option.Change(value => Settings.ShowActualCollideHitboxes = value);
                    }));
                    subMenu.Add(new TextMenuExt.EnumerableSlider<bool>("Trigger Hitboxes", Menu.CreateShowHideOptions(), Settings.HideTriggerHitboxes)
                        .Change(
                            value => Settings.HideTriggerHitboxes = value));
                    subMenu.Add(new TextMenu.OnOff("Simplified Hitboxes", Settings.SimplifiedHitboxes).Change(value =>
                        Settings.SimplifiedHitboxes = value));
                    subMenu.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
                    subMenu.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
                }),
                SimplifiedGraphicsFeature.CreateSimplifiedGraphicsOption(),
                new TextMenuExt.SubMenu("Relaunch Required", false).Apply(subMenu => {
                    subMenu.Add(new TextMenu.OnOff("Launch Studio At Boot", Settings.LaunchStudioAtBoot).Change(value =>
                        Settings.LaunchStudioAtBoot = value));
                    subMenu.Add(new TextMenu.OnOff("Auto Extract New Studio", Settings.AutoExtractNewStudio).Change(value =>
                        Settings.AutoExtractNewStudio = value));
                    subMenu.Add(new TextMenu.OnOff("Unix RTC", Settings.UnixRTC).Change(value => Settings.UnixRTC = value));
                }),
                new TextMenuExt.SubMenu("More Options", false).Apply(subMenu => {
                    subMenu.Add(new TextMenu.OnOff("Center Camera", Settings.CenterCamera).Change(value => Settings.CenterCamera = value));
                    subMenu.Add(new TextMenu.Option<InfoPositions>("Info HUD").Apply(option => {
                        Array enumValues = Enum.GetValues(typeof(InfoPositions));
                        foreach (InfoPositions value in enumValues) {
                            option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.InfoHUD));
                        }

                        option.Change(value => Settings.InfoHUD = value);
                    }));
                    subMenu.Add(
                        new TextMenu.OnOff("Disable Achievements", Settings.DisableAchievements).Change(value =>
                            Settings.DisableAchievements = value));
                    subMenu.Add(new TextMenu.OnOff("Disable Grab Desync Fix", Settings.DisableGrabDesyncFix).Change(value =>
                        Settings.DisableGrabDesyncFix = value));
                    subMenu.Add(new TextMenu.OnOff("Round Position", Settings.RoundPosition).Change(value => Settings.RoundPosition = value));
                    subMenu.Add(new TextMenu.OnOff("Auto Mute on Fast Forward", Settings.AutoMute).Change(value => Settings.AutoMute = value));
                    subMenu.Add(new TextMenu.OnOff("Mod 9D Lighting", Settings.Mod9DLighting).Change(value => Settings.Mod9DLighting = value));
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
            menu.Add(new TextMenu.OnOff("Enabled", Settings.Enabled).Change((value) => {
                Settings.Enabled = value;
                foreach (TextMenu.Item item in options) {
                    item.Visible = value;
                }
            }));
            CreateOptions(everestModule, menu, inGame);
            foreach (TextMenu.Item item in options) {
                menu.Add(item);
                item.Visible = Settings.Enabled;
            }
            if (inGame) {
                showHitboxesSubmenu.AddDescription(menu, "Hitbox color can only be edited in the menu of the title screen");
            }
        }

        public static IEnumerable<KeyValuePair<int?, string>> CreateSliderOptions(int start, int end, Func<int, string> formatter = null) {
            if (formatter == null) {
                formatter = i => i.ToString();
            }

            List<KeyValuePair<int?, string>> result = new List<KeyValuePair<int?, string>>();

            if (start <= end) {
                for (int current = start; current <= end; current++) {
                    result.Add(new KeyValuePair<int?, string>(current, formatter(current)));
                }

                result.Insert(0, new KeyValuePair<int?, string>(null, "Default"));
            } else {
                for (int current = start; current >= end; current--) {
                    result.Add(new KeyValuePair<int?, string>(current, formatter(current)));
                }

                result.Insert(0, new KeyValuePair<int?, string>(null, "Default"));
            }

            return result;
        }

        public static IEnumerable<KeyValuePair<bool, string>> CreateShowHideOptions() {
            return new List<KeyValuePair<bool, string>> {
                new KeyValuePair<bool, string>(false, "Default"),
                new KeyValuePair<bool, string>(true, "Hide"),
            };
        }

        public static IEnumerable<KeyValuePair<bool, string>> CreateSimplifyOptions() {
            return new List<KeyValuePair<bool, string>> {
                new KeyValuePair<bool, string>(false, "Default"),
                new KeyValuePair<bool, string>(true, "Simplify"),
            };
        }

        public static IEnumerable<KeyValuePair<Color?, string>> CreateNaturalColorOptions() {
            return new List<KeyValuePair<Color?, string>> {
                new KeyValuePair<Color?, string>(null, "Default"),
                new KeyValuePair<Color?, string>(new Color(196, 2, 51), "Red"),
                new KeyValuePair<Color?, string>(new Color(0, 159, 107), "Green"),
                new KeyValuePair<Color?, string>(new Color(0, 135, 189), "Blue"),
                new KeyValuePair<Color?, string>(new Color(255, 211, 0), "Yellow"),
                new KeyValuePair<Color?, string>(Color.White, "White"),
                new KeyValuePair<Color?, string>(Color.Black, "Black"),
                new KeyValuePair<Color?, string>(Color.Transparent, "Transparent"),
            };
        }
    }
}