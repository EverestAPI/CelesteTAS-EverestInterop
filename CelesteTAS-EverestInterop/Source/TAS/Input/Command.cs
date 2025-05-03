using Celeste.Mod;
using Celeste.Mod.Helpers;
using JetBrains.Annotations;
using StudioCommunication;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TAS.Communication;
using TAS.Module;
using TAS.Utils;

namespace TAS.Input;

[Flags]
public enum ExecuteTiming : byte {
    /// Executes the command while parsing inputs, like Read commands
    Parse = 1 << 0,
    /// Executes the command at runtime while playing inputs, like Console commands
    Runtime = 1 << 1,
}

/// Creates a command which can be used inside TAS files
/// The signature of the target method **must** match <see cref="Execute"/>
[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
public class TasCommandAttribute(string name) : Attribute {
    /// Name of this command inside the TAS file
    public readonly string Name = name;
    /// Alternative names which are also recognized as this command
    public string[] Aliases = [];

    /// Whether this command affects the TAS or is purely informational
    public bool CalcChecksum = true;
    /// Whether this command changes the game in ways which are illegal outside of testing starts
    public bool LegalInFullGame = true;
    /// Timing when this command should be executed
    public ExecuteTiming ExecuteTiming = ExecuteTiming.Runtime;

    /// Optional type which implements the <see cref="ITasCommandMeta"/> interface,
    /// to provide additional metadata about the command for Studio
    public Type? MetaDataProvider = null;
    internal ITasCommandMeta? MetaData = null;

    internal MethodInfo m_Execute = null!;
    public void Execute(CommandLine commandLine, int studioLine, string filePath, int fileLine) {
        m_Execute.Invoke(null, [commandLine, studioLine, filePath, fileLine]);
    }

#if DEBUG
    internal void Validate() {
        $"Validating command '{Name}'...".Log(LogLevel.Debug);
        var executeMethod = typeof(TasCommandAttribute).GetMethodInfo(nameof(Execute))!;
        Debug.Assert(m_Execute != null);
        Debug.Assert(m_Execute.GetParameters().Length == executeMethod.GetParameters().Length);
        for (int i = 0; i < m_Execute.GetParameters().Length; i++) {
            Debug.Assert(m_Execute.GetParameters()[i].ParameterType == executeMethod.GetParameters()[i].ParameterType);
        }

        if (MetaDataProvider != null) {
            Debug.Assert(typeof(ITasCommandMeta).IsAssignableFrom(MetaDataProvider));
        }
    }
#endif

    public bool IsName(string name) {
        return Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) ||
               Aliases.Any(alias => alias.Equals(name, StringComparison.InvariantCultureIgnoreCase));
    }
}

/// Represents a fully parsed command-line in a TAS file
public readonly record struct Command(
    CommandLine CommandLine,
    TasCommandAttribute Attribute,

    string FilePath,
    int FileLine,
    int StudioLine,
    int Frame
) {
    private const string DefaultSeparator = ", ";

    private static readonly List<TasCommandAttribute> Commands = [];
    public static bool Parsing { get; private set; }

    public void Invoke() => Attribute.Execute(CommandLine, StudioLine, FilePath, FileLine);
    public bool Is(string commandName) => Attribute.IsName(commandName);

    public string[] Args => CommandLine.Arguments;
    public string LineText => CommandLine.Arguments.Length == 0 ? Attribute.Name : $"{Attribute.Name}{DefaultSeparator}{string.Join(DefaultSeparator, CommandLine.Arguments)}";

    public static bool TryParse(string filePath, int fileLine, string lineText, int frame, int studioLine, out Command command) {
        command = default;

        if (string.IsNullOrWhiteSpace(lineText) || !char.IsLetter(lineText[0]) || !CommandLine.TryParse(lineText, out var commandLine)) {
            return false;
        }

        string error = $"""
                        Failed to parse command "{lineText.Trim()}" at line {fileLine} of the file "{filePath}"
                        """;
        try {
            var info = Commands.FirstOrDefault(info => info.IsName(commandLine.Command));
            if (info == null) {
                error.Log();
                return false;
            }

            command = new Command(commandLine, info, filePath, fileLine, studioLine, frame);
            return true;
        } catch (Exception e) {
            e.LogException(error);
            return false;
        }
    }

    /// Invokes the command's target method if it should generate inputs
    public void Setup() {
        if (Attribute.ExecuteTiming.Has(ExecuteTiming.Parse)) {
            Parsing = true;
            Attribute.Execute(CommandLine, StudioLine, FilePath, FileLine);
            Parsing = false;
        }
    }

    [Initialize]
    private static void CollectCommands() {
        // TODO: Re-collect commands if mod is loaded late (i.e. through installing dependencies)
        Commands.Clear();
        Commands.AddRange(FakeAssembly.GetFakeEntryAssembly().GetTypesSafe()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<TasCommandAttribute>() != null)
            .Select(method => {
                var attr = method.GetCustomAttribute<TasCommandAttribute>()!;
                attr.m_Execute = method; // Bind execution method
#if DEBUG
                attr.Validate();
#endif
                if (attr.MetaDataProvider != null) {
                    attr.MetaData = (ITasCommandMeta?)Activator.CreateInstance(attr.MetaDataProvider);
                }
                return attr;
            }));

        CommunicationWrapper.SendCommandList();
    }

    internal static ITasCommandMeta? GetMeta(string commandName) {
        return Commands.FirstOrDefault(command => command.IsName(commandName))?.MetaData;
    }

    internal static CommandInfo[] GetCommandList() =>
        Commands
            .Select(command => {
                var meta = command.MetaData;
                return new CommandInfo(command.Name, meta?.Insert ?? command.Name, meta?.HasArguments ?? false);
            })
            .ToArray();
}
