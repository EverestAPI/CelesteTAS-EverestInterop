using System;
using StudioCommunication;
using System.Collections.Generic;
using System.IO;
using TAS.InfoHUD;
using TAS.Utils;

namespace TAS.Input.Commands;

public static class AssertCommand {
    private const string CommandName = "Assert";
    private class Meta : ITasCommandMeta {
        public string Insert => $"{CommandName}{CommandInfo.Separator}[0;Condition]{CommandInfo.Separator}\"[1;Expected]\"{CommandInfo.Separator}\"[2;Actual]\"";
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

    public enum AssertCondition {
        Equal,     NotEqual,
        Contain,   NotContain,
        StartWith, NotStartWith,
        EndWith,   NotEndWith,
    }

    private readonly record struct AssertData(string Expected, InfoCustom.Template ActualTemplate, AssertCondition Condition, string? FailureMessage);

    private static readonly Dictionary<int, List<AssertData>> asserts = new();

    //  Assert, Condition, Expected, Actual, FailureMessage
    [TasCommand(CommandName, ExecuteTiming = ExecuteTiming.Parse | ExecuteTiming.Runtime, MetaDataProvider = typeof(Meta))]
    private static void Assert(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        string prefix = $"""
                         Assert '{Path.GetFileName(filePath)}' line {fileLine} failed

                         """;

        if (Command.Parsing) {
            string[] args = commandLine.Arguments;

            // Validate arguments
            if (args.IsEmpty()) {
                AbortTas($"{prefix}Lack of assert condition");
            }
            if (!Enum.TryParse<AssertCondition>(args[0], true, out var condition)) {
                AbortTas($"{prefix}{args[0]} is not a valid assert condition");
            }
            if (args.Length < 2 || args[1].IsEmpty()) {
                AbortTas($"{prefix}Lack of assert.Expected value");
            }
            if (args.Length < 3 || args[2].IsEmpty()) {
                AbortTas($"{prefix}Lack of actual value");
            }

            var template = InfoCustom.ParseTemplate(args[2]);
            asserts.AddToKey(Manager.Controller.CurrentParsingFrame, new AssertData(args[1], template, condition, args.Length >= 4 ? args[3] : null));
        } else {
            if (!asserts.TryGetValue(Manager.Controller.CurrentFrameInTas, out var currentAssets)) {
                return;
            }

            foreach (var assert in currentAssets) {
                string actual = InfoCustom.EvaluateTemplate(assert.ActualTemplate, 0, forceAllowCodeExecution: true);

                switch (assert.Condition) {
                    case AssertCondition.Equal: {
                        if (actual != assert.Expected) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected equal: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    }
                    case AssertCondition.NotEqual: {
                        if (actual == assert.Expected) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected not equal: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    }
                    case AssertCondition.Contain:
                        if (!actual.Contains(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected contain: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    case AssertCondition.NotContain:
                        if (actual.Contains(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected not contain: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    case AssertCondition.StartWith:
                        if (!actual.StartsWith(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected starts with: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    case AssertCondition.NotStartWith:
                        if (actual.StartsWith(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected not starts with: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    case AssertCondition.EndWith:
                        if (!actual.EndsWith(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected ends with: {assert.Expected}
                                                But was: {actual}
                                                """;
                            AbortTas($"{prefix}{failureMessage}", true, 4f);
                        }
                        break;
                    case AssertCondition.NotEndWith:
                        if (actual.EndsWith(assert.Expected)) {
                            string failureMessage = assert.FailureMessage ?? $"""
                                                Expected not ends with: {assert.Expected}
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
}
