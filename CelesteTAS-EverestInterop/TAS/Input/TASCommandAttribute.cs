using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Mod;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input {
    /* Additional commands can be added by giving them the TASCommand attribute and naming them (CommandName)Command.
     * The execute at start field indicates whether a command should be executed while building the input list (read, play)
     * or when playing the file (console).
     * The args field should list formats the command takes. This is not currently used but may be implemented into Studio in the future.
     * Commands that execute can be void Command(string[], InputController, int) or void Command(string[]) or void Command().
     */
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TasCommandAttribute : Attribute {
        private static readonly IDictionary<TasCommandAttribute, MethodInfo> MethodInfos = new Dictionary<TasCommandAttribute, MethodInfo>();
        public string[] AliasNames;
        public bool CalcChecksum = true;
        public ExecuteTiming ExecuteTiming = ExecuteTiming.Runtime;
        public bool LegalInMainGame = true;
        public readonly string Name;

        public TasCommandAttribute(string name) {
            Name = name;
        }

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

    public enum ExecuteTiming {
        Parse,
        Runtime
    }
}