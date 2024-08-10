using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using TAS.EverestInterop.Lua;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.EverestInterop.InfoHUD;

public static class InfoCustom {
    private static readonly Regex LuaRegex = new(@"\[\[(.+?)\]\]", RegexOptions.Compiled);
    private static readonly Regex BraceRegex = new(@"\{(.+?)\}", RegexOptions.Compiled);
    private static readonly Regex TypeNameRegex = new(@"^([.\w=+<>]+)(\[(.+?)\])?(@([^.]*))?$", RegexOptions.Compiled);
    private static readonly Regex TypeNameSeparatorRegex = new(@"^[.+]", RegexOptions.Compiled);
    private static readonly Regex MethodRegex = new(@"^(.+)\((.*)\)$", RegexOptions.Compiled);
    private static readonly Dictionary<string, Type> AllTypes = new();
    private static readonly Dictionary<string, List<Type>> CachedParsedTypes = new();
    private static bool EnforceLegal => EnforceLegalCommand.EnabledWhenRunning && !AssertCommand.Running;

    public delegate bool HelperMethod(object obj, int decimals, out string formattedValue);

    // return true if obj is of expected parameter type. otherwise we call AutoFormatter
    private static readonly Dictionary<string, HelperMethod> HelperMethods = new();

    [Initialize]
    private static void CollectAllTypeInfo() {
        AllTypes.Clear();
        CachedParsedTypes.Clear();
        foreach (Type type in ModUtils.GetTypes()) {
            if (type.FullName is { } fullName) {
                string assemblyName = type.Assembly.GetName().Name;
                string modName = ConsoleEnhancements.GetModName(type);
                AllTypes[$"{fullName}@{assemblyName}"] = type;
                AllTypes[$"{fullName}@{modName}"] = type;

                if (!fullName.StartsWith("Celeste.Mod.Everest+Events")) {
                    string fullNameAlternative = fullName.Replace("+", ".");
                    AllTypes[$"{fullNameAlternative}@{assemblyName}"] = type;
                    AllTypes[$"{fullNameAlternative}@{modName}"] = type;
                }
            }
        }
    }

    [Initialize]
    private static void InitializeHelperMethods() {
        HelperMethods.Add("toFrame()", HelperMethod_toFrame);
        HelperMethods.Add("toPixelPerFrame()", HelperMethod_toPixelPerFrame);
    }

    public static string GetInfo(int? decimals = null) {
        decimals ??= TasSettings.CustomInfoDecimals;
        Dictionary<string, List<Entity>> cachedEntities = new();

        return ParseTemplate(TasSettings.InfoCustomTemplate, decimals.Value, cachedEntities, false);
    }

    [Monocle.Command("get", "get type.fieldOrProperty value. eg get Player,Position; get Level.Wind (CelesteTAS)")]
    private static void GetCommand(string template) {
        ParseTemplate($"{{{template}}}", TasSettings.CustomInfoDecimals, new Dictionary<string, List<Entity>>(), true).ConsoleLog();
    }

    private static object ParseVector2(object input) {
        if (input is Vector2 vector2) {
            return (vector2.X, vector2.Y);
        } else {
            return input;
        }
    }

    public static object GetRawInfo((string, bool) args) {
        var template = args.Item1;
        var forceList = args.Item2;
        if (!TryParseMemberNames(template, out string typeText, out List<string> memberNames, out string errorMessage)) {
            return errorMessage;
        }
        if (!TryParseType(typeText, out Type type, out string entityId, out errorMessage)) {
            return errorMessage;
        }
        bool moreThanOneEntity = false;
        if (type.IsSameOrSubclassOf(typeof(Entity))) {
            moreThanOneEntity = FindEntities(type, entityId).Count() > 1;
        }
        if (memberNames.IsNotEmpty() && (
            type.GetGetMethod(memberNames.First()) is { IsStatic: true } ||
            type.GetFieldInfo(memberNames.First()) is { IsStatic: true } ||
            (MethodRegex.Match(memberNames.First()) is { Success: true } match &&
            type.GetMethodInfo(match.Groups[1].Value) is { IsStatic: true })
        )) {
            return ParseVector2(GetMemberValue(type, null, memberNames));
        }
        if (Engine.Scene is Level level) {
            if (type.IsSameOrSubclassOf(typeof(Entity))) {
                List<Entity> entities = FindEntities(type, entityId);
                if (entities == null) {
                    return new List<object>();
                }
                var result = entities.Select(entity => {
                    return ParseVector2(GetMemberValue(type, entity, memberNames));
                }).ToList();
                if (result.Count == 1 && !forceList) {
                    return result[0];
                } else {
                    return result;
                }
            } else if (type == typeof(Level)) {
                return ParseVector2(GetMemberValue(type, level, memberNames));
            } else if (type == typeof(Session)) {
                return ParseVector2(GetMemberValue(type, level.Session, memberNames));
            } else {
                return $"Instance of {type.FullName} not found";
            }
        }
        return 0;
    }

