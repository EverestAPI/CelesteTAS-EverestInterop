using System;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace TAS.EverestInterop.Hitboxes {
public class HitboxColor {
    public static readonly Color DefaultEntityColor = Color.Red;
    public static readonly Color DefaultTriggerColor = Color.Peru;
    public static readonly Color DefaultSolidTilesColor = Color.Red;

    private static readonly Regex hexChar = new Regex(@"^[0-9a-f]*$", RegexOptions.IgnoreCase);

    public static Color EntityColor => Settings.EntityHitboxColor;
    public static Color TriggerColor => Settings.TriggerHitboxColor;
    public static Color SolidTilesColor => Settings.SolidTilesHitboxColor;
    public static Color EntityColorInversely => EntityColor.Invert();
    public static Color EntityColorInverselyLessAlpha => EntityColorInversely * 0.6f;

    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public static TextMenu.Item CreateEntityHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Entity Hitbox Color".ToDialogText() + $": {ColorToHex(Settings.EntityHitboxColor)}").Pressed(
            () => {
                Audio.Play("event:/ui/main/savefile_rename_start");
                textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(ColorToHex(Settings.EntityHitboxColor),
                        value => Settings.EntityHitboxColor = HexToColor(value, Settings.EntityHitboxColor), 9);
            });
        item.Disabled = inGame;
        return item;
    }

    public static TextMenu.Item CreateTriggerHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Trigger Hitbox Color".ToDialogText() + $": {ColorToHex(Settings.TriggerHitboxColor)}").Pressed(
            () => {
                Audio.Play("event:/ui/main/savefile_rename_start");
                textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(ColorToHex(Settings.TriggerHitboxColor),
                        value => Settings.TriggerHitboxColor = HexToColor(value, Settings.TriggerHitboxColor), 9);
            });
        item.Disabled = inGame;
        return item;
    }

    public static TextMenu.Item CreateSolidTilesHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Solid Tiles Hitbox Color".ToDialogText() + $": {ColorToHex(Settings.SolidTilesHitboxColor)}").Pressed(
            () => {
                Audio.Play("event:/ui/main/savefile_rename_start");
                textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(ColorToHex(Settings.SolidTilesHitboxColor),
                        value => Settings.SolidTilesHitboxColor = HexToColor(value, Settings.SolidTilesHitboxColor), 9);
            });
        item.Disabled = inGame;
        return item;
    }

    private static string ColorToHex(Color color) {
        return
            $"#{color.A.ToString("X").PadLeft(2, '0')}" +
            $"{color.R.ToString("X").PadLeft(2, '0')}" +
            $"{color.G.ToString("X").PadLeft(2, '0')}" +
            $"{color.B.ToString("X").PadLeft(2, '0')}";
    }

    public static Color HexToColor(string hex, Color defaultColor) {
        if (string.IsNullOrWhiteSpace(hex)) {
            return defaultColor;
        }

        hex = hex.Replace("#", "");
        if (!hexChar.IsMatch(hex)) {
            return defaultColor;
        }

        // 123456789 => 12345678
        if (hex.Length > 8) {
            hex = hex.Substring(0, 8);
        }

        // 123 => 112233
        // 1234 => 11223344
        if (hex.Length == 3 || hex.Length == 4) {
            hex = hex.ToCharArray().Select(c => $"{c}{c}").Aggregate((s, s1) => s + s1);
        }

        // 123456 => FF123456
        hex = hex.PadLeft(8, 'F');

        try {
            long number = Convert.ToInt64(hex, 16);
            Color color = default;
            color.A = (byte) (number >> 24);
            color.R = (byte) (number >> 16);
            color.G = (byte) (number >> 8);
            color.B = (byte) number;
            return color;
        } catch (FormatException) {
            return defaultColor;
        }
    }

    public static void Load() {
        IL.Monocle.Entity.DebugRender += EntityOnDebugRender;
    }

    public static void Unload() {
        IL.Monocle.Entity.DebugRender -= EntityOnDebugRender;
    }

    private static void EntityOnDebugRender(ILContext il) {
        ILCursor ilCursor = new ILCursor(il);
        if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCall<Color>("get_DarkRed"))) {
            ilCursor.Emit(OpCodes.Ldarg_0);
            ilCursor.EmitDelegate<Func<Color, Entity, Color>>(GetCustomColor);
        }

        if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCall<Color>("get_Red"))) {
            ilCursor.Emit(OpCodes.Ldarg_0);
            ilCursor.EmitDelegate<Func<Color, Entity, Color>>(GetCustomColor);
        }
    }

    public static Color GetCustomColor(Color color, Entity entity) {
        if (!Settings.ShowHitboxes || entity is Player) {
            return color;
        }

        Color customColor = Settings.EntityHitboxColor;
        if (entity is Trigger) {
            customColor = Settings.TriggerHitboxColor;
        } else if (entity is SolidTiles) {
            customColor = Settings.SolidTilesHitboxColor;
        }

        if (!entity.Collidable) {
            customColor *= 0.5f;
        }

        return customColor;
    }

    [Command("entity_hitbox_color", "change the entity hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000")]
    private static void CmdChangeEntityHitboxColor(string color) {
        CelesteTASModule.Settings.EntityHitboxColor = HitboxColor.HexToColor(color, CelesteTASModule.Settings.EntityHitboxColor);
    }

    [Command("trigger_hitbox_color", "change the trigger hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000")]
    private static void CmdChangeTriggerHitboxColor(string color) {
        CelesteTASModule.Settings.TriggerHitboxColor = HitboxColor.HexToColor(color, CelesteTASModule.Settings.TriggerHitboxColor);
    }

    [Command("solidtiles_hitbox_color", "change the solid tiles hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000")]
    private static void CmdChangeSolidTilesHitboxColor(string color) {
        CelesteTASModule.Settings.SolidTilesHitboxColor = HitboxColor.HexToColor(color, CelesteTASModule.Settings.SolidTilesHitboxColor);
    }
}
}