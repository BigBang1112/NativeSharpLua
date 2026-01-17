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

    public LuaEngine() : this(LuaLibrary.None, LuaLibrary.None) { }

    public LuaEngine(LuaLibrary loadLibs, LuaLibrary preloadLibs)
    {
        state = LuaCAux.luaL_newstate();

        LuaCAux.luaL_openselectedlibs(state, (int)loadLibs, (int)preloadLibs);

        Register();
    }

    private void Register()
    {
        LuaC.lua_register(state, "print", PrintFunc);
    }

    public void Run(string code)
    {
        var statusCode = LuaCAux.luaL_dostring(state, code);

        if (statusCode != LuaStatusCode.Ok)
        {
            throw new LuaException(LuaC.lua_tostring(state) ?? "Unknown error");
        }
    }

    public bool NewMetatable(string name)
    {
        return LuaCAux.luaL_newmetatable(state, name) != 0;
    }

    /// <summary>
    /// Registers a C# object in Lua with the given name
    /// </summary>
    /// <param name="obj">The C# object to register</param>
    /// <param name="name">The global name in Lua</param>
    public void RegisterObject(object obj, string name)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        }

        LuaObjectRegistry.CreateObjectUserData(state, obj);
        LuaC.lua_setglobal(state, name);
    }

    /// <summary>
    /// Creates a metatable for the specified C# type
    /// </summary>
    /// <param name="type">The C# type to create a metatable for</param>
    public void RegisterType(Type type)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        LuaObjectRegistry.CreateMetatable(state, type);
    }

    /// <summary>
    /// Creates a metatable for the specified C# type
    /// </summary>
    /// <typeparam name="T">The C# type to create a metatable for</typeparam>
    public void RegisterType<T>()
    {
        RegisterType(typeof(T));
    }
}
