using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Celeste.Mod;
using JetBrains.Annotations;
using Monocle;
using System.Diagnostics.CodeAnalysis;
using TAS.EverestInterop.InfoHUD;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.EverestInterop.Lua;

/// Provides helper methods for usage in Lua scripts
public static class LuaHelpers {
    public class ValueHolder<T>(T value) {
        public T Value = value;
    }

    /// Used to pass a null value from Lua
    [UsedImplicitly]
    public static readonly object? NullValue = new ValueHolder<object?>(null);

    /// Gets all members matching the specified target-query
    [UsedImplicitly]
    public static object? Get(string? query) {
        if (query == null) {
            "No target-query specified".ConsoleLog(LogLevel.Error);
            return null;
        }

        var result = TargetQuery.GetMemberValues(query);
        if (result.Failure) {
            result.Error.ConsoleLog(LogLevel.Error);
            return null;
        }

        return result.Value.Select(entry => entry.Value).ToArray();
    }
    /// Sets all members matching the specified target-query to the value
    [UsedImplicitly]
    public static void Set(string? query, params string[]? arguments) {
        if (query == null) {
            "No target-query specified".ConsoleLog(LogLevel.Error);
            return;
        }
        if (arguments == null) {
            "No arguments specified".ConsoleLog(LogLevel.Error);
            return;
        }

        var result = TargetQuery.SetMemberValues(query, arguments);
        if (result.Failure) {
            result.Error.ConsoleLog(LogLevel.Error);
        }
    }
    /// Invokes all members matching the specified target-query with the arguments
    [UsedImplicitly]
    public static void Invoke(string? query, params string[]? arguments) {
        if (query == null) {
            "No target-query specified".ConsoleLog(LogLevel.Error);
            return;
        }
        if (arguments == null) {
            "No value specified".ConsoleLog(LogLevel.Error);
            return;
        }

        var result = TargetQuery.InvokeMemberMethods(query, arguments);
        if (result.Failure) {
            result.Error.ConsoleLog(LogLevel.Error);
        }
    }

    /// Resolves the first entity which matches the specified target-query, e.g. "Player" or "Celeste.Player"
    [UsedImplicitly]
    public static Entity? GetEntity(string targetQuery) {
        var result = TargetQuery.GetMemberValues(targetQuery);
        if (result.Failure) {
            result.Error.ToString().ConsoleLog(LogLevel.Warn);
            return null;
        }

        return (Entity?) result.Value
            .Select(entry => entry.Value)
            .FirstOrDefault(value => value is Entity);
    }

    /// Resolves all entities which match the specified target-query, e.g. "Player" or "Celeste.Player"
    [UsedImplicitly]
    public static List<Entity>? GetEntities(string targetQuery) {
        var result = TargetQuery.GetMemberValues(targetQuery);
        if (result.Failure) {
            result.Error.ToString().ConsoleLog(LogLevel.Warn);
            return null;
        }

        return result.Value
            .Where(entry => entry.Value is Entity)
            .Select(entry => (Entity) entry.Value!)
            .ToList();
    }

    /// Gets the value of a (private) field / property
    [UsedImplicitly]
    public static object? GetValue(object? instanceOrTargetQuery, string memberName) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
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
            e.LogException($"Failed getting member '{memberName}' on type '{type}'", "Lua", EvalLuaCommand.ConsoleCommandRunning);
            return null;
        }

        $"Failed finding member '{memberName}' on type '{type}'".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
        return null;
    }

    /// Sets the value of a (private) field / property
    [UsedImplicitly]
    public static void SetValue(object? instanceOrTargetQuery, string memberName, object? value) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
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
            e.LogException($"Failed setting member '{memberName}' on type '{type}'", "Lua", EvalLuaCommand.ConsoleCommandRunning);
            return;
        }

        $"Failed finding member '{memberName}' on type '{type}'".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
    }

    /// Invokes a (private) method
    [UsedImplicitly]
    public static object? InvokeMethod(object? instanceOrTargetQuery, string methodName, params object?[] parameters) {
        if (!TryGetInstance(instanceOrTargetQuery, out var type, out object? instance)) {
            $"Failed to get instance for '{instance}".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
            return null;
        }
        if (instance == null) {
            $"Cannot get value of member '{methodName}' for null-instance".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
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
                e.LogException($"Failed invoking method '{methodName}' on type '{type}'", "Lua", EvalLuaCommand.ConsoleCommandRunning);
                return null;
            }
        }

        $"Failed finding method '{methodName}' on type '{type}'".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
        return null;
    }

    /// Resolves the enum value for an ordinal or name
    [UsedImplicitly]
    public static object? GetEnum(string enumTargetQuery, object value) {
        if (TargetQuery.ResolveBaseTypes(enumTargetQuery.Split('.'), out _) is { } types && types.IsNotEmpty() &&
            types.FirstOrDefault(t => t.IsEnum) is { } type)
        {
            if (value is long longValue || long.TryParse(value.ToString(), out longValue)) {
                return Enum.ToObject(type, longValue);
            } else {
                try {
                    return Enum.Parse(type, value.ToString() ?? string.Empty, true);
                } catch {
                    $"Failed finding enum-value '{value}' on type '{type}'".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
                    return null;
                }
            }
        }

        $"Failed finding enum '{enumTargetQuery}'".Log("Lua", EvalLuaCommand.ConsoleCommandRunning, LogLevel.Error);
        return null;
    }

    /// Returns the current level
    [UsedImplicitly]
    public static Level? GetLevel() {
        return Engine.Scene.GetLevel();
    }

    /// Returns the current session
    [UsedImplicitly]
    public static Session? GetSession() {
        return Engine.Scene.GetSession();
    }

    /// Casts the value to an int, for usage with setValue / invokeMethod
    [UsedImplicitly]
    public static ValueHolder<int> ToInt(long value) {
        return new ValueHolder<int>((int) value);
    }
    /// Casts the value to a float, for usage with setValue / invokeMethod
    [UsedImplicitly]
    public static ValueHolder<float> ToFloat(double value) {
        return new ValueHolder<float>((float) value);
    }

    private static bool TryGetInstance(object? instanceOrTargetQuery, [NotNullWhen(true)] out Type? type, out object? instance) {
        if (instanceOrTargetQuery is string targetQuery) {
            if (TargetQuery.ResolveBaseTypes(targetQuery.Split('.'), out _) is { } types && types.IsNotEmpty()) {
                type = types.First();
                instance = TargetQuery.ResolveTypeInstances(type).FirstOrDefault();
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

    /// Tries to convert the value to the target type
    private static object? ConvertType(object? value, Type? valueType, Type type) {
        switch (value) {
            case ValueHolder<int> intHolder:
                return intHolder.Value;
            case ValueHolder<float> floatHolder:
                return floatHolder.Value;
            case ValueHolder<object> objectHolder:
                return objectHolder.Value;
        }

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
