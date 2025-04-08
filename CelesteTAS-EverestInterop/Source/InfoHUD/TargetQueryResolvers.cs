using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.InfoHUD;

internal class GlobalInstanceResolver<T>(Func<T> instanceProvider) : IInstanceResolver where T : notnull {
    public bool CanResolve(Type type) => type == typeof(T);

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) => [instanceProvider()];
}

internal class EverestSettingsInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(EverestModuleSettings));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        return Everest.Modules.FirstOrDefault(mod => mod.SettingsType == type) is { } module ? [module._Settings] : [];
    }
}

internal class EntityInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(Entity));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (Engine.Scene == null) {
            return [];
        }

        IEnumerable<Entity> entityInstances;
        if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
            entityInstances = entities
                .Where(e => entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key);
        } else {
            entityInstances = Engine.Scene.Entities
                .Where(e => e.GetType().IsSameOrSubclassOf(type) &&
                            (entityId == null || e.GetEntityData()?.ToEntityId().Key == entityId.Value.Key));
        }

        if (componentTypes.IsEmpty()) {
            return entityInstances
                .Select(object (e) => e)
                .ToList();
        } else {
            return entityInstances
                .SelectMany(e => e.Components.Where(c =>
                    componentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType))))
                .Select(object (c) => c)
                .ToList();
        }
    }
}

internal class ComponentInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(Component));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (Engine.Scene == null) {
            return [];
        }

        IEnumerable<Component> componentInstances;
        if (Engine.Scene.Tracker.Components.TryGetValue(type, out var components)) {
            componentInstances = components;
        } else {
            componentInstances = Engine.Scene.Entities
                .SelectMany(e => e.Components)
                .Where(c => c.GetType().IsSameOrSubclassOf(type));
        }

        return componentInstances
            .Select(object (c) => c)
            .ToList();
    }
}

internal class SceneInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(Scene));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (Engine.Scene == null) {
            return [];
        }

        if (Engine.Scene.GetType().IsSameOrSubclassOf(type)) {
            return [Engine.Scene];
        }
        if (type.IsSameOrSubclassOf(typeof(Level)) && Engine.Scene.GetLevel() is { } level) {
            return [level];
        }

        return [];
    }
}

internal class SessionInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type == typeof(Session);

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (Engine.Scene?.GetSession() is { } session) {
            return [session];
        }

        return [];
    }
}
