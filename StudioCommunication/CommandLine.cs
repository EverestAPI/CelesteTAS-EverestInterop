using System;
using System.Collections.Generic;
using System.Linq;
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
        string lineTrimmed = line.Trim();
        if (string.IsNullOrEmpty(lineTrimmed) || !char.IsLetter(lineTrimmed[0])) {
            commandLine = default;
            return false;
        }

        var separatorMatch = SeparatorRegex.Match(lineTrimmed);
        if (!separatorMatch.Success || separatorMatch.Length == 0) {
            // No arguments
            commandLine = new CommandLine {
                Command = lineTrimmed,
                Arguments = [],

                Regions = [new Region(0, lineTrimmed.Length - 1)],

                OriginalText = lineTrimmed,
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

        for (int i = currentArgIndex; i < lineTrimmed.Length; i++) {
            string subLine = lineTrimmed[i..];

            if (groupStack.Count == 0 && subLine.StartsWith(separator)) {
                arguments.Add(currentArg.ToString());
                currentArg.Clear();
                regions.Add(new Region(currentArgIndex, i - 1));

                i += separator.Length - 1;
                currentArgIndex = i + 1;
                continue;
            }

            char curr = lineTrimmed[i];
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
                    if (i == lineTrimmed.Length - 1) {
                        // Invalid escape sequence
                        commandLine = default;
                        return false;
                    }

                    char next = lineTrimmed[++i];
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
        regions.Add(new Region(currentArgIndex, lineTrimmed.Length));

        commandLine = new CommandLine {
            Command = lineTrimmed[..separatorMatch.Index],
            Arguments = arguments.ToArray(),

            Regions = regions.ToArray(),

            OriginalText = lineTrimmed,
            ArgumentSeparator = separator,
        };
        return true;
    }

    public string Format(IEnumerable<CommandInfo> gameCommands, bool forceCasing, string? overrideSeparator) {
        string commandName;

        if (forceCasing) {
            var commandLine = this;

            if (gameCommands.FirstOrDefault(cmd => string.Equals(cmd.Name, commandLine.Command, StringComparison.OrdinalIgnoreCase)) is var command
                && !string.IsNullOrEmpty(command.Name)
            ) {
                commandName = command.Name;
            } else if (CommandInfo.CommandOrder.FirstOrDefault(cmdName => string.Equals(cmdName, commandLine.Command, StringComparison.OrdinalIgnoreCase)) is var name
                       && !string.IsNullOrEmpty(name)
            ) {
                commandName = name;
            } else {
                commandName = Command;
            }
        } else {
            commandName = Command;
        }

        if (CommandInfo.SpaceSeparatedCommands.Contains(Command, StringComparer.OrdinalIgnoreCase)) {
            overrideSeparator = " ";
        }

        string separator = overrideSeparator ?? ArgumentSeparator;

        // Wrap arguments in "" if necessary
        return string.Join(separator, [commandName, ..Arguments.Select(arg => {
            if (!arg.Contains(separator)
                || arg.StartsWith('[') && arg.EndsWith(']')
                || arg.StartsWith('{') && arg.EndsWith('}')
            ) {
                return arg;
            }

            return $"\"{arg}\"";
        })]);
    }

    public override string ToString() {
        return string.Join(ArgumentSeparator, [Command, ..Arguments]);
    }
}
