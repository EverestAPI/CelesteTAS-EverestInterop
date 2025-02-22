using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using System.Diagnostics.CodeAnalysis;
using TAS.InfoHUD;
using TAS.Input.Commands;
using TAS.Utils;

namespace TAS.Lua;

/// Lua environment with TAS-specific helper methods
internal class LuaHelperEnvironment(NeoLua.Lua lua) : NeoLua.LuaGlobal(lua) {

    /// Logs a message
    [NeoLua.LuaMember("log"), UsedImplicitly]
    private static void Log(object? message, string? tag) {
        Logger.Info(LuaToString(message), tag ?? "CelesteTAS/Lua");
    }

    /// Resolves the first entity which matches the specified target-query, e.g. "Player" or "Celeste.Player"
    /// Example: getEntity("Player"), getEntity("Celeste.Player"), getEntity("DustStaticSpinner[s1:12]")
    [NeoLua.LuaMember("getEntity"), UsedImplicitly]
    public static Entity? GetEntity(string targetQuery) {
        if (TryGetEntityTypeWithId(targetQuery, out var type, out var entityId)) {
            return (Entity?) TargetQuery.ResolveTypeInstances(type, [], entityId)!.FirstOrDefault();
        }

        return null;
    }

    /// Resolves all entities which match the specified target-query, e.g. "Player" or "Celeste.Player"
    /// Example: getEntities("Player"), getEntities("Celeste.Player"), getEntities("CustomSpinner@VivHelper")
    [NeoLua.LuaMember("getEntities"), UsedImplicitly]
    public static List<Entity> GetEntities(string targetQuery) {
        if (TryGetEntityTypeWithId(targetQuery, out var type, out var entityId)) {
            return TargetQuery.ResolveTypeInstances(type, [], entityId)!.Cast<Entity>().ToList();
        }

        return [];
    }

