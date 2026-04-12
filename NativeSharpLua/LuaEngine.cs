using static NativeSharpLua.LuaCTypes;

namespace NativeSharpLua;

public class LuaEngine
{
    private readonly lua_State state;

    public static readonly lua_CFunction DefaultPrintFunc = state =>
    {
        var n = LuaC.lua_gettop(state);

        for (var i = 1; i <= n; i++)
        {
            var s = LuaCAux.luaL_tolstring(state, i);

            Console.Write(s);

            if (i < n)
            {
                Console.Write(' ');
            }
        }

        Console.WriteLine();

        return 0;
    };

    public lua_CFunction PrintFunc { get; init; } = DefaultPrintFunc;

    public LuaObjectRegistry ObjectRegistry { get; }

    public LuaEngine() : this(LuaLibrary.None, LuaLibrary.None) { }

    public LuaEngine(LuaLibrary loadLibs, LuaLibrary preloadLibs)
    {
        state = LuaCAux.luaL_newstate();

        LuaCAux.luaL_openselectedlibs(state, (int)loadLibs, (int)preloadLibs);

        Register();

        ObjectRegistry = new LuaObjectRegistry(state);
    }

    private void Register()
    {
        LuaC.lua_register(state, "print", PrintFunc);
    }

    public void Run(string code)
    {
        var top = LuaC.lua_gettop(state);
        var statusCode = LuaCAux.luaL_dostring(state, code);

        ObjectRegistry.ThrowIfPendingException();

        if (statusCode != LuaStatusCode.Ok)
        {
            var msg = LuaC.lua_tostring(state) ?? "Unknown error";
            LuaC.lua_settop(state, top);
            throw new LuaException(msg);
        }

        LuaC.lua_settop(state, top);
    }

    public object? Eval(string code)
    {
        var results = EvalMultiple(code);
        return results.Length > 0 ? results[0] : null;
    }

    public object?[] EvalMultiple(string code)
    {
        var top = LuaC.lua_gettop(state);
        var statusCode = LuaCAux.luaL_dostring(state, code);

        ObjectRegistry.ThrowIfPendingException();

        if (statusCode != LuaStatusCode.Ok)
        {
            var msg = LuaC.lua_tostring(state) ?? "Unknown error";
            LuaC.lua_settop(state, top);
            throw new LuaException(msg);
        }

        var count = LuaC.lua_gettop(state) - top;
        var results = new object?[count];

        for (var i = 0; i < count; i++)
        {
            results[i] = ReadStackValue(state, top + 1 + i);
        }

        LuaC.lua_settop(state, top);
        return results;
    }

    private static object? ReadStackValue(lua_State state, int index)
    {
        return LuaC.lua_type(state, index) switch
        {
            LuaType.Nil => null,
            LuaType.Boolean => LuaC.lua_toboolean(state, index) != 0,
            LuaType.Number => ToIntegerOrDouble(state, index),
            LuaType.String => LuaC.lua_tostring(state, index),
            _ => null
        };
    }

#pragma warning disable CA1859
    private static object ToIntegerOrDouble(lua_State state, int index)
#pragma warning restore CA1859
    {
        var number = LuaC.lua_tonumber(state, index);

        if (number == (int)number)
        {
            return (int)number;
        }

        return number;
    }
}
