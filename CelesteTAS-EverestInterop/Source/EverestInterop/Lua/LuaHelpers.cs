using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.EverestInterop.Lua;

public static class LuaHelpers {
    public static Entity GetEntity(string typeName) {
        if (TryGetEntityType(typeName, out Type type)) {
            if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
                return entities.FirstOrDefault();
            } else {
                return Engine.Scene.FirstOrDefault(entity => entity.GetType().IsSameOrSubclassOf(type));
            }
        } else {
            return null;
        }
    }

    public static List<Entity> GetEntities(string typeName) {
        if (TryGetEntityType(typeName, out Type type)) {
            if (Engine.Scene.Tracker.Entities.TryGetValue(type, out var entities)) {
                return entities;
            } else {
                return Engine.Scene.Where(entity => entity.GetType().IsSameOrSubclassOf(type)).ToList();
            }
        } else {
            return new List<Entity>();
        }
    }

    // entityTypeName can be "Player" or "Celeste.Player"
    private static bool TryGetEntityType(string entityTypeName, out Type type) {
        if (InfoCustom.TryParseTypes(entityTypeName, out List<Type> types)) {
            type = types.FirstOrDefault(t => t.IsSameOrSubclassOf(typeof(Entity)));
            return type != null;
        } else {
            type = null;
            return false;
        }
    }

    private static bool TryGetType(string typeName, out Type type) {
        if (InfoCustom.TryParseTypes(typeName, out List<Type> types)) {
            type = types.FirstOrDefault();
            return type != null;
        } else {
            type = null;
            return false;
        }
    }

    // Get field or property value
    public static object GetValue(object instanceOrTypeName, string memberName) {
        bool staticMember = false;
        if (instanceOrTypeName is string typeName && TryGetType(typeName, out Type type)) {
            staticMember = true;
        } else if (instanceOrTypeName != null) {
            type = instanceOrTypeName.GetType();
        } else {
            return null;
        }

        MemberInfo memberInfo = type.GetMemberInfo(memberName);
        if (memberInfo == null) {
            return null;
        } else {
            if (memberInfo.MemberType == MemberTypes.Field) {
                if (staticMember) {
                    return type.GetFieldValue<object>(memberName);
                } else {
                    return instanceOrTypeName.GetFieldValue<object>(memberName);
                }
            } else if (memberInfo.MemberType == MemberTypes.Property) {
                if (staticMember) {
                    return type.GetPropertyValue<object>(memberName);
                } else {
                    return instanceOrTypeName.GetPropertyValue<object>(memberName);
                }
            } else {
                return null;
            }
        }
    }

    public static object InvokeMethod(object instanceOrTypeName, string methodName, params object[] parameters) {
        bool staticMethod = false;
        if (instanceOrTypeName is string typeName && TryGetType(typeName, out Type type)) {
            staticMethod = true;
        } else if (instanceOrTypeName != null) {
            type = instanceOrTypeName.GetType();
        } else {
            return null;
        }

        // TODO Overloaded methods are not supported
        if (staticMethod) {
            return type.InvokeMethod<object>(methodName, parameters);
        } else {
            return instanceOrTypeName.InvokeMethod<object>(methodName, parameters);
        }
    }

    public static object GetEnum(string enumTypeName, string value) {
        if (TryGetType(enumTypeName, out Type type) && type.IsEnum) {
            foreach (object enumValue in Enum.GetValues(type)) {
                if (value.Equals(enumValue.ToString(), StringComparison.InvariantCultureIgnoreCase)) {
                    return enumValue;
                }
            }
        }

        return null;
    }
}