using System;
using StudioCommunication;
using System.Collections.Generic;
using System.IO;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class AssertCommand {
    private class Meta : ITasCommandMeta {
        public string Insert => $"Assert{CommandInfo.Separator}[0;Condition]{CommandInfo.Separator}\"[1;Expected]\"{CommandInfo.Separator}\"[2;Actual]\"";
        public bool HasArguments => true;

        public IEnumerator<CommandAutoCompleteEntry> GetAutoCompleteEntries(string[] args, string filePath, int fileLine) {
            if (args.Length != 1) {
                yield break;
            }

            foreach (var mode in Enum.GetValues<AssertCondition>()) {
                yield return new CommandAutoCompleteEntry { Name = mode.ToString(), Extra = "Condition", HasNext = true };
            }
        }
    }

    private enum AssertCondition {
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

    //  Assert, Condition, Expected, Actual, FailureMessage
    [TasCommand("Assert", ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    private static void Assert(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string[] args = commandLine.Arguments;
        string prefix = $"""
                         Assert '{Path.GetFileName(filePath)}' line {fileLine} failed

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
            string actualTemplate = args[2];
            string? failureMessage = args.Length >= 4 ? args[3] : null;

            Running = true;
            string actual = string.Join("\n", InfoCustom.ParseTemplateLine(actualTemplate, 0, forceAllowCodeExecution: true));
            Running = false;

            switch (condition) {
                case AssertCondition.Equal: {
                    if (actual != expected) {
                        failureMessage ??= $"""
                                            Expected equal: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                }
                case AssertCondition.NotEqual: {
                    if (actual == expected) {
                        failureMessage ??= $"""
                                            Expected not equal: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                }
                case AssertCondition.Contain:
                    if (!actual.Contains(expected)) {
                        failureMessage ??= $"""
                                            Expected contain: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                case AssertCondition.NotContain:
                    if (actual.Contains(expected)) {
                        failureMessage ??= $"""
                                            Expected not contain: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                case AssertCondition.StartWith:
                    if (!actual.StartsWith(expected)) {
                        failureMessage ??= $"""
                                            Expected starts with: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                case AssertCondition.NotStartWith:
                    if (actual.StartsWith(expected)) {
                        failureMessage ??= $"""
                                            Expected not starts with: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                case AssertCondition.EndWith:
                    if (!actual.EndsWith(expected)) {
                        failureMessage ??= $"""
                                            Expected ends with: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;
                case AssertCondition.NotEndWith:
                    if (actual.EndsWith(expected)) {
                        failureMessage ??= $"""
                                            Expected not ends with: {expected}
                                            But was: {actual}
                                            """;
                        AbortTas($"{prefix}{failureMessage}", true, 4f);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
