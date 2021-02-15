using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.EverestInterop.Hitboxes;

namespace TAS.EverestInterop {
internal static class Menu {
    private static readonly MethodInfo createKeyboardConfigUI = typeof(EverestModule).GetMethodInfo("CreateKeyboardConfigUI");
    private static List<TextMenu.Item> options;
    private static TextMenu.Item showHitboxesSubmenu;
    private static TextMenu.Item keyConfigButton;
    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    internal static string ToDialogText(this string input) => Dialog.Clean("TAS_" + input.Replace(" ", "_"));

    private static void CreateOptions(EverestModule everestModule, TextMenu menu, bool inGame) {
        options = new List<TextMenu.Item> {
            new TextMenuExt.SubMenu("Show Hitboxes".ToDialogText(), false).Apply(subMenu => {
                showHitboxesSubmenu = subMenu;
                subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), Settings.ShowHitboxes).Change(value => Settings.ShowHitboxes = value));
                subMenu.Add(new TextMenu.Option<ActualCollideHitboxTypes>("Actual Collide Hitboxes".ToDialogText()).Apply(option => {
                    Array enumValues = Enum.GetValues(typeof(ActualCollideHitboxTypes));
                    foreach (ActualCollideHitboxTypes value in enumValues) {
                        option.Add(value.ToString().SpacedPascalCase().ToDialogText(), value, value.Equals(Settings.ShowActualCollideHitboxes));
                    }

                    option.Change(value => Settings.ShowActualCollideHitboxes = value);
                }));
                subMenu.Add(new TextMenu.OnOff("Hide Trigger Hitboxes".ToDialogText(), Settings.HideTriggerHitboxes).Change(value =>
                    Settings.HideTriggerHitboxes = value));
                subMenu.Add(new TextMenu.OnOff("Simplified Hitboxes".ToDialogText(), Settings.SimplifiedHitboxes).Change(value =>
                    Settings.SimplifiedHitboxes = value));
                subMenu.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
                subMenu.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
                subMenu.Add(HitboxColor.CreateSolidTilesHitboxColorButton(menu, inGame));
            }),

            SimplifiedGraphicsFeature.CreateSimplifiedGraphicsOption(),

