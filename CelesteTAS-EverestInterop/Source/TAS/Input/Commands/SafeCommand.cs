namespace TAS.Input.Commands;

public static class SafeCommand {
    // stop tas when out of Level/LevelLoader/LevelExit
    // stop tas when entering Options/ModOptions UI
    public static bool DisallowUnsafeInput { get; private set; } = true;

    [TasCommand("Safe")]
    private static void Safe() {
        DisallowUnsafeInput = true;
    }

    [TasCommand("Unsafe")]
    private static void Unsafe() {
        DisallowUnsafeInput = false;
    }

    [DisableRun]
    private static void DisableRun() {
        DisallowUnsafeInput = true;
    }
}