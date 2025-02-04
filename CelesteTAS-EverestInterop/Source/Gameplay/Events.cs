using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Exposes additional events, along the ones provided by Everest
internal static class Events {

    /// Invoked after the scene has been updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PostSceneUpdate : Attribute;

    /// Invoked after all other entities have updated
    public static event Action<Scene> PostEntityUpdate = scene => AttributeUtils.Invoke<PostEntityUpdateAttribute>(scene);

    /// Invoked after all other entity hitboxes have been rendered
    public static event Action<Scene> PostEntityDebugRender = scene => AttributeUtils.Invoke<PostEntityDebugRenderAttribute>(scene);

    [Load]
    private static void Load() {
        typeof(Scene)
            .GetMethodInfo(nameof(Scene.AfterUpdate))!
            .HookAfter((Scene scene) => AttributeUtils.Invoke<PostSceneUpdate>(scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.Update))!
            .HookAfter((EntityList entityList) => PostEntityUpdate.Invoke(entityList.Scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.DebugRender))!
            .HookAfter((EntityList entityList) => PostEntityDebugRender.Invoke(entityList.Scene));

        AttributeUtils.CollectOwnMethods<PostSceneUpdate>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostEntityUpdateAttribute>(typeof(Scene));
        AttributeUtils.CollectOwnMethods<PostEntityDebugRenderAttribute>(typeof(Scene));
    }

    // Annotations for directly subscribing to certain events
    // TODO: Automatically generate these events with SG?

    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    internal class PostEntityUpdateAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    internal class PostEntityDebugRenderAttribute : Attribute;
}
