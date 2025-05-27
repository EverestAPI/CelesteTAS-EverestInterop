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

    /// Invoked after everything is rendered
    public static event Action? PostRender;

    [Load]
    private static void Load() {
        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.Update))!
            .HookAfter((EntityList entityList) => PostUpdate?.Invoke(entityList.Scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.DebugRender))!
            .HookAfter((EntityList entityList) => PostDebugRender?.Invoke(entityList.Scene));

        typeof(Engine)
            .GetMethodInfo(nameof(Engine.RenderCore))!
            .HookAfter(() => PostRender?.Invoke());
    }
}
