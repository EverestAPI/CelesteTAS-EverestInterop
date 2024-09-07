using System;
using System.Text.RegularExpressions;

namespace StudioCommunication;

/// A parsed command line inside a TAS file
public readonly record struct CommandLine(
    string Command,
    string[] Arguments,

    // Regions inside the text for each parameter.
    // Includes command name; excludes separator
    CommandLine.ArgumentRegion[] Regions,

    string OriginalText,
    string ArgumentSeparator
) {
    public readonly record struct ArgumentRegion(int StartIdx, int EndIdx);

    // Matches against command or space or both as a separator
    public static readonly Regex SeparatorRegex = new(@"(?:\s+)|(?:\s*,\s*)", RegexOptions.Compiled);

    public bool IsCommand(string? command) => string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);

    public static CommandLine? Parse(string line) => TryParse(line, out var commandLine) ? commandLine : null;
    public static bool TryParse(string line, out CommandLine commandLine) {
        var separatorMatch = SeparatorRegex.Match(line);
        string[] split = line.Split(separatorMatch.Value);

        if (split.Length == 0) {
            commandLine = default;
            return false;
        }

        var regions = new ArgumentRegion[split.Length];
        for (int i = 0, idx = 0; i < split.Length; i++) {
            regions[i] = new ArgumentRegion(idx, idx + split[i].Length - 1);
            idx += split[i].Length + separatorMatch.Length - 1;
        }

        commandLine = new CommandLine {
            Command = split[0],
            Arguments = split[1..],

            Regions = regions,

            OriginalText = line,
            ArgumentSeparator = separatorMatch.Value,
        };

        return true;
    }
}
