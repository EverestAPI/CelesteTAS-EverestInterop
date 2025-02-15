using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TAS.EverestInterop;
using TAS.EverestInterop.Lua;
using TAS.Utils;

namespace TAS.InfoHUD;

/// Handles parsing of custom Info HUD templates
public static class InfoCustom {
    /* Example:
     *
     *     Template:
     * Player: {Player.X:}
     * Glider Data: X={Glider.X} Y={Glider.Y}
     * JumpThruPos: ||{JumpThru.Position=} | {JumpThru.X=} : {Player.X} # {Level.Wind=} ; {JumpThru.Y=}||
     *
     *     Result:
     * Player: 3872.00
     * Glider Data: X={[9c:3] 3872.00, [9c:5] 8273.00} Y={[9c:3] 2080.00, [9c:5] 802.00}
     * JumpThruPos: # Level.Wind=400
     *     JumpThru[9b:0] JumpThru.Position=3608.00, -2040.00  | JumpThru.X=3608.00  ; JumpThru.Y=-2040.00
     *     JumpThru[9b:0] JumpThru.Position=36082.00, -2040.00 | JumpThru.X=36082.00 ; JumpThru.Y=-2040.00
     *     Player[1:0]    3872.00
     */

    private static readonly Regex TargetQueryRegex = new(@"\{(.*?)\}", RegexOptions.Compiled);
    private static readonly Regex TableRegex = new(@"\|\|(.*?)\|\|", RegexOptions.Compiled);
    private static readonly Regex LuaRegex = new(@"\[\[(.+?)\]\]", RegexOptions.Compiled);

    /// Should return true if the value was successfully formatted, otherwise false
    private delegate bool ValueFormatter(object? value, int decimals, out string formattedValue);

    // Applies custom formatting to the result of a target-query
    private static readonly Dictionary<string, ValueFormatter> CustomFormatters = new() {
        { ".toFrame()", Formatter_toFrame },
        { ".toPixelPerFrame()", Formatter_toPixelPerFrame },
    };

    /// Returns the parsed info for the current template
    [Obsolete("Use InfoCustom.ParseTemplate() once and call InfoCustom.EvaluateTemplate() with the parsed components to render")]
    public static IEnumerable<string> ParseTemplate(IEnumerable<string> template, int decimals, bool forceAllowCodeExecution = false) {
        var components = ParseTemplate(string.Join('\n', template));
        return [EvaluateTemplate(components, decimals, forceAllowCodeExecution)];
    }

    #region Parsing

    public abstract record TemplateComponent;

    /// Raw text
    private record TextComponent(string Text) : TemplateComponent;
    /// Target-query
    private record QueryComponent(TargetQuery.Parsed Query, string Prefix, ValueFormatter? Formatter = null) : TemplateComponent;
    /// Lua code
    private record LuaComponent(string Code) : TemplateComponent;
    /// Table
    private record TableComponent : TemplateComponent {
        public int ComponentCount { get; set; } = 0;
    }

