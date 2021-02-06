using System;
using System.Linq;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;

namespace TAS.EverestInterop {
class GraphicsCore {
    public static GraphicsCore instance;

    private static string clickedEntityType = "";
    private static ButtonState lastButtonState;

    private static CelesteTASModuleSettings Settings => CelesteTASModule.Settings;

    public void Load() {
        // Forced: Add more positions to top-left positioning helper.
        IL.Monocle.Commands.Render += Commands_Render;

        // Optional: Show the pathfinder.
        IL.Celeste.Level.Render += Level_Render;
        IL.Celeste.Pathfinder.Render += Pathfinder_Render;

        // Hide distortion when showing hitboxes
        On.Celeste.Distort.Render += Distort_Render;
    }

    public void Unload() {
        IL.Monocle.Commands.Render -= Commands_Render;
        IL.Celeste.Level.Render -= Level_Render;
        IL.Celeste.Pathfinder.Render -= Pathfinder_Render;
        On.Celeste.Distort.Render -= Distort_Render;
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
            Level level = Engine.Scene as Level;
            int x = (int) xObj;
            int y = (int) yObj;
            int worldX = (int) Math.Round(x + level.LevelOffset.X);
            int worldY = (int) Math.Round(y + level.LevelOffset.Y);

            MouseState mouseState = Mouse.GetState();
            if (mouseState.LeftButton == ButtonState.Pressed && lastButtonState == ButtonState.Released) {
                Entity tempEntity = new Entity {Position = new Vector2(worldX, worldY), Collider = new Hitbox(1, 1)};
                Entity clickedEntity = level.Entities.Where(entity => !(entity is Trigger)
                                                                      && !(entity is LookoutBlocker)
                                                                      && !(entity is Killbox)
                                                                      && !(entity is WindController)
                                                                      && !(entity is Water)
                                                                      && !(entity is WaterFall)
                                                                      && !(entity is BigWaterfall)
                                                                      && !(entity is PlaybackBillboard)
                                                                      && !(entity is ParticleSystem))
                    .FirstOrDefault(entity => entity.CollideCheck(tempEntity));
                if (clickedEntity?.GetType() is Type type) {
                    if (type.Assembly == typeof(Celeste.Celeste).Assembly) {
                        clickedEntityType = type.Name;
                    } else {
                        // StartExport uses a comma as a separator, so we can't use comma,
                        // use @ to place it and replace it back with a comma when looking for the type
                        clickedEntityType = type.FullName + "@" + type.Assembly.GetName().Name;
                    }

                    if (!string.IsNullOrEmpty(clickedEntityType)) {
                        ("Type of entity to be clicked: " + clickedEntityType).Log();
                    }
                } else {
                    clickedEntityType = string.Empty;
                }
            }

            lastButtonState = mouseState.LeftButton;

            return
                (string.IsNullOrEmpty(clickedEntityType) ? string.Empty : $"\n entity: {clickedEntityType}") +
                $"\n world:       {worldX}, {worldY}" +
                $"\n level:       {x}, {y}";
        });
    }

    private static void Distort_Render(On.Celeste.Distort.orig_Render orig, Texture2D source, Texture2D map, bool hasDistortion) {
        if (GameplayRendererExt.RenderDebug || Settings.SimplifiedGraphics && Settings.SimplifiedDistort) {
            Distort.Anxiety = 0f;
            Distort.GameRate = 1f;
            hasDistortion = false;
        }

        orig(source, map, hasDistortion);
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
        c.Emit(OpCodes.Callvirt, GetEntity.MakeGenericMethod(new Type[] {typeof(Seeker)}));
    }

    private void Pathfinder_Render(ILContext il) {
        // Remove the for loop which draws pathfinder tiles
        ILCursor c = new ILCursor(il);
        c.FindNext(out ILCursor[] found, i => i.MatchLdfld(typeof(Pathfinder), "lastPath"));
        c.RemoveRange(found[0].Index - 1);
    }
}
}