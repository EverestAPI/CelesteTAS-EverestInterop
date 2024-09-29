using StudioCommunication;

namespace TAS.Input.Commands;

internal static class DummyCommands {
    [TasCommand("Author:", CalcChecksum = false)]
    private static void Author(CommandLine commandLine, int studioLine, string filePath, int fileLine) { }

    [TasCommand("FrameCount:", CalcChecksum = false)]
    private static void FrameCount(CommandLine commandLine, int studioLine, string filePath, int fileLine) { }

    [TasCommand("TotalRecordCount:", CalcChecksum = false)]
    private static void TotalRecordCount(CommandLine commandLine, int studioLine, string filePath, int fileLine) { }
}
