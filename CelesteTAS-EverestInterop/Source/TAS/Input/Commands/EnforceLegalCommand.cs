namespace TAS.Input.Commands;

public static class EnforceLegalCommand {
    public static bool Enabled { get; private set; }

    [TasCommand("EnforceLegal", AliasNames = new[] {"EnforceMainGame"})]
    private static void EnforceLegal() {
        Enabled = true;
    }

    [DisableRun]
    private static void DisableRun() {
        Enabled = false;
    }
}