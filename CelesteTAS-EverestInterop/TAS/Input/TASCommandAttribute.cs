using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using TAS.Utils;

namespace TAS.Input {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TasCommandAttribute : Attribute {
        private static readonly IDictionary<TasCommandAttribute, MethodInfo> MethodInfos = new Dictionary<TasCommandAttribute, MethodInfo>();
        public bool ExecuteAtStart;
        public bool LegalInMainGame = true;
        public string Name;
        public bool SavestateChecksum = true;

        public static void CollectMethods() {
            MethodInfos.Clear();
            IEnumerable<MethodInfo> methodInfos = Assembly.GetCallingAssembly().GetTypesSafe().SelectMany(type => type
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(info => info.GetCustomAttributes<TasCommandAttribute>().IsNotEmpty());
            foreach (MethodInfo methodInfo in methodInfos) {
                IEnumerable<TasCommandAttribute> tasCommandAttributes = methodInfo.GetCustomAttributes<TasCommandAttribute>();
                foreach (TasCommandAttribute tasCommandAttribute in tasCommandAttributes) {
                    MethodInfos[tasCommandAttribute] = methodInfo;
                }
            }
        }

        public static KeyValuePair<TasCommandAttribute, MethodInfo> FindMethod(string commandType) {
            return MethodInfos.FirstOrDefault(pair => pair.Key.Name.Equals(commandType, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}