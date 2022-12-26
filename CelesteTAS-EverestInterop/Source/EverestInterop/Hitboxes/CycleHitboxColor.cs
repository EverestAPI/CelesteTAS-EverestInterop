using Celeste;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;

namespace TAS.EverestInterop.Hitboxes;

public static class CycleHitboxColor {
    public static readonly Color DefaultColor1 = Color.Red;
    public static readonly Color DefaultColor2 = Color.Yellow;
    public static readonly Color DefaultColor3 = new(0.2f, 0.4f, 1f);

    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += EntityOnDebugRender;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= EntityOnDebugRender;
    }

    // TODO support helper's custom entities
    private static void EntityOnDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (TasSettings.ShowHitboxes && TasSettings.ShowCycleHitboxColors) {
            float? offset = self switch {
                CrystalStaticSpinner spinner => spinner.offset,
                DustStaticSpinner dust => dust.offset,
                Lightning lightning => lightning.toggleOffset,
                _ => null
            };

            if (offset.HasValue) {
                int activeFrame = (int) (self.Scene.TimeActive / Engine.DeltaTime);
                for (int i = 0; i < 3; i++) {
                    if (self.Scene.OnInterval(0.05f, offset.Value - Engine.DeltaTime * i)) {
                        self.Collider.Render(camera, GetColor((i + activeFrame) % 3) * (self.Collidable ? 1f : HitboxColor.UnCollidableAlpha * 0.8f));
                        return;
                    }
                }
            }
        }

        orig(self, camera);
    }

    private static Color GetColor(int index) {
        return index switch {
            0 => TasSettings.CycleHitboxColor1,
            1 => TasSettings.CycleHitboxColor2,
            2 => TasSettings.CycleHitboxColor3,
        };
    }

    private static void SetColor(int index, string colorHex) {
        Color color = HitboxColor.HexToColor(colorHex, GetColor(index));

        if (index == 0) {
            TasSettings.CycleHitboxColor1 = color;
        } else if (index == 1) {
            TasSettings.CycleHitboxColor2 = color;
        } else if (index == 2) {
            TasSettings.CycleHitboxColor3 = color;
        }
    }

    public static TextMenu.Item CreateCycleHitboxColorButton(int index, TextMenu textMenu, bool inGame) {
        TextMenu.Item item = new TextMenu.Button($"Cycle{index + 1} Hitbox Color".ToDialogText() + $": {HitboxColor.ColorToHex(GetColor(index))}")
            .Pressed(
                () => {
                    Audio.Play("event:/ui/main/savefile_rename_start");
                    textMenu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(HitboxColor.ColorToHex(GetColor(index)),
                        value => SetColor(index, value), 9);
                });
        item.Disabled = inGame;
        return item;
    }
}