    /// Parses a custom Info HUD template into individual components, which can later be evaluated with <see cref="EvaluateTemplate"/>
    [PublicAPI]
    public static TemplateComponent[] ParseTemplate(string template) {
        List<TemplateComponent> components = [];
        PopulateComponents(template, components);

        return components.ToArray();

        static void PopulateComponents(string template, List<TemplateComponent> components) {
            Match? lastMatch = null;

            while (true) {
                int startIndex = lastMatch == null ? 0 : lastMatch.Index + lastMatch.Length;

                var nextQueryMatch = TargetQueryRegex.Match(template, startIndex);
                var nextLuaMatch = LuaRegex.Match(template, startIndex);
                var nextTableMatch = TableRegex.Match(template, startIndex);

                Match? currMatch = null;
                foreach (var match in (ReadOnlySpan<Match>)[nextQueryMatch, nextLuaMatch, nextTableMatch]) {
                    int currIdx = currMatch?.Index ?? int.MaxValue;
                    int nextIdx = match is { Success: true } ? match.Index : int.MaxValue;

                    if (nextIdx < currIdx) {
                        currMatch = match;
                    }
                }

                if (currMatch == null) {
                    break;
                }
                lastMatch = currMatch;

                // Text before match
                components.Add(new TextComponent(template[startIndex..currMatch.Index]));

                if (currMatch == nextQueryMatch) {
                    string query = currMatch.Groups[1].Value;

                    // Find query prefix
                    string queryPrefix;
                    if (query[^1] == ':') {
                        queryPrefix = $"{query} "; // query: value
                        query = query[..^1];
                    } else if (query[^1] == '=') {
                        queryPrefix = query; // query=value
                        query = query[..^1];
                    } else {
                        queryPrefix = "";
                    }

                    // Find custom formatter
                    ValueFormatter? formatter = null;
                    foreach ((string name, var customFormatter) in CustomFormatters) {
                        if (query.EndsWith(name)) {
                            query = query[..^name.Length];
                            formatter = customFormatter;
                        }
                    }

                    var parsedQuery = TargetQuery.Parsed.FromQuery(query);
                    if (parsedQuery.Failure) {
                        components.Add(new TextComponent($"{queryPrefix}<{parsedQuery.Error}>"));
                        continue;
                    }

                    components.Add(new QueryComponent(parsedQuery, queryPrefix, formatter));
                } else if (currMatch == nextLuaMatch) {
                    string code = currMatch.Groups[1].Value;
                    // TODO: Use NeoLua and JIT compile this
                    components.Add(new LuaComponent(code));
                } else if (currMatch == nextTableMatch) {
                    var table = new TableComponent();
                    components.Add(table);

                    int startComponentCount = components.Count + 1;
                    PopulateComponents(nextTableMatch.Groups[1].Value, components);
                    table.ComponentCount = components.Count - startComponentCount;
                }
            }

            int lastStartIndex = lastMatch == null ? 0 : lastMatch.Index + lastMatch.Length;
            if (lastStartIndex != template.Length) {
                components.Add(new TextComponent(template[lastStartIndex..]));
            }
        }
    }

    #endregion
    #region Formatting

    // Reused to reduce allocations
    private static readonly StringBuilder infoBuilder = new();

