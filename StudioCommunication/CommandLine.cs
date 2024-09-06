using System;
using System.Text.RegularExpressions;

namespace StudioCommunication;

/// A parsed command line inside a TAS file
public struct CommandLine {
    // Matches against command or space or both as a separator
    public static readonly Regex SeparatorRegex = new(@"(?:\s+)|(?:\s*,\s*)", RegexOptions.Compiled);

    public string Command;
    public string[] Arguments;
    public string ArgumentSeparator;

    public bool IsCommand(string? command) => string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);

    public static CommandLine? Parse(string line) => TryParse(line, out var commandLine) ? commandLine : null;
    public static bool TryParse(string line, out CommandLine commandLine) {
        var separatorMatch = SeparatorRegex.Match(line);
        string[] split = line.Split(separatorMatch.Value);

        if (split.Length == 0) {
            commandLine = default;
            return false;
        }

        commandLine = new CommandLine {
            Command = split[0],
            Arguments = split[1..],
            ArgumentSeparator = separatorMatch.Value,
        };
        return true;
    }
}
