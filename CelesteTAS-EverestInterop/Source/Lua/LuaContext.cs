global using NeoLua = Neo.IronLua;
using Celeste;
using Monocle;
using System;
using MonoMod.RuntimeDetour;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TAS.Module;
using TAS.Utils;

namespace TAS.Lua;

/// Compiled Lua code which can be executed
internal readonly struct LuaContext : IDisposable {

    private static string EnvironmentCode = null!;

    [Load]
    private static void Load() {
        // Remove '.IsPublic' check to allow accessing non-public members
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableField")!
            .OnHook(bool (Func<FieldInfo, bool, bool> _, FieldInfo fieldInfo, bool searchStatic) => fieldInfo.IsStatic == searchStatic);
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableMethod")!
            .OnHook(bool (Func<MethodInfo, bool, bool> _, MethodInfo methodInfo, bool searchStatic) => (methodInfo.CallingConvention & CallingConventions.VarArgs) == 0 && methodInfo.IsStatic == searchStatic);

        using var envStream = typeof(CelesteTasModule).Assembly.GetManifestResourceStream("environment.lua")!;
        using var envReader = new StreamReader(envStream);

        EnvironmentCode = envReader.ReadToEnd();
    }

    private readonly NeoLua.Lua lua;
    private readonly NeoLua.LuaChunk chunk;
    private readonly NeoLua.LuaTable environment;

    private LuaContext(NeoLua.Lua lua, NeoLua.LuaChunk chunk, NeoLua.LuaTable environment) {
        this.lua = lua;
        this.chunk = chunk;
        this.environment = environment;
    }
    public void Dispose() {
        lua.Dispose();
    }

    /// Compiles Lua text code into an executable context
    public static Result<LuaContext, string> Compile(string code, string? name = null) {
        try {
            var lua = new NeoLua.Lua();
            var chunk = lua.CompileChunk(EnvironmentCode + code, name ?? "CelesteTAS_LuaContext", new NeoLua.LuaCompileOptions { DebugEngine = NeoLua.LuaExceptionDebugger.Default } );
            var environment = lua.CreateEnvironment();

            return Result<LuaContext, string>.Ok(new LuaContext(lua, chunk, environment));
        } catch (NeoLua.LuaException ex) {
            ex.LogException("Lua compilation error");
            return Result<LuaContext, string>.Fail(ex.Message); // Stacktrace isn't useful for the user
        } catch (Exception ex) {
            return Result<LuaContext, string>.Fail($"Unexpected error: {ex.Message}");
        }
    }

    /// Executes the compiled Lua code
    public Result<IEnumerable<object?>, (string Message, string? Stacktrace)> Execute() {
        try {
            var result = chunk.Run(environment, null);
            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Ok(
                result.Values
                    // Level is an IEnumerable<Entity>, but we don't want to format it like that
                .SelectMany(value => value is not Level && value is IEnumerable<object?> enumerable ? enumerable : [value]));
        } catch (NeoLua.LuaException ex) {
            ex.LogException("Lua execution error");
            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Fail((ex.Message, ex.StackTrace));
        } catch (Exception ex) {
            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Fail(($"Unexpected error: {ex.Message}", ex.StackTrace));
        }
    }
}
