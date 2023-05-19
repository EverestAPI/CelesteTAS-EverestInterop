using System;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxColor {
    public static readonly Color DefaultEntityColor = Color.Red;
    public static readonly Color DefaultTriggerColor = Color.MediumPurple;
    public static readonly Color DefaultPlatformColor = Color.Coral;
    public static readonly Color RespawnTriggerColor = Color.YellowGreen;
    public static readonly Color PufferHeightCheckColor = Color.WhiteSmoke;
    public static readonly Color PufferPushRadiusColor = Color.DarkRed;

    private static readonly Regex HexChar = new(@"^[0-9a-f]*$", RegexOptions.IgnoreCase);

    public static Color EntityColor => TasSettings.EntityHitboxColor;
    public static Color TriggerColor => TasSettings.TriggerHitboxColor;
    public static Color PlatformColor => TasSettings.PlatformHitboxColor;
    public static Color EntityColorInversely => EntityColor.Invert();
    public static Color EntityColorInverselyLessAlpha => EntityColorInversely * 0.6f;
    public static float UnCollidableAlpha => TasSettings.UnCollidableHitboxesOpacity / 10f;

    public static TextMenu.Item CreateEntityHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Entity Hitbox Color".ToDialogText() + $": {ColorToHex(TasSettings.EntityHitboxColor)}").Pressed(
            () => {
                Audio.Play("event:/ui/main/savefile_rename_start");
                textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(ColorToHex(TasSettings.EntityHitboxColor),
                        value => TasSettings.EntityHitboxColor = HexToColor(value, TasSettings.EntityHitboxColor), 9);
            });
        item.Disabled = inGame;
        return item;
    }

    public static TextMenu.Item CreateTriggerHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Trigger Hitbox Color".ToDialogText() + $": {ColorToHex(TasSettings.TriggerHitboxColor)}").Pressed(
            () => {
                Audio.Play("event:/ui/main/savefile_rename_start");
                textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                    .Init<OuiModOptions>(ColorToHex(TasSettings.TriggerHitboxColor),
                        value => TasSettings.TriggerHitboxColor = HexToColor(value, TasSettings.TriggerHitboxColor), 9);
            });
        item.Disabled = inGame;
        return item;
    }

    public static TextMenu.Item CreatePlatformHitboxColorButton(TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button("Platform Hitbox Color".ToDialogText() + $": {ColorToHex(TasSettings.PlatformHitboxColor)}")
            .Pressed(
                () => {
                    Audio.Play("event:/ui/main/savefile_rename_start");
                    textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>()
                        .Init<OuiModOptions>(ColorToHex(TasSettings.PlatformHitboxColor),
                            value => TasSettings.PlatformHitboxColor = HexToColor(value, TasSettings.PlatformHitboxColor), 9);
                });
        item.Disabled = inGame;
        return item;
    }

    public static string ColorToHex(Color color) {
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
        if (!HexChar.IsMatch(hex)) {
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

    [Load]
    private static void Load() {
        IL.Monocle.Entity.DebugRender += EntityOnDebugRender;
    }

    [Unload]
    private static void Unload() {
        IL.Monocle.Entity.DebugRender -= EntityOnDebugRender;
    }

    private static void EntityOnDebugRender(ILContext il) {
        ILCursor ilCursor = new(il);
        if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCall<Color>("get_DarkRed"))) {
            ilCursor.Emit(OpCodes.Ldarg_0);
            ilCursor.EmitDelegate<Func<Color, Entity, Color>>(GetCustomColor);
        }

        if (ilCursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCall<Color>("get_Red"))) {
            ilCursor.Emit(OpCodes.Ldarg_0);
            ilCursor.EmitDelegate<Func<Color, Entity, Color>>(GetCustomColor);
        }
    }

    private static Color GetCustomColor(Color color, Entity entity) {
        if (!TasSettings.ShowHitboxes || entity is Player) {
            return color;
        }

        Color customColor = entity switch {
            Platform => TasSettings.PlatformHitboxColor,
            ChangeRespawnTrigger => RespawnTriggerColor,
            Trigger => TasSettings.TriggerHitboxColor,
            _ => TasSettings.EntityHitboxColor
        };

        if (!entity.Collidable) {
            customColor *= UnCollidableAlpha;
        }

        return customColor;
    }

    public static Color GetCustomColor(Entity entity) {
        return GetCustomColor(Color.Red, entity);
    }

    [Command("entity_hitbox_color", "change the entity hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeEntityHitboxColor(string color) {
        TasSettings.EntityHitboxColor = HexToColor(color, DefaultEntityColor);
        CelesteTasModule.Instance.SaveSettings();
    }

    [Command("trigger_hitbox_color", "change the trigger hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeTriggerHitboxColor(string color) {
        TasSettings.TriggerHitboxColor = HexToColor(color, DefaultTriggerColor);
        CelesteTasModule.Instance.SaveSettings();
    }

    [Command("platform_hitbox_color", "change the platform hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangePlatformHitboxColor(string color) {
        TasSettings.PlatformHitboxColor = HexToColor(color, DefaultPlatformColor);
        CelesteTasModule.Instance.SaveSettings();
    }
}