using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

namespace TAS.Utils {
    internal static class ReflectionExtensions {
        private const BindingFlags StaticInstanceAnyVisibility =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private const BindingFlags InstanceAnyVisibilityDeclaredOnly =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> CachedFieldInfos = new();
        private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> CachedPropertyInfos = new();
        private static readonly Dictionary<Type, Dictionary<string, MethodInfo>> CachedMethodInfos = new();

        public static FieldInfo GetFieldInfo(this Type type, string name, bool includeSuperClassPrivate = false) {
            if (!CachedFieldInfos.ContainsKey(type)) {
                CachedFieldInfos[type] = new Dictionary<string, FieldInfo>();
            }

            if (!CachedFieldInfos[type].ContainsKey(name)) {
                FieldInfo result = type.GetField(name, StaticInstanceAnyVisibility);
                if (result == null && type.BaseType != null && includeSuperClassPrivate) {
                    result = type.BaseType.GetFieldInfo(name, true);
                }

                return CachedFieldInfos[type][name] = result;
            } else {
                return CachedFieldInfos[type][name];
            }
        }

        public static PropertyInfo GetPropertyInfo(this Type type, string name, bool includeSuperClassPrivate = false) {
            if (!CachedPropertyInfos.ContainsKey(type)) {
                CachedPropertyInfos[type] = new Dictionary<string, PropertyInfo>();
            }

            if (!CachedPropertyInfos[type].ContainsKey(name)) {
                PropertyInfo result = type.GetProperty(name, StaticInstanceAnyVisibility);
                if (result == null && type.BaseType != null && includeSuperClassPrivate) {
                    result = type.BaseType.GetPropertyInfo(name, true);
                }

                return CachedPropertyInfos[type][name] = result;
            } else {
                return CachedPropertyInfos[type][name];
            }
        }

        public static MethodInfo GetMethodInfo(this Type type, string name, bool includeSuperClassPrivate = false) {
            if (!CachedMethodInfos.ContainsKey(type)) {
                CachedMethodInfos[type] = new Dictionary<string, MethodInfo>();
            }

            if (!CachedMethodInfos[type].ContainsKey(name)) {
                MethodInfo result = type.GetMethod(name, StaticInstanceAnyVisibility);
                if (result == null && type.BaseType != null && includeSuperClassPrivate) {
                    result = type.BaseType.GetMethodInfo(name, true);
                }

                return CachedMethodInfos[type][name] = result;
            } else {
                return CachedMethodInfos[type][name];
            }
        }

