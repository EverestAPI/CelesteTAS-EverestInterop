using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using TAS.Module;
using TAS.Utils;

namespace TAS.Gameplay;

/// Exposes additional events, along the ones provided by Everest
internal static class Events {

    /// Invoked after everything else has been updated for the frame. Also invoked after freeze frames
    [AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
    internal class PostUpdateAttribute : Attribute;

    /// Invoked after all other entities have updated
    public static event Action<Scene> PostEntityUpdate = scene => AttributeUtils.Invoke<PostEntityUpdateAttribute>(scene);

    /// Invoked after all other entity hitboxes have been rendered
    public static event Action<Scene> PostEntityDebugRender = scene => AttributeUtils.Invoke<PostEntityDebugRenderAttribute>(scene);

    [Load]
    private static void Load() {
        typeof(Engine)
            .GetMethodInfo(nameof(Engine.Update))!
            .OnHook((On.Monocle.Engine.orig_Update orig, Engine self, GameTime gameTime) => {
                bool wasFrozen = Engine.FreezeTimer > 0.0f;
                orig(self, gameTime);
                if (wasFrozen) {
                    AttributeUtils.Invoke<PostUpdateAttribute>();
                }
            });
        typeof(Scene)
            .GetMethodInfo(nameof(Scene.AfterUpdate))!
            // For **unknown reasons** this has to be an On-Hook instead of an IL HookAfter ???
            .OnHook((On.Monocle.Scene.orig_AfterUpdate orig, Scene self) => {
                orig(self);
                AttributeUtils.Invoke<PostUpdateAttribute>();
            });

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.Update))!
            .HookAfter((EntityList entityList) => PostEntityUpdate.Invoke(entityList.Scene));

        typeof(EntityList)
            .GetMethodInfo(nameof(EntityList.DebugRender))!
            .HookAfter((EntityList entityList) => PostEntityDebugRender.Invoke(entityList.Scene));

        AttributeUtils.CollectOwnMethods<PostUpdateAttribute>();
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
