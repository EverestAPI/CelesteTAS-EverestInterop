using System;
using System.Reflection;
using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;

namespace TAS.EverestInterop.Hitboxes {
public static class HitboxTriggerSpikes {
    private static readonly FieldInfo TriggerSpikesDirection =
        typeof(TriggerSpikes).GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo TriggerSpikesSpikes =
        typeof(TriggerSpikes).GetField("spikes", BindingFlags.Instance | BindingFlags.NonPublic);

    private static FieldInfo TriggerSpikesTriggered;
    private static FieldInfo TriggerSpikesLerp;

    private static readonly FieldInfo TriggerSpikesOriginalDirection =
        typeof(TriggerSpikesOriginal).GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo TriggerSpikesOriginalSpikes =
        typeof(TriggerSpikesOriginal).GetField("spikes", BindingFlags.Instance | BindingFlags.NonPublic);

    private static FieldInfo TriggerSpikesOriginalTriggered;
    private static FieldInfo TriggerSpikesOriginalLerp;
    private static FieldInfo TriggerSpikesOriginalPosition;

    public static void Load() {
        // Show the hitbox of the triggered TriggerSpikes.
        On.Monocle.Entity.DebugRender += Entity_DebugRender;
    }

    public static void Unload() {
        On.Monocle.Entity.DebugRender -= Entity_DebugRender;
    }

    private static void Entity_DebugRender(On.Monocle.Entity.orig_DebugRender orig, Entity self, Camera camera) {
        if (!CelesteTASModule.Settings.ShowHitboxes || !(self is TriggerSpikes) && !(self is TriggerSpikesOriginal)) {
            orig(self, camera);
            return;
        }

        self.Collider?.Render(camera, HitboxColor.EntityColorInverselyLessAlpha);

        if (self is TriggerSpikes) {
            Vector2 offset, value;
            bool vertical = false;
            switch (TriggerSpikesDirection.GetValue(self)) {
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

            Array spikes = TriggerSpikesSpikes.GetValue(self) as Array;
            for (int i = 0; i < spikes.Length; i++) {
                object spikeInfo = spikes.GetValue(i);
                if (TriggerSpikesTriggered == null) {
                    TriggerSpikesTriggered = spikeInfo.GetType().GetField("Triggered", BindingFlags.Instance | BindingFlags.Public);
                }

                if (TriggerSpikesLerp == null) {
                    TriggerSpikesLerp = spikeInfo.GetType().GetField("Lerp", BindingFlags.Instance | BindingFlags.Public);
                }

                if ((bool) TriggerSpikesTriggered.GetValue(spikeInfo) && (float) TriggerSpikesLerp.GetValue(spikeInfo) >= 1f) {
                    Vector2 position = self.Position + value * (2 + i * 4) + offset;

                    int num = 1;
                    for (int j = i + 1; j < spikes.Length; j++) {
                        object nextSpikeInfo = spikes.GetValue(j);
                        if ((bool) TriggerSpikesTriggered.GetValue(nextSpikeInfo) && (float) TriggerSpikesLerp.GetValue(nextSpikeInfo) >= 1f) {
                            num++;
                            i++;
                        } else {
                            break;
                        }
                    }

                    Draw.HollowRect(position, 4f * (vertical ? 1 : num), 4f * (vertical ? num : 1),
                        HitboxColor.GetCustomColor(Color.Red, self));
                }
            }
        } else if (self is TriggerSpikesOriginal) {
            Vector2 offset;
            float width, height;
            bool vertical = false;
            switch (TriggerSpikesOriginalDirection.GetValue(self)) {
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

            Array spikes = TriggerSpikesOriginalSpikes.GetValue(self) as Array;
            for (int i = 0; i < spikes.Length; i++) {
                object spikeInfo = spikes.GetValue(i);

                if (TriggerSpikesOriginalTriggered == null) {
                    TriggerSpikesOriginalTriggered = spikeInfo.GetType().GetField("Triggered", BindingFlags.Instance | BindingFlags.Public);
                }

                if (TriggerSpikesOriginalLerp == null) {
                    TriggerSpikesOriginalLerp = spikeInfo.GetType().GetField("Lerp", BindingFlags.Instance | BindingFlags.Public);
                }

                if (TriggerSpikesOriginalPosition == null) {
                    TriggerSpikesOriginalPosition = spikeInfo.GetType().GetField("Position", BindingFlags.Instance | BindingFlags.Public);
                }

                if ((bool) TriggerSpikesOriginalTriggered.GetValue(spikeInfo) && (float) TriggerSpikesOriginalLerp.GetValue(spikeInfo) >= 1) {
                    int num = 1;
                    for (int j = i + 1; j < spikes.Length; j++) {
                        object nextSpikeInfo = spikes.GetValue(j);
                        if ((bool) TriggerSpikesOriginalTriggered.GetValue(nextSpikeInfo) &&
                            (float) TriggerSpikesOriginalLerp.GetValue(nextSpikeInfo) >= 1) {
                            num++;
                            i++;
                        } else {
                            break;
                        }
                    }

                    Vector2 position = (Vector2) TriggerSpikesOriginalPosition.GetValue(spikeInfo) + self.Position + offset;
                    Draw.HollowRect(position, width * (vertical ? 1 : num), height * (vertical ? num : 1),
                        HitboxColor.GetCustomColor(Color.Red, self));
                }
            }
        }
    }
}
}