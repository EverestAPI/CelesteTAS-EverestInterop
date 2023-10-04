namespace TAS.Input.Commands;

public static class SafeCommand {
    // stop tas when out of Level/LevelLoader/LevelExit/Pico8/LevelEnter
    // stop tas when entering Options/ModOptions UI
    public static bool DisallowUnsafeInput { get; set; } = true;
    public static bool DisallowUnsafeInputParsing { get; private set; } = true;

    [TasCommand("Safe", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void Safe() {
        if (ParsingCommand) {
            DisallowUnsafeInputParsing = true;
        } else {
            DisallowUnsafeInput = true;       
        }
    }

    [TasCommand("Unsafe", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void Unsafe() {
        if (ParsingCommand) {
            DisallowUnsafeInputParsing = false;
        } else {
            DisallowUnsafeInput = false;       
        }
    }

    [DisableRun]
    private static void DisableRun() {
        DisallowUnsafeInput = true;
    }

    [ParseFileEnd]
    [ClearInputs]
    private static void Clear() {
        DisallowUnsafeInputParsing = true;
    }
}