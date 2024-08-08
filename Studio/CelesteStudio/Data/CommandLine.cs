using System;
using System.Text.RegularExpressions;

namespace CelesteStudio.Data;

public struct CommandLine {
    // Matches against command or space or both as a separator
    public static readonly Regex SeparatorRegex = new(@"(?:\s+)|(?:\s*,\s*)", RegexOptions.Compiled);

    public string Command;
    public string[] Args;

    public bool IsCommand(string? command) => string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);
    
    public static CommandLine? Parse(string line) => TryParse(line, out var commandLine) ? commandLine : null;
    
    public static bool TryParse(string line, out CommandLine commandLine) {
        var separatorMatch = SeparatorRegex.Match(line);
        var split = line.Split(separatorMatch.Value);
        if (split.Length == 0) {
            commandLine = default;
            return false;
        }

        commandLine = new CommandLine {
            Command = split[0],
            Args = split[1..],
        };
        return true;
    }
}