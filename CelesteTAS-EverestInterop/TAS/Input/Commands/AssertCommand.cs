using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Monocle;
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

    private const string spaceSeparator = @"\s+";
    private const string commaSeparator = @"\s*,\s*";
    public static bool Running { get; private set; }

    //  Assert, Condition, Expected, Actual
    [TasCommand("Assert", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime)]
    private static void Assert(string[] args, string lineText) {
        string prefix = $"\"Assert, {string.Join(", ", args)}\" failed\n";

        if (Command.Parsing) {
            if (args.IsEmpty()) {
                AbortTas($"{prefix}Lack of assert condition");
            } else if (!Enum.TryParse(args[0], true, out AssertCondition _)) {
                AbortTas($"{prefix}{args[0]} is not a valid assert condition");
            } else if (args.Length < 2) {
                AbortTas($"{prefix}Lack of expected value");
            } else if (args.Length < 3 || args[2].IsEmpty()) {
                AbortTas($"{prefix}Lack of actual value");
            }
        } else {
            string separator = Command.SpaceSeparatorRegex.IsMatch(lineText) ? spaceSeparator : commaSeparator;
            Regex regex = new($@"assert{separator}{Regex.Escape(args[0])}{separator}{Regex.Escape(args[1])}{separator}", RegexOptions.IgnoreCase);

            Enum.TryParse(args[0], true, out AssertCondition condition);
            string expected = args[1];
            Running = true;
            string actual = InfoCustom.ParseTemplate($"{regex.Replace(lineText, "")}", 0, new Dictionary<string, List<Entity>>(), false);
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