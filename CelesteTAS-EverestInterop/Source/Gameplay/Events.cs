using JetBrains.Annotations;
using Monocle;
using MonoMod.Cil;
using System;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Exposes additional events, along the ones provided by Everest
internal static class Events {

    /// Invoked after all other entities have updated
    public static event Action<Scene>? PostUpdate;

    /// Invoked after all other entity hitboxes have been rendered
    public static event Action<Scene>? PostDebugRender;

    /// Invoked before the current scene is updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PreSceneUpdate : Attribute;

    /// Invoked after the current scene was updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PostSceneUpdate : Attribute;

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

        AttributeUtils.CollectOwnMethods<PreSceneUpdate>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostSceneUpdate>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<EngineFrozenUpdate>();
    }
}
