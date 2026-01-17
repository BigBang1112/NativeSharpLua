using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using static NativeSharpLua.LuaCTypes;

namespace NativeSharpLua;

internal static class LuaObjectRegistry
{
    private static readonly ConcurrentDictionary<nint, object> objects = new();
    private static readonly ConcurrentDictionary<Type, string> typeNames = new();
    private static int nextId = 1;

    // Dictionary for efficient numeric type conversions
    private static readonly Dictionary<Type, Func<lua_State, int, object>> numericConverters = new()
    {
        [typeof(int)] = (state, index) => LuaC.lua_tointeger(state, index),
        [typeof(long)] = (state, index) => (long)LuaC.lua_tonumber(state, index),
        [typeof(float)] = (state, index) => (float)LuaC.lua_tonumber(state, index),
        [typeof(double)] = (state, index) => LuaC.lua_tonumber(state, index),
        [typeof(decimal)] = (state, index) => (decimal)LuaC.lua_tonumber(state, index),
        [typeof(byte)] = (state, index) => (byte)LuaC.lua_tointeger(state, index),
        [typeof(short)] = (state, index) => (short)LuaC.lua_tointeger(state, index),
        [typeof(uint)] = (state, index) => (uint)LuaC.lua_tointeger(state, index),
        [typeof(ulong)] = (state, index) => (ulong)LuaC.lua_tonumber(state, index),
        [typeof(ushort)] = (state, index) => (ushort)LuaC.lua_tointeger(state, index),
        [typeof(sbyte)] = (state, index) => (sbyte)LuaC.lua_tointeger(state, index)
    };

    private static string GetTypeName(Type type)
    {
        return typeNames.GetOrAdd(type, t => $"dotnet_{t.FullName?.Replace('.', '_')}");
    }

    public static object? GetObject(nint id)
    {
        return objects.TryGetValue(id, out var wrapper) ? wrapper : null;
    }

    private static object? GetManagedObject(IntPtr userDataPtr)
    {
        var objectId = Marshal.ReadIntPtr(userDataPtr);
        return GetObject(objectId);
    }

    public static nint RegisterObject(object obj)
    {
        var id = Interlocked.Increment(ref nextId);
        objects[id] = obj;
        return id;
    }

    public static bool UnregisterObject(nint id)
    {
        return objects.TryRemove(id, out _);
    }

    public static void CreateMetatable(lua_State state, Type type)
    {
        var typeName = GetTypeName(type);

        if (LuaCAux.luaL_newmetatable(state, typeName) == 0)
        {
            // Metatable already exists
            return;
        }

        // Set __index metamethod for property and method access
        LuaC.lua_pushcfunction(state, IndexMetamethod);
        LuaC.lua_setfield(state, -2, "__index");

        // Set __newindex metamethod for property setting
        LuaC.lua_pushcfunction(state, NewIndexMetamethod);
        LuaC.lua_setfield(state, -2, "__newindex");

        // Set __tostring metamethod
        LuaC.lua_pushcfunction(state, ToStringMetamethod);
        LuaC.lua_setfield(state, -2, "__tostring");

        // Set __gc metamethod for cleanup
        LuaC.lua_pushcfunction(state, GCMetamethod);
        LuaC.lua_setfield(state, -2, "__gc");

        // Store the type name in the metatable
        LuaC.lua_pushstring(state, typeName);
        LuaC.lua_setfield(state, -2, "__typename");

        LuaC.lua_pop(state, 1); // Pop metatable
    }

