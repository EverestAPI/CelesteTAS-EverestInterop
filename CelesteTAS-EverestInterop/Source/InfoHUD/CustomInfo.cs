using Celeste;
using Monocle;
using System;
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

            (var results, bool success, string errorMessage) = TargetQuery.GetMemberValues(query);
            if (!success) {
                mainResult.Append(betweenText);
                mainResult.Append($"<{errorMessage}>");
                continue;
            }

            bool appendedMainResult = false; // Prevent appending betweenText multiple times

            foreach ((object? value, object? baseInstance) in results) {
                var currResultType = baseInstance?.GetType();

                if (baseInstance is Entity entity && entity.GetEntityData()?.ToEntityId() is { } entityId) {
                    if (firstMatch && !appendedMainResult) {
                        mainResult.Append(betweenText);
                        appendedMainResult = true;
                    }

                    string result = $"{prefix}{value?.ToString() ?? "null"}";
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
                    mainResult.Append(value?.ToString() ?? "null");
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
}
