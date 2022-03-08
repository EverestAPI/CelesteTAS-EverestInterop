using System;
using Celeste;
using Celeste.Mod;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxMenu {
    private static EaseInSubMenu subMenuItem;
    private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

    public static EaseInSubMenu CreateSubMenu(TextMenu menu, bool inGame) {
        subMenuItem = new EaseInSubMenu("Show Hitboxes".ToDialogText(), false).Apply(subMenu => {
            subMenu.Add(new TextMenu.OnOff("Enabled".ToDialogText(), Settings.ShowHitboxes).Change(value => Settings.ShowHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Trigger Hitboxes".ToDialogText(), Settings.ShowTriggerHitboxes).Change(value =>
                Settings.ShowTriggerHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Unloaded Rooms Hitboxes".ToDialogText(), Settings.ShowUnloadedRoomsHitboxes).Change(value =>
                Settings.ShowUnloadedRoomsHitboxes = value));
            subMenu.Add(new TextMenu.OnOff("Show Camera Hitboxes".ToDialogText(), Settings.ShowCameraHitboxes).Change(value =>
                Settings.ShowCameraHitboxes = value));
            subMenu.Add(new TextMenu.Option<ActualCollideHitboxType>("Actual Collide Hitboxes".ToDialogText()).Apply(option => {
                Array enumValues = Enum.GetValues(typeof(ActualCollideHitboxType));
                foreach (ActualCollideHitboxType value in enumValues) {
                    option.Add(value.ToString().SpacedPascalCase().ToDialogText(), value, value.Equals(Settings.ShowActualCollideHitboxes));
                }

                option.Change(value => Settings.ShowActualCollideHitboxes = value);
            }));
            subMenu.Add(new TextMenu.OnOff("Simplified Hitboxes".ToDialogText(), Settings.SimplifiedHitboxes).Change(value =>
                Settings.SimplifiedHitboxes = value));
            subMenu.Add(HitboxColor.CreateEntityHitboxColorButton(menu, inGame));
            subMenu.Add(HitboxColor.CreateTriggerHitboxColorButton(menu, inGame));
            subMenu.Add(HitboxColor.CreatePlatformHitboxColorButton(menu, inGame));
        });
        return subMenuItem;
    }

    public static void AddSubMenuDescription(TextMenu menu, bool inGame) {
        if (inGame) {
            subMenuItem.AddDescription(menu, "Hitbox Color Description 2".ToDialogText());
            subMenuItem.AddDescription(menu, "Hitbox Color Description 1".ToDialogText());
        }
    }
}