global using NeoLua = Neo.IronLua;
using Monocle;
using System;
using MonoMod.RuntimeDetour;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TAS.Module;
using TAS.Utils;

namespace TAS.Lua;

/// Compiled Lua code which can be executed
internal class LuaContext : IDisposable {

    [Load]
    private static void Load() {
        // Remove '.IsPublic' check to allow accessing non-public members
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableField")!
            .OnHook(bool (Func<FieldInfo, bool, bool> _, FieldInfo fieldInfo, bool searchStatic) => fieldInfo.IsStatic == searchStatic);
        typeof(NeoLua.LuaType)
            .GetMethodInfo("IsCallableMethod")!
            .OnHook(bool (Func<MethodInfo, bool, bool> _, MethodInfo methodInfo, bool searchStatic) => methodInfo.IsStatic == searchStatic);
    }

    private readonly NeoLua.Lua lua;
    private readonly NeoLua.LuaChunk chunk;
    private readonly NeoLua.LuaTable environment;

    public LuaContext(string code) {
        lua = new NeoLua.Lua();
        lua.SetPropertyValue("PrintExpressionTree", Console.Out);
        chunk = lua.CompileChunk(code, "CelesteTAS_LuaContext", new NeoLua.LuaCompileOptions() );
        environment = lua.CreateEnvironment();
    }

    [MonocleCommand("test_lua", "")]
    public static void CmdTest() {
        string code = Engine.Commands.commandHistory[0]["test_lua ".Length..];
        using var ctx = new LuaContext(code);
        var result = ctx.chunk.Run(ctx.environment, null);

        result.Log("Lua", outputToCommands: true);
        foreach (var value in result.Values) {
            $"  - {value}".Log("Lua", outputToCommands: true);
        }
    }

    ~LuaContext() {
        Dispose();
    }
    public void Dispose() {
        GC.SuppressFinalize(this);

        lua.Dispose();
    }
}
