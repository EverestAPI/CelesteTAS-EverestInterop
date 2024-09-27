using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StudioCommunication;

/// A parsed command line inside a TAS file
public readonly record struct CommandLine(
    string Command,
    string[] Arguments,

    // Regions inside the text for each parameter.
    // Includes command name; excludes separator
    CommandLine.Region[] Regions,

    string OriginalText,
    string ArgumentSeparator
) {
    public readonly record struct Region(int StartCol, int EndCol);

    // Matches against command or space or both as a separator
    private static readonly Regex SeparatorRegex = new(@"(?:\s+)|(?:\s*,\s*)", RegexOptions.Compiled);

    public bool IsCommand(string? command) => string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);

    public static CommandLine? Parse(string line) => TryParse(line, out var commandLine) ? commandLine : null;
    public static bool TryParse(string line, out CommandLine commandLine) {
        var separatorMatch = SeparatorRegex.Match(line);
        if (!separatorMatch.Success || separatorMatch.Length == 0) {
            // No arguments
            commandLine = new CommandLine {
                Command = line,
                Arguments = [],

                Regions = [new Region(0, line.Length - 1)],

                OriginalText = line,
                ArgumentSeparator = string.Empty,
            };
            return true;
        }

        string separator = separatorMatch.Value;
        List<string> arguments = [];
        List<Region> regions = [new Region(0, separatorMatch.Index - 1)];

        // Quotes (") and brackets ([]) need to be closed before the next argument can happen
        bool quoteOpen = false;
        int bracketCount = 0;

        StringBuilder currentArg = new();
        int currentArgIndex = separatorMatch.Index + separator.Length;

        for (int i = currentArgIndex; i < line.Length; i++) {
            string subLine = line[i..];

            if (!quoteOpen && bracketCount == 0 && subLine.StartsWith(separator)) {
                arguments.Add(currentArg.ToString());
                currentArg.Clear();
                regions.Add(new Region(currentArgIndex, i - 1));

                i += separator.Length - 1;
                currentArgIndex = i + 1;
                continue;
            }

            switch (line[i]) {
                case '"' when bracketCount == 0:
                    quoteOpen = !quoteOpen;
                    break;
                case '[':
                    bracketCount++;
                    currentArg.Append(line[i]);
                    break;
                case ']':
                    bracketCount--;
                    currentArg.Append(line[i]);
                    break;
                case '\\':
                    // Escape next char
                    if (i == line.Length - 1) {
                        // Invalid escape sequence
                        commandLine = default;
                        return false;
                    }

                    currentArg.Append(line[++i]);
                    break;

                default:
                    currentArg.Append(line[i]);
                    break;
            }
        }

        if (quoteOpen || bracketCount != 0) {
            // Invalid arguments
            commandLine = default;
            return false;
        }

        // Finish last argument
        arguments.Add(currentArg.ToString());
        regions.Add(new Region(currentArgIndex, line.Length));

        commandLine = new CommandLine {
            Command = line[..separatorMatch.Index],
            Arguments = arguments.ToArray(),

            Regions = regions.ToArray(),

            OriginalText = line,
            ArgumentSeparator = separator,
        };
        return true;
    }
}
