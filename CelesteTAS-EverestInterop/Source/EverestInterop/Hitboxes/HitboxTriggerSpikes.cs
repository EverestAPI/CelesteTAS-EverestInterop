using System;
using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes;

public static class HitboxTriggerSpikes {
    private static Type? groupedTriggerSpikesType;
    private static GetDelegate<object, bool>? groupedTriggerSpikesTriggered;
    private static GetDelegate<object, float>? groupedTriggerSpikesLerp;

    [Initialize]
    private static void Initialize() {
        groupedTriggerSpikesType = ModUtils.GetType("MaxHelpingHand", "Celeste.Mod.MaxHelpingHand.Entities.GroupedTriggerSpikes");
        groupedTriggerSpikesTriggered = groupedTriggerSpikesType?.CreateGetDelegate<object, bool>("Triggered");
        groupedTriggerSpikesLerp = groupedTriggerSpikesType?.CreateGetDelegate<object, float>("Lerp");
        if (groupedTriggerSpikesType != null && groupedTriggerSpikesTriggered != null && groupedTriggerSpikesLerp != null) {
            On.Monocle.Entity.DebugRender += ShowGroupedTriggerSpikesHitboxes;
        }
    }

    [Load]
    private static void Load() {
        On.Monocle.Entity.DebugRender += ShowTriggerSpikesHitboxes;
    }

    [Unload]
    private static void Unload() {
        On.Monocle.Entity.DebugRender -= ShowGroupedTriggerSpikesHitboxes;
        On.Monocle.Entity.DebugRender -= ShowTriggerSpikesHitboxes;
    }

    private static void ShowGroupedTriggerSpikesHitboxes(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!TasSettings.ShowHitboxes || self.GetType() != groupedTriggerSpikesType) {
            orig(self, camera);
            return;
        }

        self.Collider.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);
        if (groupedTriggerSpikesTriggered!(self) && groupedTriggerSpikesLerp!(self) >= 1f) {
            self.Collider.Render(camera, HitboxColor.EntityColor);
        }
    }

    private static void ShowTriggerSpikesHitboxes(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!TasSettings.ShowHitboxes || self is not TriggerSpikes && self is not TriggerSpikesOriginal) {
            orig(self, camera);
            return;
        }

        if (self is TriggerSpikes triggerSpikes) {
            DrawSpikesHitboxes(triggerSpikes, camera);
        } else if (self is TriggerSpikesOriginal triggerSpikesOriginal) {
            DrawSpikesHitboxes(triggerSpikesOriginal, camera);
        }
    }

    private static void DrawSpikesHitboxes(TriggerSpikes triggerSpikes, Camera camera) {
        triggerSpikes.Collider?.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);

        Vector2 offset, value;
        bool vertical = false;
        switch (triggerSpikes.direction) {
            case TriggerSpikes.Directions.Up:
                offset = new Vector2(-2f, -4f);
                value = new Vector2(1f, 0f);
                break;
            case TriggerSpikes.Directions.Down:
                offset = new Vector2(-2f, 0f);
                value = new Vector2(1f, 0f);
                break;
            case TriggerSpikes.Directions.Left:
                offset = new Vector2(-4f, -2f);
                value = new Vector2(0f, 1f);
                vertical = true;
                break;
            case TriggerSpikes.Directions.Right:
                offset = new Vector2(0f, -2f);
                value = new Vector2(0f, 1f);
                vertical = true;
                break;
            default:
                return;
        }

        TriggerSpikes.SpikeInfo[] spikes = triggerSpikes.spikes;
        for (int i = 0; i < spikes.Length; i++) {
            TriggerSpikes.SpikeInfo spikeInfo = spikes[i];
            if (spikeInfo.Triggered && spikeInfo.Lerp >= 1f) {
                Vector2 position = triggerSpikes.Position + value * (2 + i * 4) + offset;

                bool startFromZero = i == 0;
                int num = 1;
                for (int j = i + 1; j < spikes.Length; j++) {
                    TriggerSpikes.SpikeInfo nextSpikeInfo = spikes[j];
                    if (nextSpikeInfo.Triggered && nextSpikeInfo.Lerp >= 1f) {
                        num++;
                        i++;
                    } else {
                        break;
                    }
                }

                float totalWidth = 4f * (vertical ? 1 : num);
                float totalHeight = 4f * (vertical ? num : 1);
                if (!startFromZero) {
                    if (vertical) {
                        position.Y -= 1;
                        totalHeight += 1;
                    } else {
                        position.X -= 1;
                        totalWidth += 1;
                    }
                }

                Draw.HollowRect(position, totalWidth, totalHeight, HitboxColor.GetCustomColor(triggerSpikes));
            }
        }
    }

    private static void DrawSpikesHitboxes(TriggerSpikesOriginal triggerSpikes, Camera camera) {
        triggerSpikes.Collider?.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);

        Vector2 offset;
        float width, height;
        bool vertical = false;
        switch (triggerSpikes.direction) {
            case TriggerSpikesOriginal.Directions.Up:
                width = 8f;
                height = 3f;
                offset = new Vector2(-4f, -4f);
                break;
            case TriggerSpikesOriginal.Directions.Down:
                width = 8f;
                height = 3f;
                offset = new Vector2(-4f, 1f);
                break;
            case TriggerSpikesOriginal.Directions.Left:
                width = 3f;
                height = 8f;
                offset = new Vector2(-4f, -4f);
                vertical = true;
                break;
            case TriggerSpikesOriginal.Directions.Right:
                width = 3f;
                height = 8f;
                offset = new Vector2(1f, -4f);
                vertical = true;
                break;
            default:
                return;
        }

        TriggerSpikesOriginal.SpikeInfo[] spikes = triggerSpikes.spikes;
        for (int i = 0; i < spikes.Length; i++) {
            TriggerSpikesOriginal.SpikeInfo spikeInfo = spikes[i];

            if (spikeInfo.Triggered && spikeInfo.Lerp >= 1) {
                bool startFromZero = i == 0;
                int num = 1;
                for (int j = i + 1; j < spikes.Length; j++) {
                    TriggerSpikesOriginal.SpikeInfo nextSpikeInfo = spikes[j];
                    if (nextSpikeInfo.Triggered && nextSpikeInfo.Lerp >= 1) {
                        num++;
                        i++;
                    } else {
                        break;
                    }
                }

                Vector2 position = spikeInfo.Position + triggerSpikes.Position + offset;
                float totalWidth = width * (vertical ? 1 : num);
                float totalHeight = height * (vertical ? num : 1);
                if (!startFromZero) {
                    if (vertical) {
                        position.Y -= 1;
                        totalHeight += 1;
                    } else {
                        position.X -= 1;
                        totalWidth += 1;
                    }
                }

                Draw.HollowRect(position, totalWidth, totalHeight, HitboxColor.GetCustomColor(triggerSpikes));
            }
        }
    }
}
