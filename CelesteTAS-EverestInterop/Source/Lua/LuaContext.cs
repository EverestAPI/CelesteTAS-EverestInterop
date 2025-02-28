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
    private const string Prologue = """
                                    const System typeof clr.System
                                    const Celeste typeof clr.Celeste
                                    const Monocle typeof clr.Monocle
                                    const Vector2 typeof clr.Microsoft.Xna.Framework.Vector2

                                    """;

    [Load]
    private static void Load() {
        // Remove '.IsPublic' check to allow accessing non-public members
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableField")!
            .OnHook(bool (Func<FieldInfo, bool, bool> _, FieldInfo fieldInfo, bool searchStatic) => fieldInfo.IsStatic == searchStatic);
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableMethod")!
            .OnHook(bool (Func<MethodInfo, bool, bool> _, MethodInfo methodInfo, bool searchStatic) => (methodInfo.CallingConvention & CallingConventions.VarArgs) == 0 && methodInfo.IsStatic == searchStatic);
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
    public static Result<LuaContext, string> Compile(string code, string name = "CelesteTAS_LuaContext") {
        var lua = new NeoLua.Lua();
        var chunkResult = CompileChunk(lua, code, name);
        if (chunkResult.Failure) {
            return Result<LuaContext, string>.Fail(chunkResult.Error);
        }
        var environment = lua.CreateEnvironment<LuaHelperEnvironment>();

        return Result<LuaContext, string>.Ok(new LuaContext(lua, chunkResult, environment));
    }

    /// Compiles Lua text code into an executable chunk
    public static Result<NeoLua.LuaChunk, string> CompileChunk(NeoLua.Lua lua, string code, string name = "CelesteTAS_LuaContext") {
        try {
            var chunk = lua.CompileChunk(Prologue + code, name, new NeoLua.LuaCompileOptions { DebugEngine = NeoLua.LuaExceptionDebugger.Default } );
            return Result<NeoLua.LuaChunk, string>.Ok(chunk);
        } catch (NeoLua.LuaException ex) {
            ex.LogException("Lua compilation error");
            return Result<NeoLua.LuaChunk, string>.Fail(ex.Message); // Stacktrace isn't useful for the user
        } catch (Exception ex) {
            return Result<NeoLua.LuaChunk, string>.Fail($"Unexpected error: {ex.Message}");
        }
    }

    /// Executes the compiled Lua code
    public Result<IEnumerable<object?>, (string Message, string? Stacktrace)> Execute() => ExecuteChunk(chunk, environment);

    /// Executes the compiled Lua code
    public static Result<IEnumerable<object?>, (string Message, string? Stacktrace)> ExecuteChunk(NeoLua.LuaChunk chunk, NeoLua.LuaTable environment) {
        try {
            var result = chunk.Run(environment, null);

            // Flatten returned collection
            if (result.Count == 1 && result[0] != null && (result[0].GetType().IsArray || result[0].GetType().IsAssignableTo(typeof(IList)))) {
                return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Ok((IEnumerable<object?>) result[0]);
            }

            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Ok(result.Values);
        } catch (NeoLua.LuaException ex) {
            ex.LogException("Lua execution error");
            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Fail((ex.Message, ex.StackTrace));
        } catch (Exception ex) {
            return Result<IEnumerable<object?>, (string Message, string? Stacktrace)>.Fail(($"Unexpected error: {ex.Message}", ex.StackTrace));
        }
    }
}
