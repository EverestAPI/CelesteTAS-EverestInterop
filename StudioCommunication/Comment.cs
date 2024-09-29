namespace StudioCommunication;

public class Comment {

    /// A comment is considered a label, if it's a single # immediately followed by the label name
    /// For example: "#lvl_1", "#Start", "#cycle_a"
    /// The following cases are NOT labels: #", "##", " #Start", "# Start", "##Start", "## Start"
    public static bool IsLabel(string line) {
        return line.Length >= 2 &&
               line[0] == '#' &&
               char.IsLetter(line[1]);
    }
}
