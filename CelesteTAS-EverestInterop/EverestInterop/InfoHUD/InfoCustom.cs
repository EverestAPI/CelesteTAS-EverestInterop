using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Celeste;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Monocle;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD {
    public static class InfoCustom {
        private const BindingFlags AllBindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Regex BraceRegex = new(@"\{(.+?)\}", RegexOptions.Compiled);
        private static readonly Regex TypeNameRegex = new(@"^([.\w=+<>]+)(\[(.+?)\])?(@([^.]*))?$", RegexOptions.Compiled);
        private static readonly MethodInfo EntityListFindAll = typeof(EntityList).GetMethod("FindAll");
        private static readonly Dictionary<string, Type> AllTypes = new();
        private static readonly Dictionary<string, string> CachedEntitiesFullName = new();
        private static readonly Dictionary<string, MethodInfo> CachedGetMethodInfos = new();
        private static readonly Dictionary<string, FieldInfo> CachedFieldInfos = new();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [LoadContent]
        private static void CollectAllTypeInfo() {
            AllTypes.Clear();
            CachedEntitiesFullName.Clear();
            CachedGetMethodInfos.Clear();
            CachedFieldInfos.Clear();
            foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypes()) {
                if (type.FullName != null) {
                    AllTypes[$"{type.FullName}@{type.Assembly.GetName().Name}"] = type;
                }
            }
        }

        public static string Parse(bool alwaysUpdate = false, int? decimals = null) {
            if (Settings.InfoCustom == HudOptions.Off && !alwaysUpdate) {
                return string.Empty;
            }

            decimals ??= Settings.CustomInfoDecimals;
            Dictionary<string, List<Entity>> cachedEntities = new();

            return BraceRegex.Replace(Settings.InfoCustomTemplate, match => {
                string matchText = match.Groups[1].Value;

                List<string> splitText = matchText.Split('.').Select(s => s.Trim()).Where(s => s.IsNotEmpty()).ToList();
                if (splitText.Count <= 1) {
                    return "missing member";
                }

                string firstText;
                List<string> memberNames;

                if (matchText.Contains("@")) {
                    int assemblyIndex = splitText.FindIndex(s => s.Contains("@"));
                    firstText = string.Join(".", splitText.Take(assemblyIndex + 1));
                    memberNames = splitText.Skip(assemblyIndex + 1).ToList();
                } else {
                    firstText = splitText[0];
                    memberNames = splitText.Skip(1).ToList();
                }

                if (memberNames.Count <= 0) {
                    return "missing member";
                }

                if (!TryParseType(firstText, out Type type, out string entityId, out string errorMessage)) {
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
                        if (cachedEntities.ContainsKey(firstText)) {
                            entities = cachedEntities[firstText];
                        } else {
                            entities = FindEntities(type, level, entityId)?.ToList();
                            cachedEntities[firstText] = entities;
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

        public static bool TryParseType(string text, out Type type, out string entityId, out string errorMessage) {
            type = null;
            entityId = "";
            errorMessage = "";

            if (!TryParseTypeName(text, out string typeNameMatched, out string typeNameWithAssembly, out entityId)) {
                errorMessage = "paring type name failed";
                return false;
            }

            if (CachedEntitiesFullName.Keys.Contains(typeNameWithAssembly)) {
                typeNameWithAssembly = CachedEntitiesFullName[typeNameWithAssembly];
            } else {
                // find the full type name
                List<string> matchTypeNames = AllTypes.Keys.Where(name => name.StartsWith(typeNameWithAssembly)).ToList();
                if (matchTypeNames.IsEmpty() && !typeNameWithAssembly.StartsWith(".")) {
                    // find the part of type name
                    matchTypeNames = AllTypes.Keys.Where(name => name.Contains($".{typeNameWithAssembly}")).ToList();
                }

                switch (matchTypeNames.Count) {
                    case 0: {
                        errorMessage = $"{typeNameMatched} not found";
                        return false;
                    }
                    case > 1: {
                        errorMessage = $"type with the same name exists:\n{string.Join("\n", matchTypeNames)}";
                        return false;
                    }
                    case 1:
                        typeNameWithAssembly = matchTypeNames.First();
                        CachedEntitiesFullName[typeNameMatched] = typeNameWithAssembly;
                        break;
                }
            }

            if (AllTypes.ContainsKey(typeNameWithAssembly)) {
                type = AllTypes[typeNameWithAssembly];
                return true;
            } else {
                errorMessage = $"{typeNameWithAssembly} not found";
                return false;
            }
        }

        private static bool TryParseTypeName(string text, out string typeNameMatched, out string typeNameWithAssembly, out string entityId) {
            typeNameMatched = "";
            typeNameWithAssembly = "";
            entityId = "";
            if (TypeNameRegex.Match(text) is {Success: true} match) {
                typeNameMatched = match.Groups[1].Value;
                typeNameWithAssembly = $"{typeNameMatched}@{match.Groups[5].Value}";
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
                    return floatValue.ToString($"F{decimals}");
                }
            }

            return obj.ToString();
        }

        private static object GetMemberValue(Type type, object obj, IEnumerable<string> memberNames) {
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
                        obj = fieldInfo.GetValue(obj);
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

        private static MethodInfo GetGetMethod(Type type, string propertyName) {
            string key = $"{type.FullName}-${propertyName}";
            if (CachedGetMethodInfos.ContainsKey(key)) {
                return CachedGetMethodInfos[key];
            } else {
                MethodInfo methodInfo = type.GetProperty(propertyName, AllBindingFlags)
                    ?.GetGetMethod(true);
                if (methodInfo == null && type.BaseType != null) {
                    methodInfo = GetGetMethod(type.BaseType, propertyName);
                }

                CachedGetMethodInfos[key] = methodInfo;
                return methodInfo;
            }
        }

        private static FieldInfo GetFieldInfo(Type type, string fieldName) {
            string key = $"{type.FullName}-${fieldName}";
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

        private static IEnumerable<Entity> FindEntities(Type type, Level level, string entityId) {
            IEnumerable<Entity> entities;
            if (level.Tracker.Entities.ContainsKey(type)) {
                entities = level.Tracker.Entities[type];
            } else {
                IList list = (IList) EntityListFindAll.MakeGenericMethod(type).Invoke(level.Entities, null);
                entities = list.Cast<Entity>();
            }

            if (entityId.IsNullOrEmpty()) {
                return entities;
            } else {
                return entities.Where(entity => entity.GetEntityData()?.ToEntityId().ToString() == entityId);
            }
        }
    }
}