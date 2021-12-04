using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoCustom {
        private const BindingFlags AllBindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Regex BraceRegex = new(@"\{(.+?)\}", RegexOptions.Compiled);
        private static readonly Regex TypeNameRegex = new(@"^([.\w=+<>]+)(\[(.+?)\])?(@([^.]*))?$", RegexOptions.Compiled);
        private static readonly Regex TypeNameSeparatorRegex = new(@"^[.+]", RegexOptions.Compiled);
        private static readonly Dictionary<string, Type> AllTypes = new();
        private static readonly Dictionary<string, Type> CachedParsedTypes = new();
        private static readonly Dictionary<string, MethodInfo> CachedGetMethodInfos = new();
        private static readonly Dictionary<string, MethodInfo> CachedSetMethodInfos = new();
        private static readonly Dictionary<string, FieldInfo> CachedFieldInfos = new();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [LoadContent]
        private static void CollectAllTypeInfo() {
            AllTypes.Clear();
            CachedParsedTypes.Clear();
            CachedGetMethodInfos.Clear();
            CachedSetMethodInfos.Clear();
            CachedFieldInfos.Clear();
            foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypes()) {
                if (type.FullName != null) {
                    AllTypes[$"{type.FullName}@{type.Assembly.GetName().Name}"] = type;
                }
            }
        }

        public static string Parse(int? decimals = null) {
            decimals ??= Settings.CustomInfoDecimals;
            Dictionary<string, List<Entity>> cachedEntities = new();

            return BraceRegex.Replace(Settings.InfoCustomTemplate, match => {
                string matchText = match.Groups[1].Value;

                if (!TryParseMemberNames(matchText, out string typeText, out List<string> memberNames, out string errorMessage)) {
                    return errorMessage;
                }

                if (!TryParseType(typeText, out Type type, out string entityId, out errorMessage)) {
                    return errorMessage;
                }

                string helperMethod = memberNames.Last();
                if (helperMethod is "toFrame()" or "toPixelPerFrame()") {
                    memberNames = memberNames.SkipLast().ToList();
                }

                if (GetGetMethod(type, memberNames.First()) is {IsStatic: true} || GetFieldInfo(type, memberNames.First()) is {IsStatic: true}) {
                    return FormatValue(GetMemberValue(type, null, memberNames), helperMethod, decimals.Value);
                }

                if (Engine.Scene is Level level) {
                    if (type.IsSameOrSubclassOf(typeof(Entity))) {
                        List<Entity> entities;
                        if (cachedEntities.ContainsKey(typeText)) {
                            entities = cachedEntities[typeText];
                        } else {
                            entities = FindEntities(type, entityId)?.ToList();
                            cachedEntities[typeText] = entities;
                        }

                        if (entities == null) {
                            return "Ignore NPE Warning";
                        }

                        return string.Join("", entities.Select(entity => {
                            string value = FormatValue(GetMemberValue(type, entity, memberNames), helperMethod, decimals.Value);

                            if (entities.Count > 1) {
                                if (entity.GetEntityData()?.ToEntityId().ToString() is { } id) {
                                    value = $"\n[{id}]{value}";
                                } else {
                                    value = $"\n{value}";
                                }
                            }

                            return value;
                        }));
                    } else if (type == typeof(Level)) {
                        return FormatValue(GetMemberValue(type, level, memberNames), helperMethod, decimals.Value);
                    }
                }

                return string.Empty;
            });
        }

        public static bool TryParseMemberNames(string matchText, out string typeText, out List<string> memberNames, out string errorMessage) {
            typeText = errorMessage = "";
            memberNames = new List<string>();

            List<string> splitText = matchText.Split('.').Select(s => s.Trim()).Where(s => s.IsNotEmpty()).ToList();
            if (splitText.Count <= 1) {
                errorMessage = "missing member";
                return false;
            }

            if (matchText.Contains("@")) {
                int assemblyIndex = splitText.FindIndex(s => s.Contains("@"));
                typeText = string.Join(".", splitText.Take(assemblyIndex + 1));
                memberNames = splitText.Skip(assemblyIndex + 1).ToList();
            } else {
                typeText = splitText[0];
                memberNames = splitText.Skip(1).ToList();
            }

            if (memberNames.Count <= 0) {
                errorMessage = "missing member";
                return false;
            }

            return true;
        }

        public static bool TryParseType(string text, out Type type, out string entityId, out string errorMessage) {
            type = null;
            entityId = "";
            errorMessage = "";

            if (!TryParseTypeName(text, out string typeNameMatched, out string typeNameWithAssembly, out entityId)) {
                errorMessage = "paring type name failed";
                return false;
            }

            if (CachedParsedTypes.Keys.Contains(typeNameWithAssembly)) {
                type = CachedParsedTypes[typeNameWithAssembly];
                return true;
            } else {
                // find the full type name
                List<string> matchTypeNames = AllTypes.Keys.Where(name => name.StartsWith(typeNameWithAssembly)).ToList();

                string typeName = TypeNameSeparatorRegex.Replace(typeNameWithAssembly, "");
                if (matchTypeNames.IsEmpty()) {
                    // find the part of type name
                    matchTypeNames = AllTypes.Keys.Where(name => name.Contains($".{typeName}")).ToList();
                }

                if (matchTypeNames.IsEmpty()) {
                    // find the nested type name
                    matchTypeNames = AllTypes.Keys.Where(name => name.Contains($"+{typeName}")).ToList();
                }

                switch (matchTypeNames.Count) {
                    case 0:
                        errorMessage = $"{typeNameMatched} not found";
                        return false;
                    case > 1:
                        errorMessage = $"type with the same name exists:\n{string.Join("\n", matchTypeNames)}";
                        return false;
                    default:
                        CachedParsedTypes[typeNameWithAssembly] = type = AllTypes[matchTypeNames.First()];
                        return true;
                }
            }
        }

        private static bool TryParseTypeName(string text, out string typeNameMatched, out string typeNameWithAssembly, out string entityId) {
            typeNameMatched = "";
            typeNameWithAssembly = "";
            entityId = "";
            if (TypeNameRegex.Match(text) is {Success: true} match) {
                typeNameMatched = match.Groups[1].Value;
                typeNameWithAssembly = $"{typeNameMatched}@{match.Groups[5].Value}";
                typeNameWithAssembly = typeNameWithAssembly switch {
                    "Theo@" => "TheoCrystal@",
                    "Jellyfish@" => "Glider@",
                    _ => typeNameWithAssembly
                };
                entityId = match.Groups[3].Value;
                return true;
            } else {
                return false;
            }
        }

        private static string FormatValue(object obj, string helperMethod, int decimals) {
            if (obj == null) {
                return string.Empty;
            }

            if (obj is Vector2 vector2) {
                if (helperMethod == "toPixelPerFrame()") {
                    vector2 = GameInfo.ConvertSpeedUnit(vector2, SpeedUnit.PixelPerFrame);
                }

                return vector2.ToSimpleString(decimals);
            }

            if (obj is Vector2Double vector2Double) {
                return vector2Double.ToSimpleString(decimals);
            }

            if (obj is float floatValue) {
                if (helperMethod == "toFrame()") {
                    return GameInfo.ConvertToFrames(floatValue).ToString();
                } else if (helperMethod == "toPixelPerFrame()") {
                    return GameInfo.ConvertSpeedUnit(floatValue, SpeedUnit.PixelPerFrame).ToString(CultureInfo.InvariantCulture);
                } else {
                    return floatValue.ToFormattedString(decimals);
                }
            }

            return obj.ToString();
        }

        public static object GetMemberValue(Type type, object obj, List<string> memberNames) {
            foreach (string memberName in memberNames) {
                if (GetGetMethod(type, memberName) is { } methodInfo) {
                    if (methodInfo.IsStatic) {
                        obj = methodInfo.Invoke(null, null);
                    } else if (obj != null) {
                        if (obj is Actor actor && memberName == "ExactPosition") {
                            obj = actor.GetMoreExactPosition(true);
                        } else {
                            obj = methodInfo.Invoke(obj, null);
                        }
                    }
                } else if (GetFieldInfo(type, memberName) is { } fieldInfo) {
                    if (fieldInfo.IsStatic) {
                        obj = fieldInfo.GetValue(null);
                    } else if (obj != null) {
                        if (obj is Actor actor && memberName == "Position") {
                            obj = actor.GetMoreExactPosition(true);
                        } else {
                            obj = fieldInfo.GetValue(obj);
                        }
                    }
                } else {
                    return $"{memberName} not found";
                }

                if (obj == null) {
                    return null;
                }

                type = obj.GetType();
            }

            return obj;
        }

        public static MethodInfo GetGetMethod(Type type, string propertyName) {
            string key = $"{type.FullName}.get_{propertyName}";
            if (CachedGetMethodInfos.ContainsKey(key)) {
                return CachedGetMethodInfos[key];
            } else {
                MethodInfo methodInfo = type.GetProperty(propertyName, AllBindingFlags)?.GetGetMethod(true);
                if (methodInfo == null && type.BaseType != null) {
                    methodInfo = GetGetMethod(type.BaseType, propertyName);
                }

                CachedGetMethodInfos[key] = methodInfo;
                return methodInfo;
            }
        }

        public static MethodInfo GetSetMethod(Type type, string propertyName) {
            string key = $"{type.FullName}.set_{propertyName}";
            if (CachedSetMethodInfos.ContainsKey(key)) {
                return CachedSetMethodInfos[key];
            } else {
                MethodInfo methodInfo = type.GetProperty(propertyName, AllBindingFlags)?.GetSetMethod(true);
                if (methodInfo == null && type.BaseType != null) {
                    methodInfo = GetSetMethod(type.BaseType, propertyName);
                }

                CachedSetMethodInfos[key] = methodInfo;
                return methodInfo;
            }
        }

        public static FieldInfo GetFieldInfo(Type type, string fieldName) {
            string key = $"{type.FullName}.{fieldName}";
            if (CachedFieldInfos.ContainsKey(key)) {
                return CachedFieldInfos[key];
            } else {
                FieldInfo fieldInfo = type.GetField(fieldName, AllBindingFlags);
                if (fieldInfo == null && type.BaseType != null) {
                    fieldInfo = GetFieldInfo(type.BaseType, fieldName);
                }

                CachedFieldInfos[key] = fieldInfo;
                return fieldInfo;
            }
        }

        public static List<Entity> FindEntities(Type type, string entityId) {
            List<Entity> entities;
            if (Engine.Scene.Tracker.Entities.ContainsKey(type)) {
                entities = Engine.Scene.Tracker.Entities[type].ToList();
            } else {
                entities = Engine.Scene.Entities.Where(entity => entity.GetType().IsSameOrSubclassOf(type)).ToList();
            }

            if (entityId.IsNullOrEmpty()) {
                return entities;
            } else {
                return entities.Where(entity => entity.GetEntityData()?.ToEntityId().ToString() == entityId).ToList();
            }
        }
    }
}