global using NeoLua = Neo.IronLua;
using Celeste;
using Celeste.Mod;
using Monocle;
using MonoMod.Cil;
using System;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TAS.Module;
using TAS.Utils;
using OpCodes = Mono.Cecil.Cil.OpCodes;

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

        // In LuaEmit.MemberMatchInfo, the penalty constants don't match the comment and also don't make sense
        // This fixes those incorrect penalties to a more reasonable value
        var t_MemberMatchInfo = typeof(NeoLua.Lua).Assembly
            .GetType("Neo.IronLua.LuaEmit")!
            .GetNestedType("MemberMatchInfo`1", BindingFlags.NonPublic)!;

        foreach (var memberType in (ReadOnlySpan<Type>)[typeof(MemberInfo), typeof(FieldInfo), typeof(PropertyInfo), typeof(MethodInfo)]) {
            t_MemberMatchInfo
                .MakeGenericType(memberType)
                .GetMethodInfo("SetMatch")!
                // _Technically_ this isn't officially supported.. but it works and that's all i care about :catsnug:
                .IlHook((cursor, _) => {
                    ILLabel[]? targets = [];
                    cursor.GotoNext(instr => instr.MatchSwitch(out targets));

                    // Change MemberMatchValue.GenericMatch to 1
                    cursor.Goto(targets[1].Target);
                    cursor.Next!.OpCode = OpCodes.Ldc_I4_1;

                    // Change MemberMatchValue.AssignableMatch to 1
                    cursor.Goto(targets[2].Target);
                    cursor.Next!.OpCode = OpCodes.Ldc_I4_1;

                    // Change MemberMatchValue.ArraySplatting to 3
                    cursor.Goto(targets[4].Target);
                    cursor.Next!.OpCode = OpCodes.Ldc_I4_3;

                    Console.WriteLine(cursor.Context);
                });
        }
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