            new TextMenuExt.SubMenu("Relaunch Required".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Launch Studio At Boot".ToDialogText(), Settings.LaunchStudioAtBoot).Change(value =>
                    Settings.LaunchStudioAtBoot = value));
                subMenu.Add(new TextMenu.OnOff("Auto Extract New Studio".ToDialogText(), Settings.AutoExtractNewStudio).Change(value =>
                    Settings.AutoExtractNewStudio = value));
                subMenu.Add(new TextMenu.OnOff("Unix RTC".ToDialogText(), Settings.UnixRTC).Change(value => Settings.UnixRTC = value));
            }),

            new TextMenuExt.SubMenu("More Options".ToDialogText(), false).Apply(subMenu => {
                subMenu.Add(new TextMenu.OnOff("Center Camera".ToDialogText(), Settings.CenterCamera).Change(value =>
                    Settings.CenterCamera = value));
                subMenu.Add(new TextMenu.OnOff("Round Position".ToDialogText(), Settings.RoundPosition).Change(value =>
                    Settings.RoundPosition = value));
                subMenu.Add(new TextMenu.Option<InfoPositions>("Info HUD".ToDialogText()).Apply(option => {
                    Array enumValues = Enum.GetValues(typeof(InfoPositions));
                    foreach (InfoPositions value in enumValues) {
                        option.Add(value.ToString().SpacedPascalCase(), value, value.Equals(Settings.InfoHUD));
                    }

                    option.Change(value => Settings.InfoHUD = value);
                }));
                subMenu.Add(new TextMenu.OnOff("Pause After Load State".ToDialogText(), Settings.PauseAfterLoadState).Change(value =>
                    Settings.PauseAfterLoadState = value));
                subMenu.Add(
                    new TextMenu.OnOff("Disable Achievements".ToDialogText(), Settings.DisableAchievements).Change(value =>
                        Settings.DisableAchievements = value));
                subMenu.Add(new TextMenu.OnOff("Disable Grab Desync Fix".ToDialogText(), Settings.DisableGrabDesyncFix).Change(value =>
                    Settings.DisableGrabDesyncFix = value));
                subMenu.Add(new TextMenu.OnOff("Auto Mute on Fast Forward".ToDialogText(), Settings.AutoMute).Change(value =>
                    Settings.AutoMute = value));
                subMenu.Add(new TextMenu.OnOff("Mod 9D Lighting".ToDialogText(), Settings.Mod9DLighting).Change(value =>
                    Settings.Mod9DLighting = value));
            }),
            new TextMenu.Button(Dialog.Clean("options_keyconfig")).Pressed(() => {
                menu.Focused = false;
                Entity keyboardConfig;
                if (createKeyboardConfigUI != null) {
                    keyboardConfig = createKeyboardConfigUI.Invoke(everestModule, new object[] {menu}) as Entity;
                } else {
                    keyboardConfig = new ModuleSettingsKeyboardConfigUI(everestModule) {OnClose = () => menu.Focused = true};
                }

                Engine.Scene.Add(keyboardConfig);
                Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
            }).Apply(item => keyConfigButton = item)
        };
    }

    public static void CreateMenu(EverestModule everestModule, TextMenu menu, bool inGame) {
        menu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), Settings.Enabled).Change((value) => {
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
            showHitboxesSubmenu.AddDescription(menu, "Hitbox Color Description 2".ToDialogText());
            showHitboxesSubmenu.AddDescription(menu, "Hitbox Color Description 1".ToDialogText());
        }

        keyConfigButton.AddDescription(menu, "Key Config Description".ToDialogText());
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

            result.Insert(0, new KeyValuePair<int?, string>(null, "Default".ToDialogText()));
        } else {
            for (int current = start; current >= end; current--) {
                result.Add(new KeyValuePair<int?, string>(current, formatter(current)));
            }

            result.Insert(0, new KeyValuePair<int?, string>(null, "Default".ToDialogText()));
        }

        return result;
    }

    public static IEnumerable<KeyValuePair<bool, string>> CreateDefaultHideOptions() {
        return new List<KeyValuePair<bool, string>> {
            new KeyValuePair<bool, string>(false, "Default".ToDialogText()),
            new KeyValuePair<bool, string>(true, "Hide".ToDialogText()),
        };
    }

    public static IEnumerable<KeyValuePair<bool, string>> CreateSimplifyOptions() {
        return new List<KeyValuePair<bool, string>> {
            new KeyValuePair<bool, string>(false, "Default".ToDialogText()),
            new KeyValuePair<bool, string>(true, "Simplify".ToDialogText()),
        };
    }

    public static IEnumerable<KeyValuePair<Color?, string>> CreateNaturalColorOptions() {
        return new List<KeyValuePair<Color?, string>> {
            new KeyValuePair<Color?, string>(null, "Default".ToDialogText()),
            new KeyValuePair<Color?, string>(new Color(196, 2, 51), "Red".ToDialogText()),
            new KeyValuePair<Color?, string>(new Color(0, 159, 107), "Green".ToDialogText()),
            new KeyValuePair<Color?, string>(new Color(0, 135, 189), "Blue".ToDialogText()),
            new KeyValuePair<Color?, string>(new Color(255, 211, 0), "Yellow".ToDialogText()),
            new KeyValuePair<Color?, string>(Color.White, "White".ToDialogText()),
            new KeyValuePair<Color?, string>(Color.Black, "Black".ToDialogText()),
            new KeyValuePair<Color?, string>(Color.Transparent, "Transparent".ToDialogText()),
        };
    }
}
}