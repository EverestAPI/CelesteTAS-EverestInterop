using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using TAS.EverestInterop;
using TAS.Utils;

namespace TAS.Input {
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TasCommandAttribute : Attribute {
        private static readonly IDictionary<TasCommandAttribute, MethodInfo> MethodInfos = new Dictionary<TasCommandAttribute, MethodInfo>();
        public string[] AliasNames;
        public bool AlwaysExecuteAtStart;
        public bool CalcChecksum = true;
        public bool ExecuteAtParse;
        public bool ExecuteAtStart;
        public bool LegalInMainGame = true;
        public string Name;

        public bool IsName(string name) {
            bool result = Name.Equals(name, StringComparison.InvariantCultureIgnoreCase);
            if (AliasNames.IsNullOrEmpty()) {
                return result;
            } else {
                return result || AliasNames.Any(aliasName => aliasName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
            }
        }

        [LoadContent]
        private static void CollectMethods() {
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

        public static KeyValuePair<TasCommandAttribute, MethodInfo> FindMethod(string commandName) {
            return MethodInfos.FirstOrDefault(pair => pair.Key.IsName(commandName));
        }
    }
}