        public static IEnumerable<FieldInfo> GetAllFieldInfos(this Type type, bool includeStatic = false, bool filterBackingField = false) {
            BindingFlags bindingFlags = InstanceAnyVisibilityDeclaredOnly;
            if (includeStatic) {
                bindingFlags |= BindingFlags.Static;
            }

            List<FieldInfo> result = new();
            while (type != null && type.IsSubclassOf(typeof(object))) {
                IEnumerable<FieldInfo> fieldInfos = type.GetFields(bindingFlags);
                if (filterBackingField) {
                    fieldInfos = fieldInfos.Where(info => !info.Name.EndsWith("k__BackingField"));
                }

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

        public static IEnumerable<PropertyInfo> GetAllProperties(this Type type, bool includeStatic = false) {
            BindingFlags bindingFlags = InstanceAnyVisibilityDeclaredOnly;
            if (includeStatic) {
                bindingFlags |= BindingFlags.Static;
            }

            List<PropertyInfo> result = new();
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

        public static T GetFieldValue<T>(this object obj, string name) {
            object result = obj.GetType().GetFieldInfo(name)?.GetValue(obj);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static T GetFieldValue<T>(this Type type, string name) {
            object result = type.GetFieldInfo(name)?.GetValue(null);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static void SetFieldValue(this object obj, string name, object value) {
            obj.GetType().GetFieldInfo(name)?.SetValue(obj, value);
        }

        public static void SetFieldValue(this Type type, string name, object value) {
            type.GetFieldInfo(name)?.SetValue(null, value);
        }

        public static T GetPropertyValue<T>(this object obj, string name) {
            object result = obj.GetType().GetPropertyInfo(name)?.GetValue(obj, null);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static T GetPropertyValue<T>(Type type, string name) {
            object result = type.GetPropertyInfo(name)?.GetValue(null, null);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static void SetPropertyValue(this object obj, string name, object value) {
            if (obj.GetType().GetPropertyInfo(name) is {CanWrite: true} propertyInfo) {
                propertyInfo.SetValue(obj, value, null);
            }
        }

        public static void SetPropertyValue(this Type type, string name, object value) {
            if (type.GetPropertyInfo(name) is {CanWrite: true} propertyInfo) {
                propertyInfo.SetValue(null, value, null);
            }
        }

        public static T InvokeMethod<T>(this object obj, string name, params object[] parameters) {
            object result = obj.GetType().GetMethodInfo(name)?.Invoke(obj, parameters);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static T InvokeMethod<T>(this Type type, string name, params object[] parameters) {
            object result = type.GetMethodInfo(name)?.Invoke(null, parameters);
            if (result == null) {
                return default;
            } else {
                return (T) result;
            }
        }

        public static void InvokeMethod(this object obj, string name, params object[] parameters) {
            obj.GetType().GetMethodInfo(name)?.Invoke(obj, parameters);
        }

        public static void InvokeMethod(this Type type, string name, params object[] parameters) {
            type.GetMethodInfo(name)?.Invoke(null, parameters);
        }

        public static bool IsSameOrSubclassOf(this Type potentialDescendant, Type potentialBase) {
            return potentialDescendant.IsSubclassOf(potentialBase) || potentialDescendant == potentialBase;
        }

        public static T CreateDelegate_Get<T>(this FieldInfo field) where T : Delegate {
            bool isStatic = field.IsStatic;
            Type[] param = isStatic ? Type.EmptyTypes : new[] {field.DeclaringType};

            DynamicMethod dyn = new($"{field.DeclaringType?.FullName}_{field.Name}_FastAccess", field.FieldType, param, field.DeclaringType);
            ILGenerator ilGen = dyn.GetILGenerator();
            if (isStatic) {
                ilGen.Emit(OpCodes.Ldsfld, field);
            } else {
                ilGen.Emit(OpCodes.Ldarg_0);
                ilGen.Emit(OpCodes.Ldfld, field);
            }

            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(T)) as T;
        }

        public static Func<T, TResult> CreateDelegate_Get<T, TResult>(this string fieldName) {
            FieldInfo field = typeof(T).GetFieldInfo(fieldName);
            return CreateDelegate_Get<Func<T, TResult>>(field);
        }

        public static Func<object, object> CreateDelegate_GetInstance(this FieldInfo field) {
            if (field.IsStatic) {
                throw new Exception("Not support static field.");
            }

            DynamicMethod dyn =
                new($"{field.DeclaringType?.FullName}_{field.Name}_FastAccess", typeof(object), new Type[] {typeof(object)}, field.DeclaringType);
            ILGenerator ilGen = dyn.GetILGenerator();
            ilGen.Emit(OpCodes.Ldarg_0);
            ilGen.Emit(OpCodes.Castclass, field.DeclaringType);
            ilGen.Emit(OpCodes.Ldfld, field);
            ilGen.Emit(field.FieldType.IsClass ? OpCodes.Castclass : OpCodes.Box, field.FieldType);
            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(Func<object, object>)) as Func<object, object>;
        }

        public static Func<object> CreateDelegate_GetStatic(this FieldInfo field) {
            if (!field.IsStatic) {
                throw new Exception("Not support non static field.");
            }

            DynamicMethod dyn = new($"{field.DeclaringType?.FullName}_{field.Name}_FastAccess", typeof(object), new Type[0]);
            ILGenerator ilGen = dyn.GetILGenerator();
            ilGen.Emit(OpCodes.Ldsfld, field);
            ilGen.Emit(field.FieldType.IsClass ? OpCodes.Castclass : OpCodes.Box, field.FieldType);
            ilGen.Emit(OpCodes.Ret);
            return dyn.CreateDelegate(typeof(Func<object>)) as Func<object>;
        }
    }

    internal static class CommonExtensions {
        public static T Apply<T>(this T obj, Action<T> action) {
            action(obj);
            return obj;
        }
    }

    internal static class StringExtensions {
        private static readonly Regex LineBreakRegex = new(@"\r\n?|\n", RegexOptions.Compiled);

        public static string ReplaceLineBreak(this string text, string replacement) {
            return LineBreakRegex.Replace(text, replacement);
        }

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

        public static bool IsNullOrEmpty<T>(this IEnumerable<T> enumerable) {
            return enumerable == null || !enumerable.Any();
        }

        public static bool IsNotEmpty<T>(this IEnumerable<T> enumerable) {
            return !enumerable.IsEmpty();
        }

        public static bool IsNotNullOrEmpty<T>(this IEnumerable<T> enumerable) {
            return !enumerable.IsNullOrEmpty();
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
            return index >= 0 && index < list.Count ? list[index] : defaultValue;
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

        public static void SetEntityData(this Entity entity, EntityData data) {
            entity.GetDynDataInstance().Set(CelesteTasEntityDataKey, data);
        }

        public static EntityData GetEntityData(this Entity entity) {
            return entity.GetDynDataInstance().Get<EntityData>(CelesteTasEntityDataKey);
        }

        public static EntityID ToEntityId(this EntityData entityData) {
            return new(entityData.Level.Name, entityData.ID);
        }

        public static float DistanceSquared(this Entity entity, Entity otherEntity) {
            return Vector2.DistanceSquared(entity.Center, otherEntity.Center);
        }
    }

    internal static class TrackerExtensions {
        public static List<T> GetCastComponents<T>(this Tracker tracker) where T : Component {
            return tracker.GetComponents<T>().Cast<T>().ToList();
        }
    }

    internal static class Vector2Extensions {
        public static string ToSimpleString(this Vector2 vector2, bool round) {
            string format = round ? "F2" : "F12";
            return $"{vector2.X.ToString(format)}, {vector2.Y.ToString(format)}";
        }
    }

    internal static class SceneExtensions {
        public static Player GetPlayer(this Scene scene) => scene.Tracker.GetEntity<Player>();
    }

    internal static class LevelExtensions {
        public static Vector2 ScreenToWorld(this Level level, Vector2 position) {
            Vector2 size = new Vector2(320f, 180f);
            Vector2 scaledSize = size / level.ZoomTarget;
            Vector2 offset = level.ZoomTarget != 1f ? (level.ZoomFocusPoint - scaledSize / 2f) / (size - scaledSize) * size : Vector2.Zero;
            float scale = level.Zoom * ((320f - level.ScreenPadding * 2f) / 320f);
            Vector2 paddingOffset = new Vector2(level.ScreenPadding, level.ScreenPadding * 9f / 16f);

            if (SaveData.Instance?.Assists.MirrorMode ?? false) {
                position.X = 1920f - position.X;
            }

            position /= 1920f / 320f;
            position -= paddingOffset;
            position = (position - offset) / scale + offset;
            position = level.Camera.ScreenToCamera(position);
            return position;
        }

        public static Vector2 WorldToScreen(this Level level, Vector2 position) {
            Vector2 size = new Vector2(320f, 180f);
            Vector2 scaledSize = size / level.ZoomTarget;
            Vector2 offset = level.ZoomTarget != 1f ? (level.ZoomFocusPoint - scaledSize / 2f) / (size - scaledSize) * size : Vector2.Zero;
            float scale = level.Zoom * ((320f - level.ScreenPadding * 2f) / 320f);
            Vector2 paddingOffset = new Vector2(level.ScreenPadding, level.ScreenPadding * 9f / 16f);

            position = level.Camera.CameraToScreen(position);
            position = (position - offset) * scale + offset;
            position += paddingOffset;
            position *= 1920f / 320f;
            if (SaveData.Instance?.Assists.MirrorMode ?? false) {
                position.X = 1920f - position.X;
            }

            return position;
        }
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
        public static T ShallowClone<T>(this T obj) => CloneUtil<T>.ShallowClone(obj);

        public static void CopyAllFields(this object to, object from, bool filterBackingField = false) {
            if (to.GetType() != from.GetType()) {
                throw new ArgumentException("object to and from must be the same type");
            }

            foreach (FieldInfo fieldInfo in to.GetType().GetAllFieldInfos(false, filterBackingField)) {
                object fromValue = fieldInfo.GetValue(from);
                fieldInfo.SetValue(to, fromValue);
            }
        }

        public static void CopyAllProperties(this object to, object from) {
            if (to.GetType() != from.GetType()) {
                throw new ArgumentException("object to and from must be the same type");
            }

            foreach (PropertyInfo propertyInfo in to.GetType().GetAllProperties()) {
                if (propertyInfo.GetGetMethod(true) == null || propertyInfo.GetSetMethod(true) == null) {
                    continue;
                }

                object fromValue = propertyInfo.GetValue(from);
                propertyInfo.SetValue(to, fromValue);
            }
        }
    }
}