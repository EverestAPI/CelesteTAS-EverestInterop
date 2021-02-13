using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

namespace TAS.EverestInterop {
internal static class ReflectionExtensions {
    public delegate object GetField(object o);

    public delegate object GetStaticField();

    private const BindingFlags StaticInstanceAnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static FieldInfo GetFieldInfo(this Type type, string name) {
        return type.GetField(name, StaticInstanceAnyVisibility);
    }

    public static PropertyInfo GetPropertyInfo(this Type type, string name) {
        return type.GetProperty(name, StaticInstanceAnyVisibility);
    }

    public static MethodInfo GetMethodInfo(this Type type, string name) {
        return type.GetMethod(name, StaticInstanceAnyVisibility);
    }

    public static T CreateDelegate_Get<T>(this FieldInfo field) where T : Delegate {
        bool isStatic = field.IsStatic;
        Type[] param;
        if (!isStatic) {
            param = new Type[] {field.DeclaringType};
        } else {
            param = new Type[0];
        }

        DynamicMethod dyn = new DynamicMethod(field.Name + "_FastAccess", field.FieldType, param, field.DeclaringType);
        ILGenerator ilGen = dyn.GetILGenerator();
        if (!isStatic) {
            ilGen.Emit(OpCodes.Ldarg_0);
        }

        ilGen.Emit(OpCodes.Ldfld, field);
        ilGen.Emit(OpCodes.Ret);
        return dyn.CreateDelegate(typeof(T)) as T;
    }

    public static GetField CreateDelegate_GetInstance(this FieldInfo field) {
        DynamicMethod dyn = new DynamicMethod(field.Name + "_FastAccess", typeof(object), new Type[] {typeof(object)}, field.DeclaringType);
        ILGenerator ilGen = dyn.GetILGenerator();
        ilGen.Emit(OpCodes.Ldarg_0);
        ilGen.Emit(OpCodes.Castclass, field.DeclaringType);
        ilGen.Emit(OpCodes.Ldfld, field);
        ilGen.Emit(OpCodes.Ret);
        return dyn.CreateDelegate(typeof(GetField)) as GetField;
    }

    public static GetStaticField CreateDelegate_GetStatic(this FieldInfo field) {
        DynamicMethod dyn = new DynamicMethod(field.Name + "_FastAccess", typeof(object), new Type[0]);
        ILGenerator ilGen = dyn.GetILGenerator();
        ilGen.Emit(OpCodes.Ldfld, field);
        ilGen.Emit(OpCodes.Ret);
        return dyn.CreateDelegate(typeof(GetStaticField)) as GetStaticField;
    }

    public static IEnumerable<FieldInfo> GetFieldInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool filterBackingField = false) {
        IEnumerable<FieldInfo> fieldInfos = type.GetFields(bindingFlags);
        if (filterBackingField) {
            fieldInfos = fieldInfos.Where(info => !info.Name.EndsWith("k__BackingField"));
        }

        return fieldInfos;
    }

    public static IEnumerable<FieldInfo> GetAllFieldInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool filterBackingField = false) {
        List<FieldInfo> result = new List<FieldInfo>();
        while (type != null && type.IsSubclassOf(typeof(object))) {
            IEnumerable<FieldInfo> fieldInfos = type.GetFieldInfos(bindingFlags, filterBackingField);
            foreach (FieldInfo fieldInfo in fieldInfos) {
                if (result.Contains(fieldInfo)) {
                    continue;
                }

                result.Add(fieldInfo);
            }

            type = type.BaseType;
        }

        return result;
    }

    public static IEnumerable<PropertyInfo> GetAllProperties(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        List<PropertyInfo> result = new List<PropertyInfo>();
        while (type != null && type.IsSubclassOf(typeof(object))) {
            IEnumerable<PropertyInfo> properties = type.GetProperties(bindingFlags);
            foreach (PropertyInfo fieldInfo in properties) {
                if (result.Contains(fieldInfo)) {
                    continue;
                }

                result.Add(fieldInfo);
            }

            type = type.BaseType;
        }

        return result;
    }
}

