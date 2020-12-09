using System;
using System.Reflection;
using System.Reflection.Emit;

namespace TAS.EverestInterop {
	public delegate object GetField(object o);
	public delegate object GetStaticField();
	public static class Extensions {
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
}
