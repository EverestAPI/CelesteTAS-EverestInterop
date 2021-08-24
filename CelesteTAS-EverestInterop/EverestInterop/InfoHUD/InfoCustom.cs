using System;
using System.Collections;
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
        private const BindingFlags AllInstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AllStaticBindingFlags = BindingFlags.Static | BindingFlags.FlattenHierarchy |
                                                           BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Regex BraceRegex = new(@"\{(.+?)\}", RegexOptions.Compiled);
        private static readonly Regex EntityIdSuffixRegex = new(@"\[(.+?)\]$", RegexOptions.Compiled);
        private static readonly Regex ModTypeNameRegex = new(@"(.+@[^\.]+?)\.", RegexOptions.Compiled);
        private static readonly MethodInfo EntityListFindAll = typeof(EntityList).GetMethod("FindAll");
        private static readonly Dictionary<string, Type> AllTypes = new();
        private static readonly Dictionary<string, MethodInfo> CachedGetMethodInfos = new();
        private static readonly Dictionary<string, FieldInfo> CachedFieldInfos = new();

        private static CelesteTasModuleSettings Settings => CelesteTasModule.Settings;

        public static void CollectAllTypeInfo() {
            AllTypes.Clear();
            CachedGetMethodInfos.Clear();
            CachedFieldInfos.Clear();
            Assembly officialAssembly = typeof(Celeste.Celeste).Assembly;
            foreach (Type type in Everest.Modules.SelectMany(module => module.GetType().Assembly.GetTypesSafe())) {
                if (type.FullName != null) {
                    if (type.Assembly == officialAssembly) {
                        AllTypes[type.FullName] = type;
                    } else {
                        AllTypes[$"{type.FullName}@{type.Assembly.GetName().Name}"] = type;
                    }
                }
            }
        }

        public static string Parse(bool export = false) {
            if (Settings.InfoCustom == HudOptions.Off && !export) {
                return string.Empty;
            }

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
                    typeFullName = $"Celeste.{typeSimpleName}";
                    if (!AllTypes.ContainsKey(typeFullName)) {
                        typeFullName = $"Monocle.{typeSimpleName}";
                    }
                }

                if (!AllTypes.ContainsKey(typeFullName)) {
                    return $"{typeFullName} not found";
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
                        List<Entity> entities;
                        if (cachedEntities.ContainsKey(firstText)) {
                            entities = cachedEntities[firstText];
                        } else {
                            entities = FindEntities(type, level, entityId)?.ToList();
                            cachedEntities[firstText] = entities;
                        }

                        if (entities == null) {
                            return string.Empty;
                        } else {
                            return string.Join("", entities.Select(entity => {
                                string value = FormatValue(GetMemberValue(entity, memberNames), toFrame);

                                if (entities.Count > 1) {
                                    if (entity.LoadEntityData()?.ToEntityId().ToString() is { } id) {
                                        value = $"\n[{id}]{value}";
                                    } else {
                                        value = $"\n{value}";
                                    }
                                }

                                return value;
                            }));
                        }
                    } else if (type == typeof(Level)) {
                        return FormatValue(GetMemberValue(level, memberNames), toFrame);
                    }
                }

                return FormatValue(GetMemberValue(null, memberNames, type), toFrame);
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

        private static string FormatValue(object obj, bool toFrame) {
            if (obj == null) {
                return string.Empty;
            }

            if (obj is Vector2 vector2) {
                return vector2.ToSimpleString(Settings.RoundCustomInfo);
            }

            if (obj is float floatValue) {
                if (toFrame) {
                    return GameInfo.ConvertToFrames(floatValue).ToString();
                } else {
                    return Settings.RoundCustomInfo ? $"{floatValue:F2}" : $"{floatValue:F12}";
                }
            }

            return obj.ToString();
        }

        private static object GetMemberValue(object obj, IEnumerable<string> memberNames, Type type = null) {
            object result = obj;
            foreach (string memberName in memberNames) {
                Type objType = result?.GetType() ?? type;
                if (GetGetMethod(objType, memberName, result == null) is { } methodInfo) {
                    result = methodInfo.Invoke(methodInfo.IsStatic ? null : result, null);
                } else if (GetFieldInfo(objType, memberName, result == null) is { } fieldInfo) {
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
                MethodInfo methodInfo = type.GetProperty(propertyName, isStatic ? AllStaticBindingFlags : AllInstanceBindingFlags)
                    ?.GetGetMethod(true);
                if (methodInfo == null && type.BaseType != null) {
                    methodInfo = GetGetMethod(type.BaseType, propertyName, isStatic);
                }

                CachedGetMethodInfos[key] = methodInfo;
                return methodInfo;
            }
        }

        private static FieldInfo GetFieldInfo(Type type, string fieldName, bool isStatic) {
            string key = $"{type.FullName}-${fieldName}-{isStatic}";
            if (CachedFieldInfos.ContainsKey(key)) {
                return CachedFieldInfos[key];
            } else {
                FieldInfo fieldInfo = type.GetField(fieldName, isStatic ? AllStaticBindingFlags : AllInstanceBindingFlags);
                if (fieldInfo == null && type.BaseType != null) {
                    fieldInfo = GetFieldInfo(type.BaseType, fieldName, isStatic);
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
                IList list = EntityListFindAll.MakeGenericMethod(type).Invoke(level.Entities, null) as IList;
                entities = list?.Cast<Entity>();
            }

            if (entityId.IsNullOrEmpty()) {
                return entities;
            } else {
                return entities?.Where(entity => entity.LoadEntityData()?.ToEntityId().ToString() == entityId);
            }
        }
    }
}