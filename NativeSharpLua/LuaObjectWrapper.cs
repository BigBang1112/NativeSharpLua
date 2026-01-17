using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using static NativeSharpLua.LuaCTypes;

namespace NativeSharpLua;

/// <summary>
/// Represents a C# object wrapped for use in Lua
/// </summary>
public class LuaObjectWrapper
{
    public object Target { get; }
    public Type Type { get; }

    public LuaObjectWrapper(object target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Type = target.GetType();
    }
}

/// <summary>
/// Manages C# object registration and interaction with Lua
/// </summary>
public static class LuaObjectRegistry
{
    private static readonly ConcurrentDictionary<nint, LuaObjectWrapper> _objects = new();
    private static readonly ConcurrentDictionary<Type, string> _typeNames = new();
    private static int _nextId = 1;

    public static nint RegisterObject(object obj)
    {
        var id = Interlocked.Increment(ref _nextId);
        var wrapper = new LuaObjectWrapper(obj);
        _objects[id] = wrapper;
        return id;
    }

    public static LuaObjectWrapper? GetWrapper(nint id)
    {
        return _objects.TryGetValue(id, out var wrapper) ? wrapper : null;
    }

    public static void UnregisterObject(nint id)
    {
        _objects.TryRemove(id, out _);
    }

    public static string GetTypeName(Type type)
    {
        return _typeNames.GetOrAdd(type, t => $"csharp_{t.FullName?.Replace('.', '_')}");
    }

    /// <summary>
    /// Creates Lua metatable for a C# type
    /// </summary>
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

    private static int IndexMetamethod(lua_State state)
    {
        try
        {
            // Get the userdata (C# object wrapper)
            var userDataPtr = LuaC.lua_touserdata(state, 1);
            if (userDataPtr == nint.Zero)
            {
                LuaC.lua_pushnil(state);
                return 1;
            }

            var objectId = Marshal.ReadIntPtr(userDataPtr);
            var wrapper = GetWrapper(objectId);
            if (wrapper == null)
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

            var obj = wrapper.Target;
            var type = wrapper.Type;

            // Try to find property first
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanRead)
            {
                var value = property.GetValue(obj);
                PushValue(state, value);
                return 1;
            }

            // Try to find field
            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(obj);
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

    private static int NewIndexMetamethod(lua_State state)
    {
        try
        {
            // Get the userdata (C# object wrapper)
            var userDataPtr = LuaC.lua_touserdata(state, 1);
            if (userDataPtr == nint.Zero)
                return 0;

            var objectId = Marshal.ReadIntPtr(userDataPtr);
            var wrapper = GetWrapper(objectId);
            if (wrapper == null)
                return 0;

            var key = LuaC.lua_tostring(state, 2);
            if (string.IsNullOrEmpty(key))
                return 0;

            var obj = wrapper.Target;
            var type = wrapper.Type;

            // Try to find property
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                var value = PopValue(state, 3, property.PropertyType);
                property.SetValue(obj, value);
                return 0;
            }

            // Try to find field
            var field = type.GetField(key, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                var value = PopValue(state, 3, field.FieldType);
                field.SetValue(obj, value);
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

    private static int MethodCallMetamethod(lua_State state)
    {
        try
        {
            // Get the captured values from the closure
            var objectId = (nint)LuaC.lua_tonumber(state, LuaC.lua_upvalueindex(1));
            var methodName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(2));

            var wrapper = GetWrapper(objectId);
            if (wrapper == null)
            {
                LuaC.lua_pushstring(state, "Invalid object reference");
                return LuaC.lua_error(state);
            }

            var obj = wrapper.Target;
            var type = wrapper.Type;

            // Get the actual argument count (excluding the implicit 'self' parameter from colon syntax)
            var totalArgs = LuaC.lua_gettop(state);
            var argCount = totalArgs > 0 ? totalArgs - 1 : 0; // Subtract 1 for the implicit self parameter
            var args = new object?[argCount];

            // Get methods with the given name
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .ToArray();

            if (methods.Length == 0)
            {
                LuaC.lua_pushstring(state, $"Method '{methodName}' not found");
                return LuaC.lua_error(state);
            }

            // Try to find a matching method by parameter count and types
            MethodInfo? matchingMethod = null;
            ParameterInfo[]? parameters = null;

            foreach (var method in methods)
            {
                parameters = method.GetParameters();
                if (parameters.Length == argCount)
                {
                    var canMatch = true;
                    for (int i = 0; i < argCount; i++)
                    {
                        // Start from index 2 to skip the implicit self parameter (index 1)
                        args[i] = PopValue(state, i + 2, parameters[i].ParameterType);
                        if (args[i] == null && !parameters[i].ParameterType.IsNullable())
                        {
                            canMatch = false;
                            break;
                        }
                    }

                    if (canMatch)
                    {
                        matchingMethod = method;
                        break;
                    }
                }
            }

            if (matchingMethod == null)
            {
                LuaC.lua_pushstring(state, $"No matching overload found for method '{methodName}' with {argCount} parameters");
                return LuaC.lua_error(state);
            }

            // Call the method
            var result = matchingMethod.Invoke(obj, args);
            
            if (matchingMethod.ReturnType == typeof(void))
            {
                return 0;
            }

            PushValue(state, result);
            return 1;
        }
        catch (Exception ex)
        {
            LuaC.lua_pushstring(state, $"Error calling method: {ex.Message}");
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

            var objectId = Marshal.ReadIntPtr(userDataPtr);
            var wrapper = GetWrapper(objectId);
            if (wrapper == null)
            {
                LuaC.lua_pushstring(state, "invalid object");
                return 1;
            }

            var str = wrapper.Target.ToString() ?? "null";
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

        if (actualType == typeof(int))
        {
            return LuaC.lua_tointeger(state, index);
        }
        else if (actualType == typeof(long))
        {
            return (long)LuaC.lua_tonumber(state, index);
        }
        else if (actualType == typeof(float))
        {
            return (float)LuaC.lua_tonumber(state, index);
        }
        else if (actualType == typeof(double))
        {
            return LuaC.lua_tonumber(state, index);
        }
        else if (actualType == typeof(decimal))
        {
            return (decimal)LuaC.lua_tonumber(state, index);
        }
        else if (actualType == typeof(byte))
        {
            return (byte)LuaC.lua_tointeger(state, index);
        }
        else if (actualType == typeof(short))
        {
            return (short)LuaC.lua_tointeger(state, index);
        }
        else if (actualType == typeof(uint))
        {
            return (uint)LuaC.lua_tointeger(state, index);
        }
        else if (actualType == typeof(ulong))
        {
            return (ulong)LuaC.lua_tonumber(state, index);
        }
        else if (actualType == typeof(ushort))
        {
            return (ushort)LuaC.lua_tointeger(state, index);
        }
        else if (actualType == typeof(sbyte))
        {
            return (sbyte)LuaC.lua_tointeger(state, index);
        }
        else
        {
            // Default to double for unknown numeric types
            return LuaC.lua_tonumber(state, index);
        }
    }

    private static object? GetUserDataObject(lua_State state, int index)
    {
        var userDataPtr = LuaC.lua_touserdata(state, index);
        if (userDataPtr == nint.Zero)
            return null;

        var objectId = Marshal.ReadIntPtr(userDataPtr);
        var wrapper = GetWrapper(objectId);
        return wrapper?.Target;
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
}

/// <summary>
/// Extension methods for type checking
/// </summary>
internal static class TypeExtensions
{
    public static bool IsNullable(this Type type)
    {
        return !type.IsValueType || (Nullable.GetUnderlyingType(type) != null);
    }
}