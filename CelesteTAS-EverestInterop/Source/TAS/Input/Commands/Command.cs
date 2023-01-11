using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TAS.Utils;

namespace TAS.Input.Commands;

public partial record Command {
    public readonly string[] Args;
    public readonly TasCommandAttribute Attribute;
    public readonly string FilePath;
    public readonly int Frame;
    public readonly int StudioLineNumber; // form zero
    public Action CommandCall { get; private set; }
    public object Data;

    private Command(TasCommandAttribute attribute, int frame, string[] args, string filePath, int studioLineNumber) {
        Attribute = attribute;
        Frame = frame;
        Args = args;
        FilePath = filePath;
        StudioLineNumber = studioLineNumber;
    }

    public string LineText => Args.Length == 0 ? Attribute.Name : $"{Attribute.Name}, {string.Join(", ", Args)}";

    public void Invoke() => CommandCall?.Invoke();
    public bool Is(string commandName) => Attribute.IsName(commandName);
}

public partial record Command {
    private static readonly object[] EmptyParameters = { };
    private static readonly Regex CheckSpaceRegex = new(@"^[^,]+?\s+[^,]", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);
    public static bool Parsing { get; private set; }

    private static string[] Split(string line) {
        string trimLine = line.Trim();
        // Determined by the first separator
        string[] args = CheckSpaceRegex.IsMatch(trimLine) ? SpaceRegex.Split(trimLine) : trimLine.Split(',');
        return args.Select(text => text.Trim()).ToArray();
    }

    public static bool TryParse(InputController inputController, string filePath, int fileLine, string lineText, int frame, int studioLine,
        out Command command) {
        command = null;
        try {
            if (!string.IsNullOrEmpty(lineText) && char.IsLetter(lineText[0])) {
                string[] args = Split(lineText);
                string commandName = args[0];

                KeyValuePair<TasCommandAttribute, MethodInfo> pair = TasCommandAttribute.FindMethod(commandName);
                if (pair.Equals(default)) {
                    return false;
                }

                MethodInfo method = pair.Value;
                TasCommandAttribute attribute = pair.Key;

                string[] commandArgs = args.Skip(1).ToArray();
                command = new(attribute, frame, commandArgs, filePath, studioLine);

                List<Type> parameterTypes = method.GetParameters().Select(info => info.ParameterType).ToList();
                object[] parameters = parameterTypes.Count switch {
                    4 => new object[] {commandArgs, studioLine, filePath, fileLine},
                    3 => new object[] {commandArgs, studioLine, filePath},
                    2 when parameterTypes[1] == typeof(int) => new object[] {commandArgs, studioLine},
                    2 when parameterTypes[1] == typeof(string) => new object[] {commandArgs, lineText.Trim()},
                    2 when parameterTypes[1] == typeof(Command) => new object[] {commandArgs, command},
                    1 => new object[] {commandArgs},
                    0 => EmptyParameters,
                    _ => throw new ArgumentException()
                };

                command.CommandCall = () => method.Invoke(null, parameters);
                if (attribute.ExecuteTiming.HasFlag(ExecuteTiming.Parse)) {
                    Parsing = true;
                    command.Invoke();
                    Parsing = false;
                }

                if (!inputController.Commands.ContainsKey(frame)) {
                    inputController.Commands[frame] = new List<Command>();
                }

                inputController.Commands[frame].Add(command);

                return true;
            }

            return false;
        } catch (Exception e) {
            e.LogException("Failed to parse command.");
            return false;
        }
    }
}