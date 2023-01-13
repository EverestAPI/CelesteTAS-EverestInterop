﻿using System;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class CycleHitboxColor {
    public static readonly Color DefaultColor1 = Color.Red;
    public static readonly Color DefaultColor2 = Color.Yellow;
    public static readonly Color DefaultColor3 = new(0.1f, 0.2f, 1f);
    public static readonly Color DefaultOthersColor = new(0.25f, 1f, 0.5f);

    private static readonly Dictionary<Type, GetDelegate<object, float>> OffsetGetters = new();

    // This counter defines which group is assigned to which third frame
    public static int GroupCounter;

    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += EntityOnDebugRender;
        On.Monocle.Scene.BeforeUpdate += SceneOnBeforeUpdate;
        On.Monocle.Scene.Begin += SceneOnBegin;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= EntityOnDebugRender;
        On.Monocle.Scene.BeforeUpdate -= SceneOnBeforeUpdate;
        On.Monocle.Scene.Begin -= SceneOnBegin;
    }

    [Initialize]
    private static void Initialize() {
        Dictionary<Type, string> types = new();

        if (ModUtils.GetType("FrostHelper", "FrostHelper.CustomSpinner") is { } frostSpinnerType) {
            types.Add(frostSpinnerType, "offset");
        }

        if (ModUtils.GetType("VivHelper", "VivHelper.Entities.CustomSpinner") is { } vidSpinnerType) {
            types.Add(vidSpinnerType, "offset");
        }

        if (ModUtils.GetType("ChronoHelper", "Celeste.Mod.ChronoHelper.Entities.ShatterSpinner") is { } chronoSpinnerType) {
            types.Add(chronoSpinnerType, "offset");
        }

        if (ModUtils.GetType("ChronoHelper", "Celeste.Mod.ChronoHelper.Entities.DarkLightning") is { } darkLightningType) {
            types.Add(darkLightningType, "toggleOffset");
        }

        foreach (Type type in types.Keys) {
            if (type.CreateGetDelegate<object, float>(types[type]) is { } offsetGetter) {
                OffsetGetters[type] = offsetGetter;
            }
        }
    }

    private static void EntityOnDebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (TasSettings.ShowHitboxes && TasSettings.ShowCycleHitboxColors) {
            float? offset = self switch {
                CrystalStaticSpinner spinner => spinner.offset,
                DustStaticSpinner dust => dust.offset,
                Lightning lightning => lightning.toggleOffset,
                _ => null
            };

            if (offset == null && OffsetGetters.TryGetValue(self.GetType(), out GetDelegate<object, float> getter)) {
                offset = getter(self);
            }

            if (offset is { } offsetValue) {
                // Calculate how many frames away is the hazard's loading check (time distance)
                float time = self.Scene.TimeActive;
                int timeDist = 0;

                while (Math.Floor((time - offsetValue - Engine.DeltaTime) / 0.05f) >= Math.Floor((time - offsetValue) / 0.05f) && timeDist < 3) {
                    time += Engine.DeltaTime;
                    timeDist++;
                }

                // Calculate what the value of the counter is after the time distance, which defines the hazard's group
                int group = 3;
                if (timeDist < 3) {
                    group = (timeDist + GroupCounter) % 3;
                }

                self.Collider.Render(camera, GetColor(group) * (self.Collidable ? 1f : HitboxColor.UnCollidableAlpha));
                return;
            }
        }

        orig(self, camera);
    }

    private static void SceneOnBeforeUpdate(On.Monocle.Scene.orig_BeforeUpdate orig, Scene self) {
        orig(self);
        if (!self.Paused) {
            // If the scene isn't paused (TimeActive is increased), advance the spinner group counter
            GroupCounter = (GroupCounter + 1) % 3;
        }
    }

    private static void SceneOnBegin(On.Monocle.Scene.orig_Begin orig, Scene self) {
        orig(self);
        GroupCounter = 0;
    }

    private static Color GetColor(int index) {
        return index switch {
            0 => TasSettings.CycleHitboxColor1,
            1 => TasSettings.CycleHitboxColor2,
            2 => TasSettings.CycleHitboxColor3,
            3 => TasSettings.OtherCyclesHitboxColor,
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
        } else if (index == 3) {
            TasSettings.OtherCyclesHitboxColor = color;
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

    [Command("cycle1_hitbox_color", "change the cycle 1 hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeCycleHitboxColor1(string color) {
        TasSettings.CycleHitboxColor1 = HitboxColor.HexToColor(color, DefaultColor1);
        CelesteTasModule.Instance.SaveSettings();
    }

    [Command("cycle2_hitbox_color", "change the cycle 2 hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeCycleHitboxColor2(string color) {
        TasSettings.CycleHitboxColor2 = HitboxColor.HexToColor(color, DefaultColor2);
        CelesteTasModule.Instance.SaveSettings();
    }

    [Command("cycle3_hitbox_color", "change the cycle 3 hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeCycleHitboxColor3(string color) {
        TasSettings.CycleHitboxColor3 = HitboxColor.HexToColor(color, DefaultColor3);
        CelesteTasModule.Instance.SaveSettings();
    }

    [Command("other_cycles_hitbox_color", "change other cycles hitbox color (ARGB). eg Red = F00 or FF00 or FFFF0000 (CelesteTAS)")]
    private static void CmdChangeOtherCyclesHitboxColor(string color) {
        TasSettings.OtherCyclesHitboxColor = HitboxColor.HexToColor(color, DefaultOthersColor);
        CelesteTasModule.Instance.SaveSettings();
    }
}