internal static class CommonExtensions {
    public static T Apply<T>(this T obj, Action<T> action) {
        action(obj);
        return obj;
    }
}

// source from: https://stackoverflow.com/a/17264480
internal static class ExtendedDataExtensions {
    private static readonly ConditionalWeakTable<object, object> ExtendedData =
        new ConditionalWeakTable<object, object>();

    private static IDictionary<string, object> CreateDictionary(object o) {
        return new Dictionary<string, object>();
    }

    public static void SetExtendedDataValue(this object o, string name, object value) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Invalid name");
        }

        name = name.Trim();

        IDictionary<string, object> values =
            (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

        if (value != null) {
            values[name] = value;
        } else {
            values.Remove(name);
        }
    }

    public static T GetExtendedDataValue<T>(this object o, string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Invalid name");
        }

        name = name.Trim();

        IDictionary<string, object> values =
            (IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

        if (values.ContainsKey(name)) {
            return (T) values[name];
        }

        return default;
    }
}

internal static class DynDataExtensions {
    private const string DynDataInstanceKey = nameof(DynDataInstanceKey);

    public static DynData<T> GetDynDataInstance<T>(this T target) where T : class {
        object obj = target;
        if (target == null) {
            obj = typeof(T);
        }

        DynData<T> dynData = obj.GetExtendedDataValue<DynData<T>>(DynDataInstanceKey);
        if (dynData == null) {
            dynData = new DynData<T>(target);
            obj.SetExtendedDataValue(DynDataInstanceKey, dynData);
        }

        return dynData;
    }
}

internal static class EntityExtensions {
    private const string ActualCollidePositionKey = nameof(ActualCollidePositionKey);
    private const string ActualCollidableKey = nameof(ActualCollidableKey);

    public static void SaveActualCollidePosition(this Entity entity) {
        entity.SetExtendedDataValue(ActualCollidePositionKey, entity.Position);
    }

    public static Vector2? LoadActualCollidePosition(this Entity entity) {
        return entity.GetExtendedDataValue<Vector2?>(ActualCollidePositionKey);
    }

    public static void ClearActualCollidePosition(this Entity entity) {
        entity.SetExtendedDataValue(ActualCollidePositionKey, null);
    }

    public static void SaveActualCollidable(this Entity entity) {
        entity.SetExtendedDataValue(ActualCollidableKey, entity.Collidable);
    }

    public static bool LoadActualCollidable(this Entity entity) {
        return entity.GetExtendedDataValue<bool>(ActualCollidableKey);
    }
}

internal static class SceneExtensions {
    public static Player GetPlayer(this Scene scene) => scene.Tracker.GetEntity<Player>();
}

internal static class CloneUtil<T> {
    private static readonly Func<T, object> clone;

    static CloneUtil() {
        MethodInfo cloneMethod = typeof(T).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        clone = (Func<T, object>) cloneMethod.CreateDelegate(typeof(Func<T, object>));
    }

    public static T ShallowClone(T obj) => (T) clone(obj);
}

internal static class CloneUtil {
    private const BindingFlags InstanceAnyVisibilityDeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    public static T ShallowClone<T>(this T obj) => CloneUtil<T>.ShallowClone(obj);

    public static void CopyAllFields(this object to, object from) {
        if (to.GetType() != from.GetType()) throw new ArgumentException("object to and from must be the same type");

        foreach (FieldInfo fieldInfo in to.GetType().GetAllFieldInfos(InstanceAnyVisibilityDeclaredOnly)) {
            object fromValue = fieldInfo.GetValue(from);
            fieldInfo.SetValue(to, fromValue);
        }
    }

    public static void CopyAllProperties(this object to, object from) {
        if (to.GetType() != from.GetType()) throw new ArgumentException("object to and from must be the same type");

        foreach (PropertyInfo propertyInfo in to.GetType().GetAllProperties(InstanceAnyVisibilityDeclaredOnly)) {
            if (propertyInfo.GetGetMethod(true) == null || propertyInfo.GetSetMethod(true) == null) {
                continue;
            }
            object fromValue = propertyInfo.GetValue(from);
            propertyInfo.SetValue(to, fromValue);
        }
    }
}
}