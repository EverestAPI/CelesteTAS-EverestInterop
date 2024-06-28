using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste;
using Monocle;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.EverestInterop.Lua;

public static class LuaHelpers {
    public class ValueHolder<T> {
        public T Value;

        public ValueHolder(T value) {
            Value = value;
        }
    }

    public static readonly object NullValue = new ValueHolder<object>(null);

    // can omit entityId
    public static Entity GetEntity(string typeNameWithId) {
        if (TryGetEntityTypeWithId(typeNameWithId, out Type type, out string entityId)) {
            return InfoCustom.FindEntities(type, entityId).FirstOrDefault();
        } else {
            return null;
        }
    }

    public static List<Entity> GetEntities(string typeNameWithId) {
        if (TryGetEntityTypeWithId(typeNameWithId, out Type type, out string entityId)) {
            return InfoCustom.FindEntities(type, entityId);
        } else {
            return new List<Entity>();
        }
    }

    // e.g. entityTypeName can be "Player" or "Celeste.Player"
    private static bool TryGetEntityTypeWithId(string entityTypeName, out Type type, out string entityId) {
        if (InfoCustom.TryParseTypes(entityTypeName, out List<Type> types, out string id)) {
            type = types.FirstOrDefault(t => t.IsSameOrSubclassOf(typeof(Entity)));
            entityId = id;
            return type != null;
        } else {
            type = null;
            entityId = "";
            return false;
        }
    }

    private static bool TryGetType(string typeName, out Type type) {
        return InfoCustom.TryParseType(typeName, out type, out _, out _);
    }

    // Get field or property value
    public static object GetValue(object instanceOrTypeName, string memberName) {
        if (!TryGetTypeFromInstanceOrTypeName(instanceOrTypeName, out Type type, out bool staticMember)) {
            return null;
        }

        object obj = staticMember ? null : instanceOrTypeName;
        MemberInfo memberInfo = type.GetMemberInfo(memberName);
        if (memberInfo != null) {
            try {
                if (memberInfo is FieldInfo fieldInfo) {
                    return fieldInfo.GetValue(obj);
                } else if (memberInfo is PropertyInfo propertyInfo) {
                    return propertyInfo.GetValue(obj);
                }
            } catch (Exception e) {
                EvalLuaCommand.Log(e);
            }
        }

        return null;
    }

    // Set field or property value
    public static void SetValue(object instanceOrTypeName, string memberName, object value) {
        if (!TryGetTypeFromInstanceOrTypeName(instanceOrTypeName, out Type type, out bool staticMember)) {
            return;
        }

        object obj = staticMember ? null : instanceOrTypeName;
        MemberInfo memberInfo = type.GetMemberInfo(memberName);
        if (memberInfo != null) {
            try {
                if (memberInfo is FieldInfo fieldInfo) {
                    value = ConvertType(value, type, fieldInfo.FieldType);
                    fieldInfo.SetValue(obj, value);
                } else if (memberInfo is PropertyInfo propertyInfo) {
                    value = ConvertType(value, type, propertyInfo.PropertyType);
                    propertyInfo.SetValue(obj, value);
                }
            } catch (Exception e) {
                EvalLuaCommand.Log(e);
            }
        }
    }

    public static object InvokeMethod(object instanceOrTypeName, string methodName, params object[] parameters) {
        if (!TryGetTypeFromInstanceOrTypeName(instanceOrTypeName, out Type type, out bool staticMethod)) {
            return null;
        }

        // TODO Overloaded methods are not supported
        object obj = staticMethod ? null : instanceOrTypeName;
        MethodInfo methodInfo = type.GetMethodInfo(methodName);
        if (methodInfo != null) {
            ParameterInfo[] parameterInfos = methodInfo.GetParameters();
            for (int i = 0; i < parameterInfos.Length; i++) {
                ParameterInfo parameterInfo = parameterInfos[i];
                if (i < parameters.Length) {
                    parameters[i] = ConvertType(parameters[i], parameters[i]?.GetType(), parameterInfo.ParameterType);
                } else if (parameterInfo.HasDefaultValue) {
                    Array.Resize(ref parameters, parameters.Length + 1);
                    parameters[i] = parameterInfo.DefaultValue;
                }
            }

            try {
                return methodInfo.Invoke(obj, parameters);
            } catch (Exception e) {
                EvalLuaCommand.Log(e);
            }
        } else {
            $"Method '{methodName}' can't be found in type '{type.FullName}'".Log(true);
        }

        return null;
    }

    public static ValueHolder<float> ToFloat(double value) {
        return new ValueHolder<float>((float) value);
    }

    private static object ConvertType(object value, Type valueType, Type type) {
        if (value is ValueHolder<float> floatHolder) {
            return floatHolder.Value;
        } else if (value is ValueHolder<object> objectHolder) {
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

    private static bool TryGetTypeFromInstanceOrTypeName(object instanceOrTypeName, out Type type, out bool staticMember) {
        type = null;
        staticMember = false;
        if (instanceOrTypeName is string typeName && TryGetType(typeName, out type)) {
            staticMember = true;
        } else if (instanceOrTypeName != null) {
            type = instanceOrTypeName.GetType();
        }

        return type != null;
    }

    public static object GetEnum(string enumTypeName, object value) {
        if (TryGetType(enumTypeName, out Type type) && type.IsEnum) {
            if (value is long longValue || long.TryParse(value.ToString(), out longValue)) {
                return Enum.ToObject(type, longValue);
            } else {
                try {
                    return Enum.Parse(type, value.ToString(), true);
                } catch {
                    return null;
                }
            }
        }

        return null;
    }

    public static Level GetLevel() {
        return Engine.Scene.GetLevel();
    }

    public static Session GetSession() {
        return Engine.Scene.GetSession();
    }
}