    [RequiresUnreferencedCode("Uses reflection to access members of objects.")]
    private static int IndexMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr == nint.Zero)
            {
                LuaC.lua_pushnil(state);
                return 1;
            }

            var objectId = Marshal.ReadIntPtr(userDataPtr);
            var dotnetObject = GetObject(objectId);

            if (dotnetObject is null)
            {
                LuaC.lua_pushnil(state);
                return 1;
            }

            var key = LuaC.lua_tostring(state, 2);

            if (string.IsNullOrEmpty(key))
            {
                LuaC.lua_pushnil(state);
                return 1;
            }

            var type = dotnetObject.GetType();

            // Try to find property first
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanRead == true)
            {
                var value = property.GetValue(dotnetObject);
                PushValue(state, value);
                return 1;
            }

            // Try to find field
            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null)
            {
                var value = field.GetValue(dotnetObject);
                PushValue(state, value);
                return 1;
            }

            // Try to find method(s)
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == key)
                .ToArray();

            if (methods.Length > 0)
            {
                // Create a closure that captures the object ID and method name
                LuaC.lua_pushnumber(state, objectId);
                LuaC.lua_pushstring(state, key);
                LuaC.lua_pushcclosure(state, MethodCallMetamethod, 2);
                return 1;
            }

            LuaC.lua_pushnil(state);
            return 1;
        }
        catch (Exception ex)
        {
            LuaC.lua_pushstring(state, $"Error in __index: {ex.Message}");
            return LuaC.lua_error(state);
        }
    }

    [RequiresUnreferencedCode("Uses reflection to access members of objects.")]
    private static int NewIndexMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr == nint.Zero)
            {
                LuaC.lua_pushnil(state);
                return 0;
            }

            var dotnetObject = GetManagedObject(userDataPtr);

            if (dotnetObject is null)
            {
                LuaC.lua_pushnil(state);
                return 0;
            }

            var key = LuaC.lua_tostring(state, 2);

            if (string.IsNullOrEmpty(key))
            {
                LuaC.lua_pushnil(state);
                return 0;
            }

            var type = dotnetObject.GetType();

            // Try to find property
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanWrite == true)
            {
                var value = PopValue(state, 3, property.PropertyType);
                property.SetValue(dotnetObject, value);
                return 0;
            }

            // Try to find field
            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                var value = PopValue(state, 3, field.FieldType);
                field.SetValue(dotnetObject, value);
                return 0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            LuaC.lua_pushstring(state, $"Error in __newindex: {ex.Message}");
            return LuaC.lua_error(state);
        }
    }

    private static int ToStringMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr == nint.Zero)
            {
                LuaC.lua_pushstring(state, "null");
                return 1;
            }

            var dotnetObject = GetManagedObject(userDataPtr);

            if (dotnetObject is null)
            {
                LuaC.lua_pushstring(state, "invalid object");
                return 1;
            }

            var str = dotnetObject.ToString() ?? "null";
            LuaC.lua_pushstring(state, str);
            return 1;
        }
        catch (Exception ex)
        {
            LuaC.lua_pushstring(state, $"Error in __tostring: {ex.Message}");
            return 1;
        }
    }

    private static int GCMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr != nint.Zero)
            {
                var objectId = Marshal.ReadIntPtr(userDataPtr);
                UnregisterObject(objectId);
            }
        }
        catch
        {
            // Ignore errors during GC
        }

        return 0;
    }

    [RequiresUnreferencedCode("Uses reflection to access members of objects.")]
    private static int MethodCallMetamethod(lua_State state)
    {
        try
        {
            // Get the captured values from the closure
            var objectId = (nint)LuaC.lua_tonumber(state, LuaC.lua_upvalueindex(1));
            var methodName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(2));

            var dotnetObject = GetObject(objectId);

            if (dotnetObject is null)
            {
                LuaC.lua_pushstring(state, "Invalid object reference");
                return LuaC.lua_error(state);
            }

            var type = dotnetObject.GetType();

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (methods.Length == 0)
            {
                LuaC.lua_pushstring(state, $"Method '{methodName}' not found.");
                return LuaC.lua_error(state);
            }

            // Argument count includes the self object at index 1, so subtract 1 to get actual args
            var argCount = LuaC.lua_gettop(state) - 1;

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();

                if (parameters.Length != argCount)
                {
                    continue;
                }

                var args = new object?[parameters.Length];
                var match = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var arg = PopValue(state, i + 2, paramType); // +2 to skip self and 1-based index

                    if (arg is null && paramType.IsValueType && Nullable.GetUnderlyingType(paramType) is null)
                    {
                        match = false;
                        break;
                    }

                    if (arg != null && !paramType.IsAssignableFrom(arg.GetType()))
                    {
                        match = false;
                        break;
                    }

                    args[i] = arg;
                }

                if (!match)
                {
                    continue;
                }

                var result = method.Invoke(dotnetObject, args);

                if (method.ReturnType == typeof(void))
                {
                    return 0;
                }

                PushValue(state, result);
                return 1;
            }

            LuaC.lua_pushstring(state, $"No matching method '{methodName}' found for the given arguments.");
            return LuaC.lua_error(state);
        }
        catch (Exception ex)
        {
            LuaC.lua_pushstring(state, $"Error in method call: {ex.Message}");
            return LuaC.lua_error(state);
        }
    }

    private static void PushValue(lua_State state, object? value)
    {
        switch (value)
        {
            case null:
                LuaC.lua_pushnil(state);
                break;
            case bool b:
                LuaC.lua_pushboolean(state, b ? 1 : 0);
                break;
            case int i:
                LuaC.lua_pushinteger(state, i);
                break;
            case double d:
                LuaC.lua_pushnumber(state, d);
                break;
            case float f:
                LuaC.lua_pushnumber(state, f);
                break;
            case string s:
                LuaC.lua_pushstring(state, s);
                break;
            default:
                // For complex objects, register them and create userdata
                CreateObjectUserData(state, value);
                break;
        }
    }

    private static object? PopValue(lua_State state, int index, Type targetType)
    {
        var luaType = LuaC.lua_type(state, index);

        return luaType switch
        {
            LuaType.Nil => null,
            LuaType.Boolean => LuaC.lua_toboolean(state, index) != 0,
            LuaType.Number => ConvertNumber(state, index, targetType),
            LuaType.String => LuaC.lua_tostring(state, index),
            LuaType.UserData => GetUserDataObject(state, index),
            _ => null
        };
    }

    private static object ConvertNumber(lua_State state, int index, Type targetType)
    {
        // Handle nullable types
        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (numericConverters.TryGetValue(actualType, out var converter))
        {
            return converter(state, index);
        }

        // Default to double for unknown numeric types
        return LuaC.lua_tonumber(state, index);
    }

    public static void CreateObjectUserData(lua_State state, object obj)
    {
        var objectId = RegisterObject(obj);
        var type = obj.GetType();
        var typeName = GetTypeName(type);

        // Create userdata and store the object ID in it
        var userDataPtr = LuaC.lua_newuserdatauv(state, nint.Size);
        Marshal.WriteIntPtr(userDataPtr, objectId);

        // Create metatable if it doesn't exist
        CreateMetatable(state, type);

        // Set the metatable for the userdata
        LuaCAux.luaL_setmetatable(state, typeName);
    }

    private static object? GetUserDataObject(lua_State state, int index)
    {
        var userDataPtr = LuaC.lua_touserdata(state, index);

        if (userDataPtr == nint.Zero)
        {
            return null;
        }

        return GetManagedObject(userDataPtr);
    }
}