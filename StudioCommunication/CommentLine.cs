using System.Text.RegularExpressions;

namespace StudioCommunication;

public static class CommentLine {

    /// Matches the standard room label format
    /// e.g. '#lvl_SomeName (1)'
    public static readonly Regex RoomLabelRegex = new(@"^#lvl_([^\(\)]*)(?:\s\((\d+)\))?$", RegexOptions.Compiled);

    /// A comment is considered a label, if it's a single # immediately followed by the label name
    /// For example: "#lvl_1", "#Start", "#cycle_a"
    /// The following cases are NOT labels: #", "##", " #Start", "# Start", "##Start", "## Start"
    public static bool IsLabel(string line) {
        return line.Length >= 2 &&
               line[0] == '#' &&
               char.IsLetter(line[1]);
    }
}
