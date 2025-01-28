using Microsoft.Xna.Framework;
using Monocle;
using StudioCommunication;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TAS.EverestInterop;
using TAS.EverestInterop.Lua;
using TAS.Utils;
using StringExtensions = StudioCommunication.Util.StringExtensions;

namespace TAS.InfoHUD;

/// Handles parsing of custom Info HUD templates
public static class InfoCustom {
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
    public static string GetInfo(int? decimals = null, bool forceAllowCodeExecution = false) {
        return string.Join('\n', ParseTemplate(StringExtensions.SplitLines(TasSettings.InfoCustomTemplate), decimals ?? TasSettings.CustomInfoDecimals, forceAllowCodeExecution));
    }

    #region Parsing

    /// Parses lines of a custom Info HUD template into actual values for the current frame
    public static IEnumerable<string> ParseTemplate(IEnumerable<string> template, int decimals, bool forceAllowCodeExecution = false) {
        return template.SelectMany(line => ParseTemplateLine(line, decimals, forceAllowCodeExecution));
    }
    /// Parses a single line of a custom Info HUD template into actual values for the current frame
    public static IEnumerable<string> ParseTemplateLine(string templateLine, int decimals, bool forceAllowCodeExecution = false) {
        /* Replace single results inline and can format an aligned list for multiple results
         * Example:
         *
         *     Template:
         * JumpThruPos: ||{JumpThru.Position=} | {JumpThru.X=} : {Player.X} # {Level.Wind=} ; {JumpThru.Y=}|| ABC={Session.SID}
         * Glider Data: X={Glider.X} Y={Glider.Y}
         *
         *     Single Entity:
         * JumpThruPos: JumpThru.Position=3608.00, -2040.00 | JumpThru.X=3608 : 3872.00 # Level-Wind=400 ; JumpThru.Y=-2040.00
         * Glider Data: X=3872.00 Y=2080.00
         *
         *     Multiple Entities:
         * JumpThruPos: # Level.Wind=400
         *     [9b:0] JumpThru.Position=3608.00, -2040.00  | JumpThru.X=3608.00  ; JumpThru.Y=-2040.00
         *     [9b:0] JumpThru.Position=36082.00, -2040.00 | JumpThru.X=36082.00 ; JumpThru.Y=-2040.00
         *     [1:0]  3872.00
         * Glider Data: X={[9c:3] 3872.00, [9c:5] 8273.00} Y={[9c:3] 2080.00, [9c:5] 802.00}
         */

        // A table is a mapping of result types -> it's query results (including prefix)
        List<Dictionary<string, List<string>>> tables = [];

        // Find tables (and remove them to avoid duplicate results)
        templateLine = TableRegex.Replace(templateLine, tableMatch => {
            string tableQuery = tableMatch.Groups[1].Value;
            Dictionary<string, List<string>> tableResults = [];

            Type? firstResultType = null;
            bool hideTypes = true; // Don't show types if all result are of the same type

            Match? lastMatch = null;
            foreach (Match match in TargetQueryRegex.Matches(tableQuery)) {
                string prefixText; // Text before target-queries
                if (lastMatch == null) {
                    prefixText = tableQuery[..match.Index];
                } else {
                    prefixText = tableQuery[(lastMatch.Index + lastMatch.Length)..match.Index];
                }
                lastMatch = match;

                string query = match.Groups[1].Value;

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

                (var queryResults, bool success, string errorMessage) = TargetQuery.GetMemberValues(query, forceAllowCodeExecution);
                if (!success) {
                    tableResults.AddToKey("Error", $"{prefixText}{queryPrefix}<{errorMessage}>");
                    continue;
                }

                foreach ((object? value, object? baseInstance) in queryResults) {
                    var currResultType = baseInstance?.GetType();
                    firstResultType ??= currResultType;

                    if (firstResultType != currResultType) {
                        hideTypes = false;
                    }

                    if (formatter == null || !formatter(value, decimals, out string valueStr)) {
                        valueStr = DefaultFormatter(value, decimals);
                    }

                    string key = currResultType?.Name ?? "";
                    string result = $"{queryPrefix}{valueStr}";

                    if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                        key += $"[{entityId}]";
                    }

                    if (tableResults.TryGetValue(key, out var results)) {
                        results.Add(prefixText + result);
                    } else {
                        tableResults[key] = [prefixText.TrimStart() +result];
                    }
                }
            }

            if (hideTypes && firstResultType != null) {
                tableResults = tableResults.ToDictionary(entry => entry.Key[firstResultType.Name.Length..], entry => entry.Value);
            }

            tables.Add(tableResults);

            return string.Empty;
        });

