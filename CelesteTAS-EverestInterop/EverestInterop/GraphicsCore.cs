using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using System;
using System.Reflection;
using Celeste.Mod.Entities;

namespace TAS.EverestInterop {
    class GraphicsCore {
        public static GraphicsCore instance;

        public static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

        private static readonly FieldInfo TriggerSpikesDirection = typeof(TriggerSpikes).GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TriggerSpikesSpikes = typeof(TriggerSpikes).GetField("spikes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static  FieldInfo TriggerSpikesTriggered;
        private static  FieldInfo TriggerSpikesLerp;

        private static readonly FieldInfo TriggerSpikesOriginalDirection = typeof(TriggerSpikesOriginal).GetField("direction", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TriggerSpikesOriginalSpikes = typeof(TriggerSpikesOriginal).GetField("spikes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static  FieldInfo TriggerSpikesOriginalTriggered;
        private static  FieldInfo TriggerSpikesOriginalLerp;
        private static  FieldInfo TriggerSpikesOriginalPosition;

        public void Load() {
            // Forced: Add more positions to top-left positioning helper.
            IL.Monocle.Commands.Render += Commands_Render;

            // Optional: Show the pathfinder.
            IL.Celeste.Level.Render += Level_Render;
            IL.Celeste.Pathfinder.Render += Pathfinder_Render;

            // Hide distortion when showing hitboxes
            On.Celeste.Distort.Render += Distort_Render;

            // Hide SoundSource when showing hitboxes
            On.Celeste.SoundSource.DebugRender += SoundSource_DebugRender;

            // Show pufferfish explosion radius
            On.Celeste.Puffer.Render += Puffer_Render;

            // Show the hitbox of the triggered TriggerSpikes.
            On.Monocle.Entity.DebugRender += Entity_DebugRender;

            // Stop updating tentacles texture when fast forward
            On.Celeste.ReflectionTentacles.UpdateVertices += ReflectionTentaclesOnUpdateVertices;
        }

        public void Unload() {
            IL.Monocle.Commands.Render -= Commands_Render;
            IL.Celeste.Level.Render -= Level_Render;
            IL.Celeste.Pathfinder.Render -= Pathfinder_Render;
            On.Celeste.Distort.Render -= Distort_Render;
            On.Celeste.SoundSource.DebugRender -= SoundSource_DebugRender;
            On.Celeste.Puffer.Render -= Puffer_Render;
            On.Monocle.Entity.DebugRender -= Entity_DebugRender;
            On.Celeste.ReflectionTentacles.UpdateVertices -= ReflectionTentaclesOnUpdateVertices;
        }

        public static void Commands_Render(ILContext il) {
            // Hijack string.Format("\n level:       {0}, {1}", xObj, yObj)
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdstr("\n level:       {0}, {1}"),
                i => i.MatchCall(typeof(string), "Format")
            );
            ILCursor c = found[1];
            c.Remove();
            c.EmitDelegate<Func<string, object, object, string>>((text, xObj, yObj) => {
                int x = (int)xObj;
                int y = (int)yObj;
                Level level = Engine.Scene as Level;
                return
                    $"\n world:       {(int)Math.Round(x + level.LevelOffset.X)}, {(int)Math.Round(y + level.LevelOffset.Y)}" +
                    $"\n level:       {x}, {y}";
            });
        }

        private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion) {
            if (GameplayRendererExt.RenderDebug || Settings.SimplifiedGraphics) {
                Distort.Anxiety = 0f;
                Distort.GameRate = 1f;
                hasDistortion = false;
            }
            orig(source, map, hasDistortion);
        }

        private static void SoundSource_DebugRender(On.Celeste.SoundSource.orig_DebugRender orig, SoundSource self, Camera camera) {
            if (!Settings.ShowHitboxes)
                orig(self, camera);
        }

        private void Puffer_Render(On.Celeste.Puffer.orig_Render orig, Puffer self) {
            if (GameplayRendererExt.RenderDebug)
                Draw.Circle(self.Position, 32f, Color.Red, 32);
            orig(self);
        }

        private void Entity_DebugRender(On.Monocle.Entity.orig_DebugRender orig, Monocle.Entity self, Camera camera) {
            orig(self, camera);

            if (self is TriggerSpikes) {
                Vector2 offset, value;
                bool vertical = false;
                switch (TriggerSpikesDirection.GetValue(self)) {
                    case TriggerSpikes.Directions.Up:
                        offset = new Vector2(-2f, -4f);
                        value = new Vector2(1f, 0f);
                        break;
                    case TriggerSpikes.Directions.Down:
                        self.Collider.Render(camera, Color.Aqua);
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
                for (var i = 0; i < spikes.Length; i++) {
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
                        for (var j = i + 1; j < spikes.Length; j++) {
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
                        self.Collider.Render(camera, Color.Aqua);
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
                for (var i = 0; i < spikes.Length; i++) {
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
                        for (var j = i + 1; j < spikes.Length; j++) {
                            object nextSpikeInfo = spikes.GetValue(j);
                            if ((bool) TriggerSpikesOriginalTriggered.GetValue(nextSpikeInfo) && (float) TriggerSpikesOriginalLerp.GetValue(nextSpikeInfo) >= 1) {
                                num++;
                                i++;
                            } else {
                                break;
                            }
                        }

                        Vector2 position = (Vector2) TriggerSpikesOriginalPosition.GetValue(spikeInfo) + self.Position + offset;
                        Draw.HollowRect(position, width * (vertical ? 1 : num), height * (vertical ? num : 1), HitboxColor.GetCustomColor(Color.Red, self));
                    }
                }
            }
        }

        private void ReflectionTentaclesOnUpdateVertices(On.Celeste.ReflectionTentacles.orig_UpdateVertices orig, ReflectionTentacles self) {
            if ((Manager.state == State.Enable && Manager.FrameLoops > 1) || Settings.SimplifiedGraphics)
                return;

            orig(self);
        }

        public static void Level_Render(ILContext il) {
            ILCursor c;
            new ILCursor(il).FindNext(out ILCursor[] found,
                i => i.MatchLdfld(typeof(Pathfinder), "DebugRenderEnabled"),
                i => i.MatchCall(typeof(Draw), "get_SpriteBatch"),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0),
                i => i.MatchLdarg(0)
            );

            // Place labels at and after pathfinder rendering code
            ILLabel render = il.DefineLabel();
            ILLabel skipRender = il.DefineLabel();
            c = found[1];
            c.MarkLabel(render);
            c = found[4];
            c.MarkLabel(skipRender);

            // || the value of DebugRenderEnabled with Debug rendering being enabled, && with seekers being present.
            c = found[0];
            c.Index++;
            c.Emit(OpCodes.Brtrue_S, render.Target);
            c.Emit(OpCodes.Call, typeof(GameplayRendererExt).GetMethod("get_RenderDebug"));
            c.Emit(OpCodes.Brfalse_S, skipRender.Target);
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Callvirt, typeof(Scene).GetMethod("get_Tracker"));
            MethodInfo GetEntity = typeof(Tracker).GetMethod("GetEntity");
            c.Emit(OpCodes.Callvirt, GetEntity.MakeGenericMethod(new Type[] { typeof(Seeker) }));
        }

        private void Pathfinder_Render(ILContext il) {
            // Remove the for loop which draws pathfinder tiles
            ILCursor c = new ILCursor(il);
            c.FindNext(out ILCursor[] found, i => i.MatchLdfld(typeof(Pathfinder), "lastPath"));
            c.RemoveRange(found[0].Index - 1);
        }
    }
}