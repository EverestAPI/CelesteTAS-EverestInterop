using Celeste;
using JetBrains.Annotations;
using Monocle;
using MonoMod.Cil;
using System;
using TAS.ModInterop;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Exposes additional events, along the ones provided by Everest
internal static class Events {

    /// Invoked after all entities have updated
    public static event Action<Scene>? PostUpdate;

    /// Invoked after all entity hitboxes have been rendered
    public static event Action<Scene>? PostDebugRender;

    /// Invoked after all entities (and their hitboxes) have been rendered
    public class PostGameplayRender(int priority = 0) : EventAttribute(priority);

    /// Invoked before the current scene is updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PreSceneUpdate : Attribute;

    /// Invoked after the current scene was updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PostSceneUpdate : Attribute;

    /// Invoked before the current scene is rendered
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PreSceneRender : Attribute;

    /// Invoked after the current scene was rendered
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PostSceneRender : Attribute;

    /// Invoked after the current scene was rendered, while an HD spritebatch is active
    public class PostSceneRenderBatch(int priority = 0) : EventAttribute(priority);

    /// Invoked while the engine is frozen
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class EngineFrozenUpdate : Attribute;

    [Load]
    private static void Load() {
        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.Update))!
            .HookAfter((EntityList entityList) => PostUpdate?.Invoke(entityList.Scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.DebugRender))!
            .HookAfter((EntityList entityList) => PostDebugRender?.Invoke(entityList.Scene));

        typeof(GameplayRenderer)
            .GetMethodInfo(nameof(GameplayRenderer.Render))!
            .IlHook((cursor, _) => {
                cursor.GotoNext(MoveType.AfterLabel, instr => instr.MatchCall<GameplayRenderer>(nameof(GameplayRenderer.End)));

                cursor.EmitLdarg1();
                cursor.EmitStaticDelegate($"Event_{nameof(PostGameplayRender)}", (Scene scene) => AttributeUtils.Invoke<PostGameplayRender>(scene));
            });
        ModUtils.GetMethod("SpirialisHelper", "Celeste.Mod.Spirialis.TimeGameplayRenderer", "Render")
            ?.IlHook((cursor, _) => {
                cursor.GotoNext(MoveType.AfterLabel, instr => instr.MatchCall("Celeste.Mod.Spirialis.TimeGameplayRenderer", "End"));

                cursor.EmitLdarg1();
                cursor.EmitStaticDelegate($"Event_{nameof(PostGameplayRender)}", (Scene scene) => AttributeUtils.Invoke<PostGameplayRender>(scene));
            });

        typeof(Engine)
            .GetMethodInfo(nameof(Engine.Update))!
            .IlHook((cursor, _) => {
                // EngineFrozenUpdate
                cursor.GotoNext(MoveType.After, instr => instr.MatchStsfld($"Monocle.{nameof(Engine)}", nameof(Engine.FreezeTimer)));
                cursor.MoveBeforeLabels();
                cursor.EmitStaticDelegate($"Event_{nameof(EngineFrozenUpdate)}", () => AttributeUtils.Invoke<EngineFrozenUpdate>());

                // PreSceneUpdate
                cursor.GotoNext(MoveType.Before, instr => instr.MatchCallvirt<Scene>(nameof(Scene.BeforeUpdate)));
                cursor.EmitDup();
                cursor.EmitStaticDelegate($"Event_{nameof(PreSceneUpdate)}", (Scene scene) => AttributeUtils.Invoke<PreSceneUpdate>(scene));

                // PostSceneUpdate
                cursor.GotoNext(MoveType.Before, instr => instr.MatchCallvirt<Scene>(nameof(Scene.AfterUpdate)));
                cursor.EmitDup();
                cursor.Index++; // Go after callvirt Scene::AfterUpdate
                cursor.MoveBeforeLabels();
                cursor.EmitStaticDelegate($"Event_{nameof(PostSceneUpdate)}", (Scene scene) => AttributeUtils.Invoke<PostSceneUpdate>(scene));
            });

        typeof(Engine)
            .GetMethodInfo(nameof(Engine.RenderCore))!
            .IlHook((cursor, _) => {
                // PreSceneRender
                cursor.GotoNext(MoveType.Before, instr => instr.MatchCallvirt<Scene>(nameof(Scene.BeforeRender)));
                cursor.EmitDup();
                cursor.EmitStaticDelegate($"Event_{nameof(PreSceneRender)}", (Scene scene) => AttributeUtils.Invoke<PreSceneRender>(scene));

                // PostSceneRender
                cursor.GotoNext(MoveType.Before, instr => instr.MatchCallvirt<Scene>(nameof(Scene.AfterRender)));
                cursor.EmitDup();
                cursor.Index++; // Go after callvirt Scene::AfterRender
                cursor.MoveBeforeLabels();
                cursor.EmitStaticDelegate($"Event_{nameof(PostSceneRender)}", (Scene scene) => {
                    AttributeUtils.Invoke<PostSceneRender>(scene);

                    Draw.SpriteBatch.Begin();
                    AttributeUtils.Invoke<PostSceneRenderBatch>(scene);
                    Draw.SpriteBatch.End();
                });
            });

        AttributeUtils.CollectOwnMethods<PreSceneUpdate>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostSceneUpdate>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PreSceneRender>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostSceneRender>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostSceneRenderBatch>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<EngineFrozenUpdate>();
    }
}
