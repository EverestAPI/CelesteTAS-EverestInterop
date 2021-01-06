using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using Platform = Celeste.Platform;

namespace TAS.EverestInterop {
	internal static class ReflectionExtensions {
		public delegate object GetField(object o);
		public delegate object GetStaticField();
		public static object GetPublicField(this object obj, string name) {
			return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
		}
		public static object GetPrivateField(this object obj, string name) {
			return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
		}
		public static object InvokePrivateMethod(this object obj, string methodName, params object[] parameters) {
			return obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
				?.Invoke(obj, parameters);
		}
		public static T CreateDelegate_Get<T>(this FieldInfo field) where T : Delegate {
			bool isStatic = field.IsStatic;
			Type[] param;
			if (!isStatic)
				param = new Type[] { field.DeclaringType };
			else
				param = new Type[0];
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

			DynamicMethod dyn = new DynamicMethod(field.Name + "_FastAccess", typeof(object), new Type[] { typeof(object) }, field.DeclaringType);
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
	}

	internal static class CommonExtensions {
		public static T Apply<T>(this T obj, Action<T> action) {
			action(obj);
			return obj;
		}

		public static bool IsType<T>(this object obj) {
			return obj?.GetType() == typeof(T);
		}

		public static bool IsType<T>(this Type type) {
			return type == typeof(T);
		}

		public static bool IsNotType<T>(this object obj) {
			return !obj.IsType<T>();
		}

		public static bool IsNotType<T>(this Type type) {
			return !type.IsType<T>();
		}
	}

	// source from: https://stackoverflow.com/a/17264480
	internal static class ExtendedDataExtensions {
		private const string NamePrefix = "CelesteTAS_";
		private static readonly ConditionalWeakTable<object, object> ExtendedData =
			new ConditionalWeakTable<object, object>();

		private static IDictionary<string, object> CreateDictionary(object o) {
			return new Dictionary<string, object>();
		}

		public static void SetExtendedDataValue(this object o, string name, object value) {
			if (string.IsNullOrWhiteSpace(name)) {
				throw new ArgumentException("Invalid name");
			}

			name = name.Trim() + NamePrefix;

			IDictionary<string, object> values =
				(IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

			if (value != null) {
				values[name] = value;
			} else {
				values.Remove(name);
			}
		}

		public static T GetExtendedDataValue<T>(this object o, string name){
			if (string.IsNullOrWhiteSpace(name)) {
				throw new ArgumentException("Invalid name");
			}

			name = name.Trim() + NamePrefix;

			IDictionary<string, object> values =
				(IDictionary<string, object>) ExtendedData.GetValue(o, CreateDictionary);

			if (values.ContainsKey(name)) {
				return (T) values[name];
			}

			return default;
		}
	}

	internal static class MenuExtensions {
		private const string SetActionKey = nameof(SetActionKey);

		public static void SetAction(this TextMenu.Item item, Action action) {
			item.SetExtendedDataValue(SetActionKey, action);
		}

		public static void InvokeAction(this TextMenu.Item item) {
			item.GetExtendedDataValue<Action>(SetActionKey)?.Invoke();
		}
	}

	internal static class EntityExtensions {
		private const string LastPositionKey = nameof(LastPositionKey);
		private const string PlayerUpdatedKey = nameof(PlayerUpdatedKey);

		public static void SaveLastPosition(this Entity entity) {
			entity.SetExtendedDataValue(LastPositionKey, entity.Position);
		}

		public static Vector2 LoadLastPosition(this Entity entity) {
			return entity.GetExtendedDataValue<Vector2>(LastPositionKey);
		}

		public static void SavePlayerUpdated(this Entity entity, bool playerUpdated) {
			entity.SetExtendedDataValue(PlayerUpdatedKey, playerUpdated);
		}

		public static bool UpdateLaterThanPlayer(this Entity entity) {
			return entity.GetExtendedDataValue<bool>(PlayerUpdatedKey);
		}
	}
}
