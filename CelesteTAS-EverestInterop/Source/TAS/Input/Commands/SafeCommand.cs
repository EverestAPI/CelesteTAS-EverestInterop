namespace TAS.Input.Commands;

public static class SafeCommand {
    public static bool AllowUnsafeInput { get; private set; }

    [TasCommand("Safe")]
    private static void Safe() {
        AllowUnsafeInput = false;
    }

    [TasCommand("Unsafe")]
    private static void Unsafe() {
        AllowUnsafeInput = true;
    }

    [DisableRun]
    private static void DisableRun() {
        AllowUnsafeInput = false;
    }
}