namespace TAS.Input.Commands;

public static class EnforceLegalCommand {
    public static bool EnabledWhenRunning { get; private set; }
    public static bool EnabledWhenParsing { get; private set; }

    [TasCommand("EnforceLegal", AliasNames = new[] {"EnforceMainGame"}, ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void EnforceLegal() {
        if (Command.Parsing) {
            EnabledWhenParsing = true;
        } else {
            EnabledWhenRunning = true;
        }
    }

    [DisableRun]
    private static void DisableRun() {
        EnabledWhenRunning = false;
    }

    [ClearInputs]
    private static void ClearInputs() {
        EnabledWhenParsing = false;
    }
}