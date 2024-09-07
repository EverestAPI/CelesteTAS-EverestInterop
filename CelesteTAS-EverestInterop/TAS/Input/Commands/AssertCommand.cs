using System;
using StudioCommunication;
using TAS.EverestInterop.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class AssertCommand {
    enum AssertCondition {
        Equal,
        NotEqual,
        Contain,
        NotContain,
        StartWith,
        NotStartWith,
        EndWith,
        NotEndWith,
    }

    public static bool Running { get; private set; }

    //  Assert, Condition, Expected, Actual
    [TasCommand("Assert", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void Assert(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string prefix = $"""
                         "Assert, {string.Join(", ", args)}" failed

                         """;

        if (Command.Parsing) {
            // Validate arguments
            if (args.IsEmpty()) {
                AbortTas($"{prefix}Lack of assert condition");
            } else if (!Enum.TryParse<AssertCondition>(args[0], true, out _)) {
                AbortTas($"{prefix}{args[0]} is not a valid assert condition");
            } else if (args.Length < 2 || args[1].IsEmpty()) {
                AbortTas($"{prefix}Lack of expected value");
            } else if (args.Length < 3 || args[2].IsEmpty()) {
                AbortTas($"{prefix}Lack of actual value");
            }
        } else {
            var condition = Enum.Parse<AssertCondition>(args[0], ignoreCase: true); // Must succeed, since this was checked in Parse
            string expected = args[1];
            string actualTemplate = commandLine.OriginalText[commandLine.Regions[3].StartCol..];

            Running = true;
            string actual = InfoCustom.ParseTemplate(actualTemplate, 0, [], false);
            Running = false;

            switch (condition) {
                case AssertCondition.Equal: {
                    if (actual != expected) {
                        AbortTas($"{prefix}Expected: {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                }
                case AssertCondition.NotEqual: {
                    if (actual == expected) {
                        AbortTas($"{prefix}Expected: not equal to {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                }
                case AssertCondition.Contain:
                    if (!actual.Contains(expected)) {
                        AbortTas($"{prefix}Expected: contain {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                case AssertCondition.NotContain:
                    if (actual.Contains(expected)) {
                        AbortTas($"{prefix}Expected: not contain {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                case AssertCondition.StartWith:
                    if (!actual.StartsWith(expected)) {
                        AbortTas($"{prefix}Expected: start with {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                case AssertCondition.NotStartWith:
                    if (actual.StartsWith(expected)) {
                        AbortTas($"{prefix}Expected: not start with {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                case AssertCondition.EndWith:
                    if (!actual.EndsWith(expected)) {
                        AbortTas($"{prefix}Expected: end with {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                case AssertCondition.NotEndWith:
                    if (actual.EndsWith(expected)) {
                        AbortTas($"{prefix}Expected: not end with {expected}\nBut was: {actual}", true, 4f);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
