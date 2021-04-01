using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoCustom {
        private const BindingFlags AllBindingFlags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy |
                                                     BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AllStaticBindingFlags = BindingFlags.Static | BindingFlags.FlattenHierarchy |
                                                           BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Regex BraceRegex = new Regex(@"\{(.+?)\}", RegexOptions.Compiled);
        private static readonly MethodInfo EntityListFindFirst = typeof(EntityList).GetMethod("FindFirst");
        private static readonly Dictionary<string, Type> AllTypes = new Dictionary<string, Type>();
        private static readonly Dictionary<string, MethodInfo> CachedGetMethodInfos = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, FieldInfo> CachedFieldInfos = new Dictionary<string, FieldInfo>();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;
        private static float FramesPerSecond => 60f / Engine.TimeRateB;

        public static void CollectAllTypeInfo() {
            AllTypes.Clear();
            CachedGetMethodInfos.Clear();
            CachedFieldInfos.Clear();
            foreach (Type type in Everest.Modules.SelectMany(module => module.GetType().Assembly.GetTypesSafe())) {
                if (type.FullName != null) {
                    AllTypes[type.FullName] = type;
                }
            }
        }

        public static string Parse() {
            if (!Settings.InfoCustom) {
                return string.Empty;
            }

            return BraceRegex.Replace(Settings.InfoCustomTemplate, match => {
                string origText = match.Value;
                string matchText = match.Groups[1].Value;

                string[] splitText = matchText.Split('.').Select(s => s.Trim()).ToArray();
                if (splitText.Length <= 1) {
                    return origText;
                }

                string typeFullName = $"Celeste.{splitText[0]}";
                if (!AllTypes.ContainsKey(typeFullName)) {
                    typeFullName = $"Monocle.{splitText[0]}";
                    if (!AllTypes.ContainsKey(typeFullName)) {
                        return $"{splitText[0]} not found";
                    }
                }

                Type type = AllTypes[typeFullName];

                IEnumerable<string> memberNames = splitText.Skip(1).ToArray();
                bool toFrame = false;
                if (memberNames.Last() == "toFrame()") {
                    memberNames = memberNames.SkipLast().ToArray();
                    toFrame = true;
                }

                if (Engine.Scene is Level level) {
                    if (type.IsSameOrSubclassOf(typeof(Entity))) {
                        if (FindEntity(type, level.Entities) is object entity) {
                            return FormatValue(GetMemberValue(entity, memberNames), toFrame);
                        } else {
                            return string.Empty;
                        }
                    } else if (type == typeof(Level)) {
                        return FormatValue(GetMemberValue(level, memberNames), toFrame);
                    }
                }

                return FormatValue(GetMemberValue(null, memberNames, type), toFrame);
            });
        }

        private static string FormatValue(object obj, bool toFrame) {
            if (obj == null) {
                return string.Empty;
            }

            if (obj is Vector2 vector2) {
                return $"{vector2.X:F2}, {vector2.Y:F2}";
            }

            if (obj is float floatValue) {
                if (toFrame) {
                    return $"{(int) FramesPerSecond * floatValue:F0}";
                } else {
                    return floatValue.ToString("F2");
                }
            }

            return obj.ToString();
        }

        private static object GetMemberValue(object obj, IEnumerable<string> memberNames, Type type = null) {
            object result = obj;
            foreach (string memberName in memberNames) {
                Type objType = result?.GetType() ?? type;
                if (GetGetMethod(objType, memberName, result == null) is MethodInfo methodInfo) {
                    result = methodInfo.Invoke(methodInfo.IsStatic ? null : result, null);
                } else if (GetFieldInfo(objType, memberName, result == null) is FieldInfo fieldInfo) {
                    result = fieldInfo.GetValue(fieldInfo.IsStatic ? null : result);
                } else {
                    return $"{memberName} not found";
                }

                if (result == null) {
                    return null;
                }
            }

            return result;
        }

        private static MethodInfo GetGetMethod(Type type, string propertyName, bool isStatic) {
            string key = $"{type.FullName}-${propertyName}-{isStatic}";
            if (CachedGetMethodInfos.ContainsKey(key)) {
                return CachedGetMethodInfos[key];
            } else {
                MethodInfo methodInfo = type.GetProperty(propertyName, isStatic ? AllStaticBindingFlags : AllBindingFlags)?.GetGetMethod(true);
                CachedGetMethodInfos[key] = methodInfo;
                return methodInfo;
            }
        }

        private static FieldInfo GetFieldInfo(Type type, string fieldName, bool isStatic) {
            string key = $"{type.FullName}-${fieldName}-{isStatic}";
            if (CachedFieldInfos.ContainsKey(key)) {
                return CachedFieldInfos[key];
            } else {
                FieldInfo fieldInfo = type.GetField(fieldName, isStatic ? AllStaticBindingFlags : AllBindingFlags);
                CachedFieldInfos[key] = fieldInfo;
                return fieldInfo;
            }
        }

        private static object FindEntity(Type type, EntityList entityList) {
            return EntityListFindFirst.MakeGenericMethod(type).Invoke(entityList, null);
        }
    }
}