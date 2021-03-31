using System;
using Celeste;
using Celeste.Mod;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxTweak {
        private static TextMenu.Item subMenuItem;
        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void Load() {
            HitboxTriggerSpikes.Load();
            ActualEntityCollideHitbox.Load();
            HitboxFixer.Load();
            HitboxSimplified.Load();
            HitboxHideTrigger.Load();
            HitboxColor.Load();
            HitboxFinalBoss.Load();
            HitboxOptimized.Load();
        }

        public static void Unload() {
            HitboxTriggerSpikes.Unload();
            ActualEntityCollideHitbox.Unload();
            HitboxFixer.Unload();
            HitboxSimplified.Unload();
            HitboxHideTrigger.Unload();
            HitboxColor.Unload();
            HitboxFinalBoss.Unload();
            HitboxOptimized.Unload();
        }

        public static TextMenu.Item CreateSubMenu(TextMenu menu, bool inGame) {
            subMenuItem = new TextMenuExt.SubMenu("Show Hitboxes".ToDialogText(), false).Apply(subMenu => {
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
}