    /// Gets the value of a (private) field / property
    [NeoLua.LuaMember("getValue"), UsedImplicitly]
    public static object? GetValue(object? instanceOrTargetQuery, string memberName) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
            return null;
        }

        try {
            if (type.GetFieldInfo(memberName) is { } fieldInfo) {
                if (fieldInfo.IsStatic) {
                    return fieldInfo.GetValue(null);
                } else {
                    return fieldInfo.GetValue(instance);
                }
            }
            if (type.GetPropertyInfo(memberName) is { } propertyInfo && propertyInfo.GetMethod != null) {
                if (propertyInfo.IsStatic()) {
                    return propertyInfo.GetValue(null);
                } else {
                    return propertyInfo.GetValue(instance);
                }
            }
        } catch (Exception e) {
            e.LogException($"Failed getting member '{memberName}' on type '{type}'", "Lua", EvalLuaCommand.LogToConsole);
            return null;
        }

        $"Failed finding member '{memberName}' on type '{type}'".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
        return null;
    }

    /// Sets the value of a (private) field / property
    [NeoLua.LuaMember("setValue"), UsedImplicitly]
    public static void SetValue(object? instanceOrTargetQuery, string memberName, object? value) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
            return;
        }

        try {
            if (type.GetFieldInfo(memberName) is { } fieldInfo) {
                value = ConvertType(value, type, fieldInfo.FieldType);
                if (fieldInfo.IsStatic) {
                    fieldInfo.SetValue(null, value);
                } else {
                    fieldInfo.SetValue(instance, value);
                }
            }
            if (type.GetPropertyInfo(memberName) is { } propertyInfo && propertyInfo.SetMethod != null) {
                value = ConvertType(value, type, propertyInfo.PropertyType);
                if (propertyInfo.IsStatic()) {
                    propertyInfo.SetValue(null, value);
                } else {
                    propertyInfo.SetValue(instance, value);
                }
            }
        } catch (Exception e) {
            e.LogException($"Failed setting member '{memberName}' on type '{type}'", "Lua", EvalLuaCommand.LogToConsole);
            return;
        }

        $"Failed finding member '{memberName}' on type '{type}'".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
    }

    /// Invokes a (private) method
    [NeoLua.LuaMember("invokeMethod"), UsedImplicitly]
    public static object? InvokeMethod(object? instanceOrTargetQuery, string methodName, params object?[] parameters) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
            return null;
        }
        if (instance == null) {
            $"Cannot get value of member '{methodName}' for null-instance".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
            return null;
        }
        if (type.GetMethodInfo(methodName) is { } methodInfo) {
            var parameterInfos = methodInfo.GetParameters();
            for (int i = 0; i < parameterInfos.Length; i++) {
                var parameterInfo = parameterInfos[i];
                if (i < parameters.Length) {
                    parameters[i] = ConvertType(parameters[i], parameters[i]?.GetType(), parameterInfo.ParameterType);
                } else if (parameterInfo.HasDefaultValue) {
                    Array.Resize(ref parameters, parameters.Length + 1);
                    parameters[i] = parameterInfo.DefaultValue;
                }
            }

            try {
                return methodInfo.Invoke(instance, parameters);
            } catch (Exception e) {
                e.LogException($"Failed invoking method '{methodName}' on type '{type}'", "Lua", EvalLuaCommand.LogToConsole);
                return null;
            }
        }

        $"Failed finding method '{methodName}' on type '{type}'".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
        return null;
    }

    /// Resolves the enum value for an ordinal or name
    [NeoLua.LuaMember("getEnum"), UsedImplicitly]
    public static object? GetEnum(string enumTargetQuery, object value) {
        if (TargetQuery.ResolveBaseTypes(enumTargetQuery.Split('.'), out _, out _, out _) is { } types && types.IsNotEmpty() &&
            types.FirstOrDefault(t => t.IsEnum) is { } type) {
            if (value is long longValue || long.TryParse(value.ToString(), out longValue)) {
                return Enum.ToObject(type, longValue);
            }

            try {
                return Enum.Parse(type, value.ToString() ?? string.Empty, true);
            } catch {
                $"Failed finding enum-value '{value}' on type '{type}'".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
                return null;
            }
        }

        $"Failed finding enum '{enumTargetQuery}'".Log("Lua", EvalLuaCommand.LogToConsole, LogLevel.Error);
        return null;
    }

    /// Provides the current scene
    [NeoLua.LuaMember("scene"), UsedImplicitly]
    public static Scene Scene => Engine.Scene;

    /// Provides the current level
    [NeoLua.LuaMember("level"), UsedImplicitly]
    public static Level Level => Engine.Scene.GetLevel();

    /// Provides the current session
    [NeoLua.LuaMember("session"), UsedImplicitly]
    public static Session Session => Engine.Scene.GetSession();

    /// Provides the current player
    [NeoLua.LuaMember("player"), UsedImplicitly]
    public static Player Player => Engine.Scene.GetPlayer();

    #region Legacy

    [Obsolete("Use level instead"), NeoLua.LuaMember("getLevel"), UsedImplicitly]
    public static Level GetLevel() => Engine.Scene.GetLevel();

    [Obsolete("Use session instead"), NeoLua.LuaMember("getSession"), UsedImplicitly]
    public static Session GetSession() => Engine.Scene.GetSession();

    [Obsolete("Use cast(int, value) instead"), NeoLua.LuaMember("toInt"), UsedImplicitly]
    public static int ToInt(long value) => (int) value;
    [Obsolete("Use cast(float, value) instead"), NeoLua.LuaMember("toFloat"), UsedImplicitly]
    public static float ToFloat(double value) => (float) value;

    #endregion

    private static bool TryGetInstance(object? instanceOrTargetQuery, [NotNullWhen(true)] out Type? type, out object? instance) {
        if (instanceOrTargetQuery is string targetQuery) {
            if (TargetQuery.ResolveBaseTypes(targetQuery.Split('.'), out _, out _, out _) is { } types && types.IsNotEmpty()) {
                type = types[0];
                instance = TargetQuery.ResolveTypeInstances(types[0], [], EntityID.None)?.FirstOrDefault();
                return true;
            } else {
                type = null;
                instance = null;
                return false;
            }
        } else {
            type = instanceOrTargetQuery?.GetType()!;
            instance = instanceOrTargetQuery;
            return true;
        }
    }

    private static bool TryGetEntityTypeWithId(string targetQuery, [NotNullWhen(true)] out Type? type, out EntityID? entityId) {
        if (TargetQuery.ResolveBaseTypes(targetQuery.Split('.'), out _, out _, out var id) is { } types && types.IsNotEmpty()) {
            type = types.FirstOrDefault(t => t.IsSameOrSubclassOf(typeof(Entity)));
            entityId = id;
            return type != null;
        } else {
            type = null;
            entityId = EntityID.None;
            return false;
        }
    }

    /// Tries to convert the value to the target type
    private static object? ConvertType(object? value, Type? valueType, Type type) {
        if (valueType != null && type.IsSameOrSubclassOf(valueType)) {
            return value;
        }

        try {
            if (value is null) {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            } else {
                return type.IsEnum ? Enum.Parse(type, (string) value, true) : Convert.ChangeType(value, type);
            }
        } catch {
            return value;
        }
    }
}
