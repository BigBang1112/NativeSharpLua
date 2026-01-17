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

    public void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        LuaObjectRegistry.CreateMetatable(state, type);
    }

    public void RegisterType<T>()
    {
        RegisterType(typeof(T));
    }

    public void RegisterObject(object obj, string name)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentException.ThrowIfNullOrEmpty(name);

        LuaObjectRegistry.CreateObjectUserData(state, obj);
        LuaC.lua_setglobal(state, name);
    }
}