        // Find main queries
        string mainResult = TargetQueryRegex.Replace(templateLine, match => {
            string query = match.Groups[1].Value;

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

            (var queryResults, bool success, string errorMessage) = TargetQuery.GetMemberValues(query, forceAllowCodeExecution);
            if (!success) {
                return $"{queryPrefix}<{errorMessage}>";
            }

            if (queryResults.Count == 0) {
                return "<Not found>";
            }
            if (queryResults.Count == 1) {
                if (formatter == null || !formatter(queryResults[0].Value, decimals, out string valueStr)) {
                    valueStr = DefaultFormatter(queryResults[0].Value, decimals);
                }

                return $"{queryPrefix}{valueStr}";
            }

            var resultCollection = new StringBuilder("{ ");
            bool firstValue = true;
            foreach ((object? value, object? baseInstance) in queryResults) {
                if (!firstValue) {
                    resultCollection.Append(", ");
                }
                firstValue = false;

                if (formatter == null || !formatter(value, decimals, out string valueStr)) {
                    valueStr = DefaultFormatter(value, decimals);
                }

                if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                    resultCollection.Append($"[{entityId}] {queryPrefix}{valueStr}");
                } else {
                    resultCollection.Append($"{queryPrefix}{valueStr}");
                }
            }
            resultCollection.Append(" }");

            return resultCollection.ToString();
        });

        // Evaluate Lua code for main line
        yield return LuaRegex.Replace(mainResult, match => {
            if (TargetQuery.PreventCodeExecution && !forceAllowCodeExecution) {
                return "<Cannot safely evaluate Lua code during EnforceLegal>";
            }

            string code = match.Groups[1].Value;
            object?[]? objects = EvalLuaCommand.EvalLuaImpl(code);
            return objects == null ? "null" : string.Join(", ", objects.Select(o => o?.ToString() ?? "null"));
        });

        // Format tables
        foreach (var table in tables) {
            var lines = table.ToDictionary(
                entry => entry.Key,
                entry => new StringBuilder($"  {entry.Key} "));

            if (lines.Count == 0) {
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
                foreach ((string key, var results) in table) {
                    if (resultIdx >= results.Count) {
                        continue;
                    }

                    lines[key].Append(results[resultIdx]);
                    allDone = false;
                }

                resultIdx++;
            }

            foreach (var (_, line) in lines) {
                yield return line.ToString();
            }
        }
    }

    #endregion
    #region Formatting

    /// Formats a value in seconds into frames
    private static bool Formatter_toFrame(object? value, int _, out string formattedValue) {
        if (value is float floatValue) {
            formattedValue = GameInfo.ConvertToFrames(floatValue).ToString();
            return true;
        }

        formattedValue = "";
        return false;
    }
    /// Formats a value in px/s into px/f
    private static bool Formatter_toPixelPerFrame(object? value, int decimals, out string formattedValue) {
        if (value is float floatValue) {
            formattedValue = GameInfo.ConvertSpeedUnit(floatValue, SpeedUnit.PixelPerFrame).ToFormattedString(decimals);
            return true;
        }
        if (value is Vector2 vectorValue) {
            formattedValue = GameInfo.ConvertSpeedUnit(vectorValue, SpeedUnit.PixelPerFrame).ToSimpleString(decimals);
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
