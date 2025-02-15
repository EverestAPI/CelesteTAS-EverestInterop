using Celeste;
using JetBrains.Annotations;
using Monocle;
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

    /// Invoked after the current scene was updated
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    public class PostSceneUpdate : Attribute;

    [Load]
    private static void Load() {
        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.Update))!
            .HookAfter((EntityList entityList) => PostUpdate?.Invoke(entityList.Scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.DebugRender))!
            .HookAfter((EntityList entityList) => PostDebugRender?.Invoke(entityList.Scene));

        typeof(Scene)
            .GetMethodInfo(nameof(Scene.AfterUpdate))!
            .HookAfter((Scene scene) => AttributeUtils.Invoke<PostSceneUpdate>(scene));

        AttributeUtils.CollectOwnMethods<PostSceneUpdate>(typeof(Scene));
    }
}
