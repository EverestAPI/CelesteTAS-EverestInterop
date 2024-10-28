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
using TAS.Utils;

namespace TAS.InfoHUD;

#nullable enable

/// Handles parsing of custom Info HUD templates
public static class CustomInfo {

    private static readonly Regex TargetQueryRegex = new(@"\{(.*?)\}", RegexOptions.Compiled);

    /// Should return true if the value was successfully formatted, otherwise false
    private delegate bool ValueFormatter(object? value, int decimals, out string formattedValue);

    // Applies custom formatting to the result of a target-query
    private static readonly Dictionary<string, ValueFormatter> CustomFormatters = new() {
        { ".toFrame()", Formatter_toFrame },
        { ".toPixelPerFrame()", Formatter_toPixelPerFrame },
    };

    #region Parsing

    /// Parses lines of a custom Info HUD template into actual values for the current frame
    public static IEnumerable<string> ParseTemplate(IEnumerable<string> template, int decimals) {
        return template.SelectMany(line => ParseTemplateLine(line, decimals));
    }
    /// Parses a single line of a custom Info HUD template into actual values for the current frame
    public static IEnumerable<string> ParseTemplateLine(string templateLine, int decimals) {
        /* Replace single results inline and format an aligned list for multiple results
         * Example:
         *
         * JumpThruPos: {JumpThru.Position=} | {JumpThru.X=} : {Player.X} # {Level.Wind=} ; {JumpThru.Y=}
         *
         * JumpThruPos: # Level.Wind=400
         *     [9b:0] JumpThru.Position=3608.00, -2040.00  | JumpThru.X=3608.00  ; JumpThru.Y=-2040.00
         *     [9b:0] JumpThru.Position=36082.00, -2040.00 | JumpThru.X=36082.00 ; JumpThru.Y=-2040.00
         *     [1:0]  Player.X=3872.00
         *
         * JumpThruPos: JumpThru.Position=3608.00, -2040.00 | JumpThru.X=3608 : Player.X=3872.00 # Level-Wind=400 ; JumpThru.Y=-2040.00
         */

        StringBuilder mainResult = new();
        Dictionary<string, List<string>> entityResults = [];

        // Find target-queries
        Match? lastMatch = null;
        Type? lastResultType = null;
        foreach (Match match in TargetQueryRegex.Matches(templateLine)) {
            string betweenText;
            bool firstMatch = lastMatch == null;
            if (firstMatch) {
                betweenText = templateLine[..match.Index];
            } else {
                betweenText = templateLine[(lastMatch!.Index + lastMatch.Length)..match.Index];
            }
            lastMatch = match;

            string query = match.Groups[1].Value;

            // Ignore empty queries
            if (string.IsNullOrWhiteSpace(query)) {
                mainResult.Append(betweenText);
                continue;
            }

            // Find prefix
            string prefix;
            if (query[^1] == ':') {
                prefix = $"{query} "; // query: value
                query = query[..^1];
            } else if (query[^1] == '=') {
                prefix = query; // query=value
                query = query[..^1];
            } else {
                prefix = "";
            }

            // Find custom formatter
            ValueFormatter? formatter = null;
            foreach ((string name, var customFormatter) in CustomFormatters) {
                if (query.EndsWith(name)) {
                    query = query[..^name.Length];
                    formatter = customFormatter;
                }
            }

            (var results, bool success, string errorMessage) = TargetQuery.GetMemberValues(query);
            if (!success) {
                mainResult.Append(betweenText);
                mainResult.Append($"<{errorMessage}>");
                continue;
            }

            bool appendedMainResult = false; // Prevent appending betweenText multiple times

            foreach ((object? value, object? baseInstance) in results) {
                var currResultType = baseInstance?.GetType();

                if (formatter == null || !formatter(value, decimals, out string valueStr)) {
                    valueStr = DefaultFormatter(value, decimals);
                }

                if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                    if (firstMatch && !appendedMainResult) {
                        mainResult.Append(betweenText);
                        appendedMainResult = true;
                    }

                    string result = $"{prefix}{valueStr}";
                    entityResults.AddToKey($"{currResultType?.Name ?? ""}[{entityId}]", (firstMatch ? "" : betweenText) + result);
                } else {
                    if (appendedMainResult) {
                        mainResult.Append(", ");
                    } else {
                        // Trim start to ignore spacing for previous different matches
                        mainResult.Append(lastResultType == currResultType ? betweenText : betweenText.TrimStart());
                        appendedMainResult = true;
                    }

                    mainResult.Append(prefix);
                    mainResult.Append(valueStr);
                }

                lastResultType = currResultType;
            }
        }

        yield return mainResult.ToString();

        // Format entity lines
        var entityLines = entityResults.ToDictionary(
            entry => entry.Key,
            entry => new StringBuilder($"  {entry.Key} "));

        int resultIdx = 0;
        bool allDone = false;
        while (!allDone) {
            if (entityLines.Count > 0) {
                // Align all lines
                int maxLength = entityLines
                    .Select(entry => entry.Value.Length)
                    .Aggregate(Math.Max);
                foreach (var (_, entityLine) in entityLines) {
                    entityLine.Append(' ', maxLength - entityLine.Length);
                }
            }

            // Append next parameter
            allDone = true;
            foreach ((string entityKey, var results) in entityResults) {
                if (resultIdx >= results.Count) {
                    continue;
                }

                entityLines[entityKey].Append(results[resultIdx]);
                allDone = false;
            }

            resultIdx++;
        }

        foreach (var (_, entityLine) in entityLines) {
            yield return entityLine.ToString();
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
