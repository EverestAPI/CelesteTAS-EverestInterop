namespace TAS.Input.Commands;

internal static class DummyCommands {
    [TasCommand("Author:", CalcChecksum = false)]
    private static void Author() {}

    [TasCommand("FrameCount:", CalcChecksum = false)]
    private static void FrameCount() {}

    [TasCommand("TotalRecordCount:", CalcChecksum = false)]
    private static void TotalRecordCount() {}
}