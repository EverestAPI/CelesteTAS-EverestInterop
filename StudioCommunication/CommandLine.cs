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
        string lineTrimmed = line.TrimStart();
        if (string.IsNullOrEmpty(lineTrimmed) || !char.IsLetter(lineTrimmed[0])) {
            commandLine = default;
            return false;
        }

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

        // All quotes ("), braces ({}) and brackets ([]) need to be closed before the next argument can happen
        Stack<char> groupStack = new();

        StringBuilder currentArg = new();
        int currentArgIndex = separatorMatch.Index + separator.Length;

        for (int i = currentArgIndex; i < line.Length; i++) {
            string subLine = line[i..];

            if (groupStack.Count == 0 && subLine.StartsWith(separator)) {
                arguments.Add(currentArg.ToString());
                currentArg.Clear();
                regions.Add(new Region(currentArgIndex, i - 1));

                i += separator.Length - 1;
                currentArgIndex = i + 1;
                continue;
            }

            char curr = line[i];
            switch (curr) {
                case '"' when groupStack.Count == 0 || groupStack.Peek() == '"':
                    if (groupStack.Count > 0 && groupStack.Peek() == '"') {
                        groupStack.Pop();
                    } else {
                        groupStack.Push('"');
                    }
                    break;
                case '[':
                case '{':
                    groupStack.Push(curr);
                    currentArg.Append(curr);
                    break;
                case ']':
                    if (groupStack.Count > 0 && groupStack.Peek() == '[') {
                        groupStack.Pop();
                    } else {
                        // Unopened bracket
                        commandLine = default;
                        return false;
                    }
                    currentArg.Append(curr);
                    break;
                case '}':
                    if (groupStack.Count > 0 && groupStack.Peek() == '{') {
                        groupStack.Pop();
                    } else {
                        // Unopened brace
                        commandLine = default;
                        return false;
                    }
                    currentArg.Append(curr);
                    break;
                case '\\':
                    // Escape next char
                    if (i == line.Length - 1) {
                        // Invalid escape sequence
                        commandLine = default;
                        return false;
                    }

                    char next = line[++i];
                    switch (next) {
                        case 'n':
                            currentArg.Append('\n');
                            break;

                        default:
                            currentArg.Append(next);
                            break;
                    }
                    break;

                default:
                    currentArg.Append(curr);
                    break;
            }
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

    public override string ToString() {
        return string.Join(ArgumentSeparator, [Command, ..Arguments]);
    }
}
