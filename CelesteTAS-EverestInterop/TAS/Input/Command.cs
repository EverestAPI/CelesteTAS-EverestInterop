using Celeste.Mod;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.Input.Commands;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input;

#nullable enable

public interface ITasCommand {
    [Flags]
    public enum ExecuteTiming {
        Parse = 1,
        Runtime = 2
    }

    public static abstract string Name { get; }
    public static virtual string[] Aliases => [];
    public static virtual bool CalcChecksum => true;
    public static virtual bool LegalInFullGame => true;
    public static virtual ExecuteTiming Timing => ExecuteTiming.Runtime;

    public static abstract void Execute(CommandLine commandLine, int studioLine, string filePath, int fileLine);
}

/// There are no instances of the ITasCommand interface, as everything is static
/// This collects the information of a single implementation into an object
public readonly record struct TasCommandInfo(
    string Name,
    string[] Aliases,
    bool CalcChecksum,
    bool LegalInFullGame,
    ITasCommand.ExecuteTiming Timing,

    MethodInfo m_Execute
) {
    public void Execute(CommandLine commandLine, int studioLine, string filePath, int fileLine) => m_Execute.Invoke(null, [commandLine, studioLine, filePath, fileLine]);

    public bool IsName(string name) {
        return Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) ||
               Aliases.Any(alias => alias.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }

    public static TasCommandInfo Parse(Type type) => new() {
        Name = (string)type.GetProperty(nameof(ITasCommand.Name), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,
        Aliases = (string[])type.GetProperty(nameof(ITasCommand.Aliases), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,
        CalcChecksum = (bool)type.GetProperty(nameof(ITasCommand.CalcChecksum), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,
        LegalInFullGame = (bool)type.GetProperty(nameof(ITasCommand.LegalInFullGame), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,
        Timing = (ITasCommand.ExecuteTiming)type.GetProperty(nameof(ITasCommand.Timing), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!,

        m_Execute = type.GetMethod(nameof(ITasCommand.Execute), BindingFlags.Public | BindingFlags.Static)!,
    };
}

public readonly record struct Command(
    CommandLine CommandLine,
    TasCommandInfo Info,

    string FilePath,
    int FileLine,
    int StudioLine,
    int Frame
) {
    private const string DefaultSeparator = ", ";

    private static readonly List<TasCommandInfo> CommandInfos = [];
    public static bool Parsing { get; private set; }

    public void Invoke() => Info.Execute(CommandLine, StudioLine, FilePath, FileLine);
    public bool Is(string commandName) => CommandLine.IsCommand(commandName);

    public string[] Args => CommandLine.Arguments;
    public string LineText => CommandLine.Arguments.Length == 0 ? Info.Name : $"{Info.Name}{DefaultSeparator}{string.Join(DefaultSeparator, CommandLine.Arguments)}";

    public static bool TryParse(InputController inputController, string filePath, int fileLine, string lineText, int frame, int studioLine, out Command command) {
        command = default;

        if (!CommandLine.TryParse(lineText, out var commandLine)) {
            return false;
        }

        string error = $"""
                        Failed to parse command "{lineText.Trim()}" at line {fileLine} of the file "{filePath}"
                        """;
        try {
            var info = CommandInfos.FirstOrDefault(info => info.IsName(commandLine.Command));
            if (string.IsNullOrEmpty(info.Name)) {
                error.Log();
                return false;
            }

            if (info.Timing.Has(ITasCommand.ExecuteTiming.Parse)) {
                Parsing = true;
                info.Execute(commandLine, studioLine, filePath, fileLine);
                Parsing = false;
            }

            if (!inputController.Commands.TryGetValue(frame, out var commands)) {
                inputController.Commands[frame] = commands = new List<Command>();
            }
            commands.Add(command);

            return true;

            // if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
            //     string[] args = Split(lineText);
            //     string commandName = args[0];
            //
            //     KeyValuePair<TasCommandAttribute, MethodInfo> pair = TasCommandAttribute.FindMethod(commandName);
            //     if (pair.Key == null || pair.Value == null) {
            //         error.Log();
            //         return false;
            //     }
            //
            //     TasCommandAttribute attribute = pair.Key;
            //     MethodInfo method = pair.Value;
            //
            //     string[] commandArgs = args.Skip(1).ToArray();
            //
            //     List<Type> parameterTypes = method.GetParameters().Select(info => info.ParameterType).ToList();
            //     object[] parameters = parameterTypes.Count switch {
            //         4 => new object[] {commandArgs, studioLine, filePath, fileLine},
            //         3 => new object[] {commandArgs, studioLine, filePath},
            //         2 when parameterTypes[1] == typeof(int) => new object[] {commandArgs, studioLine},
            //         2 when parameterTypes[1] == typeof(string) => new object[] {commandArgs, lineText.Trim()},
            //         1 => new object[] {commandArgs},
            //         0 => EmptyParameters,
            //         _ => throw new ArgumentException()
            //     };
            //
            //     Action commandCall = () => method.Invoke(null, parameters);
            //     command = new(attribute, frame, commandCall, commandArgs, filePath, studioLine);
            //
            //     if (attribute.ExecuteTiming.Has(ExecuteTiming.Parse)) {
            //         Parsing = true;
            //         commandCall.Invoke();
            //         Parsing = false;
            //     }
            //
            //     if (!inputController.Commands.TryGetValue(frame, out List<Command> commands)) {
            //         inputController.Commands[frame] = commands = new List<Command>();
            //     }
            //     commands.Add(command);
            //
            //     return true;
            // }

            return false;
        } catch (Exception e) {
            e.LogException(error);
            return false;
        }
    }

    [Initialize]
    private static void CollectCommands() {
        CommandInfos.Clear();
        CommandInfos.AddRange(typeof(CelesteTasModule).Assembly.GetTypesSafe()
            .Where(type => type.IsSubclassOf(typeof(ITasCommand)))
            .Select(TasCommandInfo.Parse));
    }
}