    public static string ParseTemplate(string template, int decimals, Dictionary<string, List<Entity>> cachedEntities, bool consoleCommand) {
        List<Entity> GetCachedOrFindEntities(Type type, string entityId, Dictionary<string, List<Entity>> dictionary) {
            string entityText = $"{type.FullName}{entityId}";
            List<Entity> entities;
            if (dictionary.TryGetValue(entityText, out List<Entity> value)) {
                entities = value;
            } else {
                entities = FindEntities(type, entityId);
                dictionary[entityText] = entities;
            }

            return entities;
        }

        string result = BraceRegex.Replace(template, match => {
            string matchText = match.Groups[1].Value;

            if (!TryParseMemberNames(matchText, out string typeText, out List<string> memberNames, out string errorMessage)) {
                return errorMessage;
            }

            if (!TryParseTypes(typeText, out List<Type> types, out string entityId, out errorMessage)) {
                return errorMessage;
            }

            string lastMemberName = memberNames.Last();
            string lastCharacter = lastMemberName.Substring(lastMemberName.Length - 1, 1);
            if (lastCharacter is ":" or "=") {
                lastMemberName = lastMemberName.Substring(0, lastMemberName.Length - 1).Trim();
                memberNames[memberNames.Count - 1] = lastMemberName;
            }

            string helperMethod = "";
            if (HelperMethods.ContainsKey(lastMemberName)) {
                helperMethod = lastMemberName;
                memberNames = memberNames.SkipLast().ToList();
            }

            bool moreThanOneEntity = types.Where(type => type.IsSameOrSubclassOf(typeof(Entity)))
                .SelectMany(type => GetCachedOrFindEntities(type, entityId, cachedEntities)).Count() > 1;

            List<string> result = types.Select(type => {
                if (memberNames.IsNotEmpty() && (
                        type.GetGetMethod(memberNames.First()) is {IsStatic: true} ||
                        type.GetFieldInfo(memberNames.First()) is {IsStatic: true} ||
                        (MethodRegex.Match(memberNames.First()) is {Success: true} match &&
                         type.GetMethodInfo(match.Groups[1].Value) is {IsStatic: true})
                    )) {
                    return FormatValue(GetMemberValue(type, null, memberNames), helperMethod, decimals);
                }

                if (Engine.Scene is Level level) {
                    if (type.IsSameOrSubclassOf(typeof(Entity))) {
                        List<Entity> entities = GetCachedOrFindEntities(type, entityId, cachedEntities);

                        if (entities == null) {
                            return "Ignore NPE Warning";
                        }

                        return string.Join("", entities.Select(entity => {
                            string value = FormatValue(GetMemberValue(type, entity, memberNames), helperMethod, decimals);

                            if (moreThanOneEntity) {
                                if (entity.GetEntityData()?.ToEntityId().ToString() is { } id) {
                                    value = $"\n[{id}] {value}";
                                } else {
                                    value = $"\n{value}";
                                }
                            }

                            return value;
                        }));
                    } else if (type == typeof(Level)) {
                        return FormatValue(GetMemberValue(type, level, memberNames), helperMethod, decimals);
                    } else if (type == typeof(Session)) {
                        return FormatValue(GetMemberValue(type, level.Session, memberNames), helperMethod, decimals);
                    } else {
                        return $"Instance of {type.FullName} not found";
                    }
                }

                return string.Empty;
            }).Where(s => s.IsNotNullOrEmpty()).ToList();

            string prefix = lastCharacter switch {
                "=" => matchText,
                ":" => $"{matchText} ",
                _ => ""
            };

            string separator = types.First().IsSameOrSubclassOf(typeof(Entity)) ? "" : " ";
            if (consoleCommand && separator.IsEmpty() && result.IsNotEmpty()) {
                result[0] = result[0].TrimStart();
            }

            return $"{prefix}{string.Join(separator, result)}";
        });

        return LuaRegex.Replace(result, match => {
            if (EnforceLegal) {
                return "Evaluate lua code is illegal when enforce legal";
            }

            string code = match.Groups[1].Value;
            object[] objects = EvalLuaCommand.EvalLuaImpl(code);
            return objects == null ? "null" : string.Join(", ", objects.Select(o => o?.ToString() ?? "null"));
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
        TryParseTypes(text, out List<Type> types, out entityId, out errorMessage);

        if (types.IsEmpty()) {
            type = null;
            return false;
        } else {
            type = types.First();
            return true;
        }
    }

    public static bool TryParseTypes(string text, out List<Type> types) {
        return TryParseTypes(text, out types, out _, out _);
    }

    public static bool TryParseTypes(string text, out List<Type> types, out string entityId) {
        return TryParseTypes(text, out types, out entityId, out _);
    }

    public static bool TryParseTypes(string text, out List<Type> types, out string entityId, out string errorMessage) {
        types = new List<Type>();
        entityId = "";
        errorMessage = "";

        if (!TryParseTypeName(text, out string typeNameMatched, out string typeNameWithAssembly, out entityId)) {
            errorMessage = "parsing type name failed";
            return false;
        }

        if (CachedParsedTypes.Keys.Contains(typeNameWithAssembly)) {
            types = CachedParsedTypes[typeNameWithAssembly];
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

            // one type can correspond to two keys (..@assemblyName and ..@modName), so we need Distinct<Type>()
            types = matchTypeNames.Select(name => AllTypes[name]).Distinct<Type>().ToList();
            CachedParsedTypes[typeNameWithAssembly] = types;
        }

        if (types.IsEmpty()) {
            errorMessage = $"{typeNameMatched} not found";
            return false;
        } else {
            return true;
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

    public static object GetMemberValue(Type type, object obj, List<string> memberNames, bool setCommand = false) {
        foreach (string memberName in memberNames) {
            if (type.GetGetMethod(memberName) is { } getMethodInfo) {
                if (getMethodInfo.IsStatic) {
                    obj = getMethodInfo.Invoke(null, null);
                } else if (obj != null) {
                    if (obj is Actor actor && memberName == "ExactPosition") {
                        obj = actor.GetMoreExactPosition(true);
                    } else if (obj is Platform platform && memberName == "ExactPosition") {
                        obj = platform.GetMoreExactPosition(true);
                    } else {
                        obj = getMethodInfo.Invoke(obj, null);
                    }
                }
            } else if (type.GetFieldInfo(memberName) is { } fieldInfo) {
                if (fieldInfo.IsStatic) {
                    obj = fieldInfo.GetValue(null);
                } else if (obj != null) {
                    obj = setCommand switch {
                        true when obj is Actor actor && memberName == "Position" => actor.GetMoreExactPosition(true),
                        true when obj is Platform platform && memberName == "Position" => platform.GetMoreExactPosition(true),
                        _ => fieldInfo.GetValue(obj)
                    };
                }
            } else if (MethodRegex.Match(memberName) is {Success: true} match && type.GetMethodInfo(match.Groups[1].Value) is { } methodInfo) {
                if (EnforceLegal) {
                    return $"{memberName}: Calling methods is illegal when enforce legal";
                } else if (match.Groups[2].Value.IsNotNullOrWhiteSpace() || methodInfo.GetParameters().Length > 0) {
                    return $"{memberName}: Only method without parameters is supported";
                } else if (methodInfo.ReturnType == typeof(void)) {
                    return $"{memberName}: Method return void is not supported";
                } else if (methodInfo.IsStatic) {
                    obj = methodInfo.Invoke(null, null);
                } else if (obj != null) {
                    if (obj is Actor actor && memberName == "get_ExactPosition()") {
                        obj = actor.GetMoreExactPosition(true);
                    } else if (obj is Platform platform && memberName == "get_ExactPosition()") {
                        obj = platform.GetMoreExactPosition(true);
                    } else {
                        obj = methodInfo.Invoke(obj, null);
                    }
                }
            } else {
                if (obj == null) {
                    return $"{type.FullName}.{memberName} member not found";
                } else {
                    return $"{obj.GetType().FullName}.{memberName} member not found";
                }
            }

            if (obj == null) {
                return null;
            }

            type = obj.GetType();
        }

        return obj;
    }

    public static List<Entity> FindEntities(Type type, string entityId) {
        if (!Engine.Scene.Tracker.Entities.TryGetValue(type, out List<Entity> entities)) {
            entities = Engine.Scene.Entities.Where(entity => entity.GetType().IsSameOrSubclassOf(type)).ToList();
        }

        if (entityId.IsNullOrEmpty()) {
            return entities;
        } else {
            return entities.Where(entity => entity.GetEntityData()?.ToEntityId().ToString() == entityId).ToList();
        }
    }

    public static string FormatValue(object obj, string helperMethodName, int decimals) {
        if (obj == null) {
            return string.Empty;
        }

        bool invalidParameter = false;
        if (HelperMethods.TryGetValue(helperMethodName, out HelperMethod method)) {
            if (method(obj, decimals, out string formattedValue)) {
                return formattedValue;
            } else {
                invalidParameter = true;
            }
        }

        return $"{AutoFormatter(obj, decimals)}{(invalidParameter ? $",\n not a valid parameter of {helperMethodName}" : "")}";
    }

    public static bool HelperMethod_toFrame(object obj, int decimals, out string formattedValue) {
        if (obj is float floatValue) {
            formattedValue = GameInfo.ConvertToFrames(floatValue).ToString();
            return true;
        }

        formattedValue = "";
        return false;
    }

    public static bool HelperMethod_toPixelPerFrame(object obj, int decimals, out string formattedValue) {
        if (obj is float floatValue) {
            formattedValue = GameInfo.ConvertSpeedUnit(floatValue, SpeedUnit.PixelPerFrame).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (obj is Vector2 vector2) {
            formattedValue = GameInfo.ConvertSpeedUnit(vector2, SpeedUnit.PixelPerFrame).ToSimpleString(decimals);
            return true;
        }

        formattedValue = "";
        return false;
    }

    public static string AutoFormatter(object obj, int decimals) {
        if (obj is Vector2 vector2) {
            return vector2.ToSimpleString(decimals);
        }

        if (obj is Vector2Double vector2Double) {
            return vector2Double.ToSimpleString(decimals);
        }

        if (obj is float floatValue) {
            return floatValue.ToFormattedString(decimals);
        }

        if (obj is Scene) {
            return obj.ToString();
        }

        if (obj is Entity entity) {
            string id = entity.GetEntityData()?.ToEntityId().ToString() is { } value ? $"[{value}]" : "";
            return $"{entity}{id}";
        }

        if (obj is IEnumerable enumerable and not IEnumerable<char>) {
            bool compressed = enumerable is IEnumerable<Component> or IEnumerable<Entity>;
            string separator = compressed ? ",\n " : ", ";
            return IEnumerableToString(enumerable, separator, compressed);
        }

        if (obj is Collider collider) {
            return ColliderToString(collider);
        }

        return obj.ToString();
    }

    public static string IEnumerableToString(IEnumerable enumerable, string separator, bool compressed) {
        StringBuilder sb = new();
        if (!compressed) {
            foreach (object o in enumerable) {
                if (sb.Length > 0) {
                    sb.Append(separator);
                }

                sb.Append(o);
            }

            return sb.ToString();
        }

        Dictionary<string, int> keyValuePairs = new Dictionary<string, int>();
        foreach (object obj in enumerable) {
            string str = obj.ToString();
            if (keyValuePairs.ContainsKey(str)) {
                keyValuePairs[str]++;
            } else {
                keyValuePairs.Add(str, 1);
            }
        }

        foreach (string key in keyValuePairs.Keys) {
            if (sb.Length > 0) {
                sb.Append(separator);
            }

            if (keyValuePairs[key] == 1) {
                sb.Append(key);
            } else {
                sb.Append($"{key} * {keyValuePairs[key]}");
            }
        }

        return sb.ToString();
    }

    public static string ColliderToString(Collider collider, int iterationHeight = 1) {
        if (collider is Hitbox hitbox) {
            return $"Hitbox=[{hitbox.Left},{hitbox.Right}]×[{hitbox.Top},{hitbox.Bottom}]";
        }

        if (collider is Circle circle) {
            if (circle.Position == Vector2.Zero) {
                return $"Circle=radius {circle.Radius}";
            } else {
                return $"Circle=radius {circle.Radius}, offset {circle.Position}";
            }
        }

        if (collider is ColliderList list && iterationHeight > 0) {
            return "ColliderList: {" + string.Join("; ", list.colliders.Select(s => ColliderToString(s, iterationHeight - 1))) + "}";
        }

        return collider.ToString();
    }
}
