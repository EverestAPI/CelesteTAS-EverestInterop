using System;
using System.Reflection;
using System.Reflection.Emit;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

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
	}

	internal static class DynDataExtensions {
		private const string KeyPrefix = "CelesteTAS_";
		public static void SaveValue<T>(this T target, string name, object value) where T : class {
			DynData<T> dynData = new DynData<T>(target);
			dynData.Set(KeyPrefix + name, value);
		}

		public static R LoadValue<T, R>(this T target, string name, R defaultValue = default) where T : class {
			DynData<T> dynData = new DynData<T>(target);
			object value = dynData[KeyPrefix + name];
			return value == null ? defaultValue : (R) value;
		}
	}

	internal static class MenuExtensions {
		private const string SetActionKey = nameof(SetActionKey);

		public static void SetAction(this TextMenu.Item item, Action action) {
			item.SaveValue(SetActionKey, action);
		}

		public static void InvokeAction(this TextMenu.Item item) {
			item.LoadValue<TextMenu.Item, Action>(SetActionKey)?.Invoke();
		}
	}

	internal static class EntityExtensions {
		private const string LastPositionKey = nameof(LastPositionKey);

		public static void SaveLastPosition(this Entity entity) {
			entity.SaveValue(LastPositionKey, entity.Position);
		}

		public static Vector2 LoadLastPosition(this Entity entity) {
			return entity.LoadValue(LastPositionKey, entity.Position);
		}
	}
}
