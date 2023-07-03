namespace TAS.Input.Commands;

internal static class DummyCommands {
    [TasCommand("Author")]
    private static void Author() {}

    [TasCommand("FrameCount")]
    private static void FrameCount() {}

    [TasCommand("TotalRecordCount")]
    private static void TotalRecordCount() {}
}