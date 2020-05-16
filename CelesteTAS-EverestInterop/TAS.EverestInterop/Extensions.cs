using System.Reflection;

namespace TAS.EverestInterop {
	public static class Extensions {
		public static object GetPrivateField(this object obj, string name) {
			return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
		}
		public static object InvokePrivateMethod(this object obj, string methodName, params object[] parameters) {
			return obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
				?.Invoke(obj, parameters);
		}
	}
}
