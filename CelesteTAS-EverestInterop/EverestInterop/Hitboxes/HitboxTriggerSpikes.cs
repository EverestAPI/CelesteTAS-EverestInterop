using System;
using System.Reflection;
using Celeste;
using Celeste.Mod.Entities;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.Hitboxes {
    public static class HitboxTriggerSpikes {
        private static readonly Func<TriggerSpikes, TriggerSpikes.Directions> TriggerSpikesDirection =
            typeof(TriggerSpikes).GetFieldInfo("direction").CreateDelegate_Get<Func<TriggerSpikes, TriggerSpikes.Directions>>();

        private static readonly Func<object, Array> TriggerSpikesSpikes = typeof(TriggerSpikes).CreateDelegate_Get<object, Array>("spikes");

        private static FieldInfo triggerSpikesTriggered;
        private static FieldInfo triggerSpikesLerp;

        private static readonly Func<TriggerSpikesOriginal, TriggerSpikesOriginal.Directions> TriggerSpikesOriginalDirection =
            typeof(TriggerSpikesOriginal).GetFieldInfo("direction")
                .CreateDelegate_Get<Func<TriggerSpikesOriginal, TriggerSpikesOriginal.Directions>>();

        private static readonly Func<object, Array> TriggerSpikesOriginalSpikes =
            typeof(TriggerSpikesOriginal).CreateDelegate_Get<object, Array>("spikes");

        private static FieldInfo triggerSpikesOriginalTriggered;
        private static FieldInfo triggerSpikesOriginalLerp;
        private static FieldInfo triggerSpikesOriginalPosition;

        private static Type groupedTriggerSpikesType;
        private static Func<object, bool> groupedTriggerSpikesTriggered;
        private static Func<object, float> groupedTriggerSpikesLerp;

        static HitboxTriggerSpikes() { }

        [Initialize]
        private static void Initialize() {
            groupedTriggerSpikesType = FakeAssembly.GetFakeEntryAssembly().GetType("Celeste.Mod.MaxHelpingHand.Entities.GroupedTriggerSpikes");
            groupedTriggerSpikesTriggered = groupedTriggerSpikesType?.CreateDelegate_Get<object, bool>("Triggered");
            groupedTriggerSpikesLerp = groupedTriggerSpikesType?.CreateDelegate_Get<object, float>("Lerp");
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
            if (!CelesteTasModule.Settings.ShowHitboxes || self.GetType() != groupedTriggerSpikesType) {
                orig(self, camera);
                return;
            }

            self.Collider.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);
            if (groupedTriggerSpikesTriggered(self) && groupedTriggerSpikesLerp(self) >= 1f) {
                self.Collider.Render(camera, HitboxColor.EntityColor);
            }
        }

        private static void ShowTriggerSpikesHitboxes(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
            if (!CelesteTasModule.Settings.ShowHitboxes || self is not TriggerSpikes && self is not TriggerSpikesOriginal) {
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
            switch (TriggerSpikesDirection(triggerSpikes)) {
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

            Array spikes = TriggerSpikesSpikes(triggerSpikes) as Array;
            for (int i = 0; i < spikes.Length; i++) {
                object spikeInfo = spikes.GetValue(i);
                if (triggerSpikesTriggered == null) {
                    triggerSpikesTriggered = spikeInfo.GetType().GetField("Triggered", BindingFlags.Instance | BindingFlags.Public);
                }

                if (triggerSpikesLerp == null) {
                    triggerSpikesLerp = spikeInfo.GetType().GetField("Lerp", BindingFlags.Instance | BindingFlags.Public);
                }

                if ((bool) triggerSpikesTriggered.GetValue(spikeInfo) && (float) triggerSpikesLerp.GetValue(spikeInfo) >= 1f) {
                    Vector2 position = triggerSpikes.Position + value * (2 + i * 4) + offset;

                    int num = 1;
                    for (int j = i + 1; j < spikes.Length; j++) {
                        object nextSpikeInfo = spikes.GetValue(j);
                        if ((bool) triggerSpikesTriggered.GetValue(nextSpikeInfo) && (float) triggerSpikesLerp.GetValue(nextSpikeInfo) >= 1f) {
                            num++;
                            i++;
                        } else {
                            break;
                        }
                    }

                    Draw.HollowRect(position, 4f * (vertical ? 1 : num), 4f * (vertical ? num : 1),
                        HitboxColor.GetCustomColor(Color.Red, triggerSpikes));
                }
            }
        }

        private static void DrawSpikesHitboxes(TriggerSpikesOriginal triggerSpikes, Camera camera) {
            triggerSpikes.Collider?.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);

            Vector2 offset;
            float width, height;
            bool vertical = false;
            switch (TriggerSpikesOriginalDirection(triggerSpikes)) {
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

            Array spikes = TriggerSpikesOriginalSpikes(triggerSpikes) as Array;
            for (int i = 0; i < spikes.Length; i++) {
                object spikeInfo = spikes.GetValue(i);

                if (triggerSpikesOriginalTriggered == null) {
                    triggerSpikesOriginalTriggered = spikeInfo.GetType().GetField("Triggered", BindingFlags.Instance | BindingFlags.Public);
                }

                if (triggerSpikesOriginalLerp == null) {
                    triggerSpikesOriginalLerp = spikeInfo.GetType().GetField("Lerp", BindingFlags.Instance | BindingFlags.Public);
                }

                if (triggerSpikesOriginalPosition == null) {
                    triggerSpikesOriginalPosition = spikeInfo.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.Public);
                }

                if ((bool) triggerSpikesOriginalTriggered.GetValue(spikeInfo) && (float) triggerSpikesOriginalLerp.GetValue(spikeInfo) >= 1) {
                    int num = 1;
                    for (int j = i + 1; j < spikes.Length; j++) {
                        object nextSpikeInfo = spikes.GetValue(j);
                        if ((bool) triggerSpikesOriginalTriggered.GetValue(nextSpikeInfo) &&
                            (float) triggerSpikesOriginalLerp.GetValue(nextSpikeInfo) >= 1) {
                            num++;
                            i++;
                        } else {
                            break;
                        }
                    }

                    Vector2 position = (Vector2) triggerSpikesOriginalPosition.GetValue(spikeInfo) + triggerSpikes.Position + offset;
                    Draw.HollowRect(position, width * (vertical ? 1 : num), height * (vertical ? num : 1),
                        HitboxColor.GetCustomColor(Color.Red, triggerSpikes));
                }
            }
        }
    }
}