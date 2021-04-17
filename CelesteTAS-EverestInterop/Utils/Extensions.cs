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

namespace TAS.Utils {
    internal static class ReflectionExtensions {
        public delegate object GetField(object o);

        public delegate object GetStaticField();

        private const BindingFlags StaticInstanceAnyVisibility =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool IsSameOrSubclassOf(this Type potentialDescendant, Type potentialBase) {
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialDescendant == potentialBase;
        }

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

            DynamicMethod dyn = new(field.Name + "_FastAccess", field.FieldType, param, field.DeclaringType);
            ILGenerator ilGen = dyn.GetILGenerator();
            if (!isStatic) {
                ilGen.Emit(OpCodes.Ldarg_0);
            }

            ilGen.Emit(OpCodes.Ldfld, field);
            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(T)) as T;
        }

        public static GetField CreateDelegate_GetInstance(this FieldInfo field) {
            DynamicMethod dyn = new(field.Name + "_FastAccess", typeof(object), new Type[] {typeof(object)}, field.DeclaringType);
            ILGenerator ilGen = dyn.GetILGenerator();
            ilGen.Emit(OpCodes.Ldarg_0);
            ilGen.Emit(OpCodes.Castclass, field.DeclaringType);
            ilGen.Emit(OpCodes.Ldfld, field);
            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(GetField)) as GetField;
        }

        public static GetStaticField CreateDelegate_GetStatic(this FieldInfo field) {
            DynamicMethod dyn = new(field.Name + "_FastAccess", typeof(object), new Type[0]);
            ILGenerator ilGen = dyn.GetILGenerator();
            ilGen.Emit(OpCodes.Ldfld, field);
            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(GetStaticField)) as GetStaticField;
        }

        public static IEnumerable<FieldInfo> GetFieldInfos(this Type type, BindingFlags bindingFlags = StaticInstanceAnyVisibility,
            bool filterBackingField = false) {
            IEnumerable<FieldInfo> fieldInfos = type.GetFields(bindingFlags);
            if (filterBackingField) {
                fieldInfos = fieldInfos.Where(info => !info.Name.EndsWith("k__BackingField"));
            }

            return fieldInfos;
        }
    }

    internal static class CommonExtensions {
        public static T Apply<T>(this T obj, Action<T> action) {
            action(obj);
            return obj;
        }
    }

    internal static class StringExtensions {
        public static bool IsNullOrEmpty(this string text) {
            return string.IsNullOrEmpty(text);
        }

        public static bool IsNotNullOrEmpty(this string text) {
            return !string.IsNullOrEmpty(text);
        }

        public static bool IsNullOrWhiteSpace(this string text) {
            return string.IsNullOrWhiteSpace(text);
        }

        public static bool IsNotNullOrWhiteSpace(this string text) {
            return !string.IsNullOrWhiteSpace(text);
        }
    }

    internal static class EnumerableExtensions {
        public static bool IsEmpty<T>(this IEnumerable<T> enumerable) {
            return !enumerable.Any();
        }

        public static bool IsNotEmpty<T>(this IEnumerable<T> enumerable) {
            return !enumerable.IsEmpty();
        }

        public static IEnumerable<T> SkipLast<T>(this IEnumerable<T> source, int n = 1) {
            var it = source.GetEnumerator();
            bool hasRemainingItems = false;
            var cache = new Queue<T>(n + 1);

            do {
                if (hasRemainingItems = it.MoveNext()) {
                    cache.Enqueue(it.Current);
                    if (cache.Count > n)
                        yield return cache.Dequeue();
                }
            } while (hasRemainingItems);
        }
    }

    internal static class ListExtensions {
        public static T GetValueOrDefault<T>(this IList<T> list, int index, T defaultValue = default) {
            return index > 0 && index < list.Count ? list[index] : defaultValue;
        }
    }

    internal static class DictionaryExtensions {
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default) {
            return dict.TryGetValue(key, out TValue value) ? value : defaultValue;
        }

        public static TKey LastKeyOrDefault<TKey, TValue>(this SortedDictionary<TKey, TValue> dict) {
            return dict.Count > 0 ? dict.Last().Key : default;
        }

        public static TValue LastValueOrDefault<TKey, TValue>(this SortedDictionary<TKey, TValue> dict) {
            return dict.Count > 0 ? dict.Last().Value : default;
        }
    }

