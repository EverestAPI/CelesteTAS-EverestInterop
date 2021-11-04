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
        private static readonly Regex EntityIdSuffixRegex = new(@"\[(.+?)\]$", RegexOptions.Compiled);
        private static readonly Regex ModTypeNameRegex = new(@"(.+@[^\.]+?)\.", RegexOptions.Compiled);
        private static readonly MethodInfo EntityListFindAll = typeof(EntityList).GetMethod("FindAll");
        public static readonly Dictionary<string, Type> AllTypes = new();
        private static readonly Dictionary<string, string> CachedEntitiesFullName = new();
        private static readonly Dictionary<string, MethodInfo> CachedGetMethodInfos = new();
        private static readonly Dictionary<string, FieldInfo> CachedFieldInfos = new();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        [LoadContent]
        private static void CollectAllTypeInfo() {
            AllTypes.Clear();
            CachedGetMethodInfos.Clear();
            CachedFieldInfos.Clear();
            CachedEntitiesFullName.Clear();
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

                string[] splitText = matchText.Split('.').Select(s => s.Trim()).ToArray();
                if (splitText.Length <= 1) {
                    return "invalid template";
                }

                string typeFullName;
                string entityId;
                string firstText = splitText[0];

                if (matchText.Contains("@")) {
                    if (ModTypeNameRegex.Match(matchText) is { } matchTypeName) {
                        firstText = matchTypeName.Groups[1].Value;
                        typeFullName = TryParseTypeName(firstText, out entityId);
                        List<string> modTypeSplitText = ModTypeNameRegex.Replace(matchText, string.Empty).Split('.').ToList();
                        modTypeSplitText.Insert(0, typeFullName);
                        splitText = modTypeSplitText.ToArray();
                        if (splitText.Length <= 1) {
                            return "invalid template";
                        }
                    } else {
                        return "invalid template";
                    }
                } else {
                    string typeSimpleName = TryParseTypeName(firstText, out entityId);
                    if (CachedEntitiesFullName.Keys.Contains(typeSimpleName)) {
                        typeFullName = CachedEntitiesFullName[typeSimpleName];
                    } else {
                        List<string> matchTypeNames = AllTypes.Keys.Where(typeName => typeName.Contains($".{typeSimpleName}@")).ToList();
                        if (matchTypeNames.IsEmpty()) {
                            return $"{typeSimpleName} not found";
                        } else if (matchTypeNames.Count > 1) {
                            return $"type with the same name exists: {string.Join(", ", matchTypeNames)}";
                        } else {
                            typeFullName = matchTypeNames.First();
                            CachedEntitiesFullName[typeSimpleName] = typeFullName;
                        }
                    }
                }

                if (!AllTypes.ContainsKey(typeFullName)) {
                    return $"{typeFullName} not found";
                }

                Type type = AllTypes[typeFullName];

                IEnumerable<string> memberNames = splitText.Skip(1).ToArray();
                string helperMethod = memberNames.Last();
                if (helperMethod is "toFrame()" or "toPixelPerFrame()") {
                    memberNames = memberNames.SkipLast().ToArray();
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

        private static string TryParseTypeName(string firstText, out string entityId) {
            if (EntityIdSuffixRegex.IsMatch(firstText)) {
                entityId = EntityIdSuffixRegex.Match(firstText).Groups[1].Value;
                return EntityIdSuffixRegex.Replace(firstText, "");
            } else {
                entityId = string.Empty;
                return firstText;
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