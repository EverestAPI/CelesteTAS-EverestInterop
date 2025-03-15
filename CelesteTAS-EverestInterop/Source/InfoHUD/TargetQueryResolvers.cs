using Celeste;
using Celeste.Mod;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.InfoHUD;

internal record GlobalInstanceResolver<T>(T Instance) : IInstanceResolver where T : notnull {
    public bool CanResolve(Type type) => type == typeof(T);

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) => [Instance];
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
                .Select(e => (object)e)
                .ToList();
        } else {
            return entityInstances
                .SelectMany(e => e.Components.Where(c =>
                    componentTypes.Any(componentType => c.GetType().IsSameOrSubclassOf(componentType))))
                .Select(c => (object)c)
                .ToList();
        }
    }
}

internal class ComponentInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type.IsSameOrSubclassOf(typeof(Component));

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        IEnumerable<Component> componentInstances;
        if (Engine.Scene.Tracker.Components.TryGetValue(type, out var components)) {
            componentInstances = components;
        } else {
            componentInstances = Engine.Scene.Entities
                .SelectMany(e => e.Components)
                .Where(c => c.GetType().IsSameOrSubclassOf(type));
        }

        return componentInstances
            .Select(c => (object)c)
            .ToList();
    }
}

internal class SceneInstanceResolver : IInstanceResolver {
    public bool CanResolve(Type type) => type == typeof(Session) || type == Engine.Scene.GetType();

    public List<object> Resolve(Type type, List<Type> componentTypes, EntityID? entityId) {
        if (type == typeof(Session) && Engine.Scene is Level level) {
            return [level.Session];
        }

        return [Engine.Scene];
    }
}