// source from: https://stackoverflow.com/a/17264480
    internal static class ExtendedDataExtensions {
        private static readonly ConditionalWeakTable<object, object> ExtendedData =
            new();

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
        public static DynData<T> GetDynDataInstance<T>(this T target) where T : class {
            object obj = target;
            if (target == null) {
                obj = typeof(T);
            }

            string dynDataInstanceKey = $"{typeof(T).FullName}_DynDataInstanceKey";
            DynData<T> dynData = obj.GetExtendedDataValue<DynData<T>>(dynDataInstanceKey);
            if (dynData == null) {
                dynData = new DynData<T>(target);
                obj.SetExtendedDataValue(dynDataInstanceKey, dynData);
            }

            return dynData;
        }
    }

    internal static class EntityExtensions {
        private const string CelesteTasEntityDataKey = nameof(CelesteTasEntityDataKey);

        public static void SaveEntityData(this Entity entity, EntityData data) {
            entity.GetDynDataInstance().Set(CelesteTasEntityDataKey, data);
        }

        public static EntityData LoadEntityData(this Entity entity) {
            return entity.GetDynDataInstance().Get<EntityData>(CelesteTasEntityDataKey);
        }

        public static EntityID ToEntityId(this EntityData entityData) {
            return new(entityData.Level.Name, entityData.ID);
        }

        public static string ToUniqueId(this EntityData entityData) {
            return $"{entityData.Name}:{entityData.Level.Name}:{entityData.ID}";
        }
    }

    internal static class Vector2Extensions {
        public static string ToSimpleString(this Vector2 vector2, bool round) {
            return $"{vector2.X.ToString(round ? "F2" : "F12")}, {vector2.Y.ToString(round ? "F2" : "F12")}";
        }
    }

    internal static class SceneExtensions {
        public static Player GetPlayer(this Scene scene) => scene.Tracker.GetEntity<Player>();
    }

    internal static class CloneUtil<T> {
        private static readonly Func<T, object> Clone;

        static CloneUtil() {
            MethodInfo cloneMethod = typeof(T).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            Clone = (Func<T, object>) cloneMethod.CreateDelegate(typeof(Func<T, object>));
        }

        public static T ShallowClone(T obj) => (T) Clone(obj);
    }

    internal static class CloneUtil {
        private const BindingFlags InstanceAnyVisibilityDeclaredOnly =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public static T ShallowClone<T>(this T obj) => CloneUtil<T>.ShallowClone(obj);

        public static void CopyAllFields(this object to, object from) {
            if (to.GetType() != from.GetType()) {
                throw new ArgumentException("object to and from must be the same type");
            }

            foreach (FieldInfo fieldInfo in GetAllFieldInfos(to.GetType())) {
                object fromValue = fieldInfo.GetValue(from);
                fieldInfo.SetValue(to, fromValue);
            }
        }

        public static void CopyAllProperties(this object to, object from) {
            if (to.GetType() != from.GetType()) {
                throw new ArgumentException("object to and from must be the same type");
            }

            foreach (PropertyInfo propertyInfo in GetAllProperties(to.GetType())) {
                if (propertyInfo.GetGetMethod(true) == null || propertyInfo.GetSetMethod(true) == null) {
                    continue;
                }

                object fromValue = propertyInfo.GetValue(from);
                propertyInfo.SetValue(to, fromValue);
            }
        }

        private static IEnumerable<FieldInfo> GetAllFieldInfos(Type type, bool filterBackingField = false) {
            List<FieldInfo> result = new();
            while (type != null && type.IsSubclassOf(typeof(object))) {
                IEnumerable<FieldInfo> fieldInfos = type.GetFieldInfos(InstanceAnyVisibilityDeclaredOnly, filterBackingField);
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

        private static IEnumerable<PropertyInfo> GetAllProperties(Type type) {
            List<PropertyInfo> result = new();
            while (type != null && type.IsSubclassOf(typeof(object))) {
                IEnumerable<PropertyInfo> properties = type.GetProperties(InstanceAnyVisibilityDeclaredOnly);
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
}