    /// Evaluates a parsed template into a string for the current values
    [PublicAPI]
    public static string EvaluateTemplate(TemplateComponent[] components, int decimals, bool forceAllowCodeExecution = false) {
        infoBuilder.Clear();

        // Format components
        for (int i = 0; i < components.Length; i++) {
            switch (components[i]) {
                case TextComponent text: {
                    infoBuilder.Append(text.Text);
                    continue;
                }

                case QueryComponent query: {
                    var result = query.Query.GetMemberValues(forceAllowCodeExecution);
                    if (result.Failure) {
                        infoBuilder.Append(query.Prefix);
                        infoBuilder.Append($"<{result.Error}>");
                        continue;
                    }

                    if (result.Value.Count == 0) {
                        infoBuilder.Append("<Not found>");
                        continue;
                    }
                    if (result.Value.Count == 1) {
                        if (query.Formatter == null || !query.Formatter(result.Value[0].Value, decimals, out string valueStr)) {
                            valueStr = DefaultFormatter(result.Value[0].Value, decimals);
                        }

                        infoBuilder.Append(query.Prefix);
                        infoBuilder.Append(valueStr);
                        continue;
                    }

                    infoBuilder.Append("{ ");
                    bool firstValue = true;
                    foreach ((object? value, object? baseInstance) in result.Value) {
                        if (!firstValue) {
                            infoBuilder.Append(", ");
                        }
                        firstValue = false;

                        if (query.Formatter == null || !query.Formatter(value, decimals, out string valueStr)) {
                            valueStr = DefaultFormatter(value, decimals);
                        }

                        if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                            infoBuilder.Append($"[{entityId}]");
                            infoBuilder.Append(query.Prefix);
                            infoBuilder.Append(valueStr);
                        } else {
                            infoBuilder.Append(query.Prefix);
                            infoBuilder.Append(valueStr);
                        }
                    }
                    infoBuilder.Append(" }");
                    continue;
                }

                case LuaComponent lua: {
                    if (TargetQuery.PreventCodeExecution && !forceAllowCodeExecution) {
                        infoBuilder.Append("<Cannot safely evaluate Lua code during EnforceLegal>");
                    }

                    object?[]? objects = EvalLuaCommand.EvalLuaImpl(lua.Code);
                    if (objects == null) {
                        infoBuilder.Append("null");
                    } else {
                        infoBuilder.AppendJoin(", ", objects.Select(o => o?.ToString() ?? "null"));
                    }
                    continue;
                }

                case TableComponent table: {
                    var resultBuilder = new StringBuilder();

                    // A table is a mapping of result types -> their query results (including prefix)
                    Dictionary<string, List<string>> tableResults = [];

                    // Don't show types if all result are of the same type
                    Type? firstResultType = null;
                    bool hideTypes = true;

                    string lastKey = string.Empty;

                    // Collect data
                    int endIdx = i + table.ComponentCount;
                    for (; i <= endIdx; i++) {
                        switch (components[i + 1]) {
                            case TextComponent text: {
                                resultBuilder.Append(text.Text);
                                continue;
                            }
                            case LuaComponent lua: {
                                if (TargetQuery.PreventCodeExecution && !forceAllowCodeExecution) {
                                    infoBuilder.Append("<Cannot safely evaluate Lua code during EnforceLegal>");
                                }

                                object?[]? objects = EvalLuaCommand.EvalLuaImpl(lua.Code);
                                if (objects == null) {
                                    resultBuilder.Append("null");
                                } else {
                                    resultBuilder.AppendJoin(", ", objects.Select(o => o?.ToString() ?? "null"));
                                }
                                continue;
                            }

                            case QueryComponent query: {
                                var result = query.Query.GetMemberValues(forceAllowCodeExecution);
                                if (result.Failure) {
                                    resultBuilder.Append(query.Prefix);
                                    resultBuilder.Append($"<{result.Error}>");
                                    tableResults.AddToKey("Error", resultBuilder.ToString());
                                    resultBuilder.Clear();

                                    continue;
                                }

                                string prefixText = resultBuilder.ToString();
                                resultBuilder.Clear();

                                foreach ((object? value, object? baseInstance) in result.Value) {
                                    var currResultType = baseInstance?.GetType();
                                    firstResultType ??= currResultType;

                                    if (firstResultType != currResultType) {
                                        hideTypes = false;
                                    }

                                    if (query.Formatter == null || !query.Formatter(value, decimals, out string valueStr)) {
                                        valueStr = DefaultFormatter(value, decimals);
                                    }

                                    string key = currResultType?.Name ?? "";
                                    if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                                        key += $"[{entityId}]";
                                    } else {
                                        hideTypes = false;
                                    }

                                    if (tableResults.TryGetValue(key, out var results)) {
                                        results.Add(prefixText + query.Prefix + valueStr);
                                    } else {
                                        tableResults[key] = [prefixText.TrimStart() + query.Prefix + valueStr];
                                    }

                                    lastKey = key;
                                }

                                continue;
                            }

                            case TableComponent: {
                                throw new UnreachableException();
                            }
                        }
                    }

                    if (tableResults.TryGetValue(lastKey, out var lastResults)) {
                        lastResults.Add(resultBuilder.ToString());
                    }
                    if (hideTypes && firstResultType != null) {
                        tableResults = tableResults.ToDictionary(entry => entry.Key[firstResultType.Name.Length..], entry => entry.Value);
                    }

                    // Format results
                    var lines = tableResults.ToDictionary(
                        entry => entry.Key,
                        entry => new StringBuilder($"  {entry.Key} "));

                    if (lines.Count == 0) {
                        infoBuilder.Append("<No table entries>");
                        continue;
                    }

                    int resultIdx = 0;
                    bool allDone = false;

                    while (!allDone) {
                        // Align all lines
                        int maxLength = lines
                            .Select(entry => entry.Value.Length)
                            .Aggregate(Math.Max);
                        foreach (var (_, line) in lines) {
                            line.Append(' ', maxLength - line.Length);
                        }

                        // Append next parameter
                        allDone = true;
                        foreach ((string key, var results) in tableResults) {
                            if (resultIdx >= results.Count) {
                                continue;
                            }

                            lines[key].Append(results[resultIdx]);
                            allDone = false;
                        }

                        resultIdx++;
                    }

                    foreach (var (_, line) in lines) {
                        infoBuilder.Append('\n'); infoBuilder.Append(line.ToString());
                    }

                    continue;
                }
            }
        }

        return infoBuilder.ToString();
    }

    /// Formats a value in seconds into frames
    private static bool Formatter_toFrame(object? value, int _, out string formattedValue) {
        if (value is float floatValue) {
            formattedValue = TAS.GameInfo.ConvertToFrames(floatValue).ToString();
            return true;
        }

        formattedValue = "";
        return false;
    }
    /// Formats a value in px/s into px/f
    private static bool Formatter_toPixelPerFrame(object? value, int decimals, out string formattedValue) {
        if (value is float floatValue) {
            formattedValue = TAS.GameInfo.ConvertSpeedUnit(floatValue, SpeedUnit.PixelPerFrame).ToFormattedString(decimals);
            return true;
        }
        if (value is Vector2 vectorValue) {
            formattedValue = TAS.GameInfo.ConvertSpeedUnit(vectorValue, SpeedUnit.PixelPerFrame).ToSimpleString(decimals);
            return true;
        }

        formattedValue = "";
        return false;
    }

    /// Fallback for when no specific formatter is applicable
    private static string DefaultFormatter(object? obj, int decimals) {
        switch (obj) {
            case string stringValue:
                return stringValue;
            case Vector2 vectorValue:
                return vectorValue.ToSimpleString(decimals);
            case Vector2Double vectorValue:
                return vectorValue.ToSimpleString(decimals);
            case float floatValue:
                return floatValue.ToFormattedString(decimals);
            case Scene sceneValue:
                return sceneValue.ToString() ?? "null";
            case Entity entity:
                string id = entity.GetEntityData()?.ToEntityId().ToString() is { } value ? $"[{value}]" : "";
                return $"{entity}{id}";
            case Collider collider:
                return ColliderToString(collider);
            case IEnumerable enumerable:
                bool compressed = enumerable is IEnumerable<Component> or IEnumerable<Entity>;
                return IEnumerableToString(enumerable, ", ", compressed);

            default:
                return obj?.ToString() ?? "null";
        }
    }

    /// Formats items of the IEnumerable, optionally compressing same values
    private static string IEnumerableToString(IEnumerable enumerable, string separator, bool compressed) {
        var builder = new StringBuilder();

        if (!compressed) {
            foreach (object value in enumerable) {
                if (builder.Length > 0) {
                    builder.Append(separator);
                }

                builder.Append(value);
            }

            return builder.ToString();
        }

        var valueOccurrences = new Dictionary<string, int>();
        foreach (object value in enumerable) {
            string str = value.ToString() ?? "null";
            if (!valueOccurrences.TryAdd(str, 1)) {
                valueOccurrences[str]++;
            }
        }

        foreach ((string key, int occurrences) in valueOccurrences) {
            if (builder.Length > 0) {
                builder.Append(separator);
            }

            if (occurrences == 1) {
                builder.Append(key);
            } else {
                builder.Append($"{key} * {occurrences}");
            }
        }

        return builder.ToString();
    }

    /// Formats a collider with its important values
    private static string ColliderToString(Collider collider, int iterationHeight = 1) {
        return collider switch {
            Hitbox hitbox => $"Hitbox=[{hitbox.Left},{hitbox.Right}]×[{hitbox.Top},{hitbox.Bottom}]",
            Circle circle => circle.Position == Vector2.Zero
                ? $"Circle=[Radius={circle.Radius}]"
                : $"Circle=[Radius={circle.Radius},Offset={circle.Position}]",
            ColliderList list => iterationHeight > 0
                ? "ColliderList: { " + string.Join("; ", list.colliders.Select(s => ColliderToString(s, iterationHeight - 1))) + " }"
                : "ColliderList: { ... }",

            _ => collider.ToString() ?? "null"
        };
    }

    #endregion
}
