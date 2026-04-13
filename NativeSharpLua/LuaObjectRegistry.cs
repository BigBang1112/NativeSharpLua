using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using static NativeSharpLua.LuaCTypes;

namespace NativeSharpLua;

public sealed class LuaObjectRegistry
{
    private const string UsesReflection = "Uses reflection to access members of objects.";

    private static readonly ConcurrentDictionary<Type, string> typeNames = new();
    private static readonly ConcurrentDictionary<string, Type> registeredTypes = new();

    // Efficient numeric type conversions
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

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly lua_State state;
    private readonly ConcurrentDictionary<nint, object> objects = new();
    private readonly ConcurrentDictionary<object, nint> objectIds = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<nint, int> objectRefCounts = new();
    private int nextId = 1;
    private Exception? pendingException;
    private readonly lua_CFunction nilIterator;

    // Kept as a field to prevent GC collection while Lua holds the function pointer
    private int NilIterator(lua_State _) { LuaC.lua_pushnil(_); return 1; }

    internal LuaObjectRegistry(lua_State state)
    {
        this.state = state;
        nilIterator = NilIterator;
    }

    /// <summary>
    /// If a managed exception occurred inside a Lua callback, re-throws it.
    /// Called after every Lua execution to surface errors safely.
    /// </summary>
    internal void ThrowIfPendingException()
    {
        var ex = Interlocked.Exchange(ref pendingException, null);
        if (ex is not null)
            throw new LuaException(ex.Message, ex);
    }

    private void StorePendingException(Exception ex)
    {
        Interlocked.CompareExchange(ref pendingException, ex, null);
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    public void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        CreateMetatable(type);

        var typeName = GetTypeName(type);
        registeredTypes[typeName] = type;

        // Expose as a table so it can be called as TypeName:new(...) or TypeName.new(...)
        LuaC.lua_newtable(state);
        LuaC.lua_pushstring(state, typeName);
        LuaC.lua_pushcclosure(state, ConstructorCallMetamethod, 1);
        LuaC.lua_setfield(state, -2, "new");

        // Add static methods to the class table
        foreach (var methodName in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .Distinct())
        {
            LuaC.lua_pushstring(state, typeName);
            LuaC.lua_pushstring(state, methodName);
            LuaC.lua_pushcclosure(state, StaticCallMetamethod, 2);
            LuaC.lua_setfield(state, -2, methodName);
        }

        LuaC.lua_setglobal(state, type.Name);
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    public void RegisterType<T>()
    {
        RegisterType(typeof(T));
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    public void RegisterObject(object obj, string name)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentException.ThrowIfNullOrEmpty(name);

        CreateObjectUserData(obj);
        LuaC.lua_setglobal(state, name);
    }

    private static string GetTypeName(Type type)
    {
        return typeNames.GetOrAdd(type, t => $"dotnet_{t.FullName?.Replace('.', '_')}");
    }

    private object? GetObject(nint id)
    {
        return objects.TryGetValue(id, out var wrapper) ? wrapper : null;
    }

    private object? GetManagedObject(IntPtr userDataPtr)
    {
        var objectId = Marshal.ReadIntPtr(userDataPtr);
        return GetObject(objectId);
    }

    private nint RegisterObject(object obj)
    {
        if (objectIds.TryGetValue(obj, out var existingId))
        {
            objectRefCounts.AddOrUpdate(existingId, 1, static (_, count) => count + 1);
            return existingId;
        }

        var id = Interlocked.Increment(ref nextId);
        objects[id] = obj;
        objectIds[obj] = id;
        objectRefCounts[id] = 1;
        return id;
    }

    private bool UnregisterObject(nint id)
    {
        var newCount = objectRefCounts.AddOrUpdate(id, 0, static (_, count) => count - 1);

        if (newCount > 0)
            return false;

        objectRefCounts.TryRemove(id, out _);

        if (objects.TryRemove(id, out var obj))
            objectIds.TryRemove(obj, out _);

        return true;
    }

    // Walk the hierarchy from most-derived to base so that hiding/overriding members
    // (declared with 'new') take precedence over base-class definitions.
    
    [RequiresUnreferencedCode(UsesReflection)]
    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null) return p;
        }
        return null;
    }

    [RequiresUnreferencedCode(UsesReflection)]
    private static FieldInfo? FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    [RequiresUnreferencedCode(UsesReflection)]
    private static MethodInfo[] FindMethods(Type type, string? name, BindingFlags flags)
    {
        var instanceFlags = flags | BindingFlags.DeclaredOnly;
        for (var t = type; t != null; t = t.BaseType)
        {
            var methods = t.GetMethods(instanceFlags)
                .Where(m => m.Name == name)
                .ToArray();
            if (methods.Length > 0) return methods;
        }
        return [];
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private void CreateMetatable(Type type)
    {
        var typeName = GetTypeName(type);

        if (LuaCAux.luaL_newmetatable(state, typeName) == 0)
        {
            // Metatable already exists
            LuaC.lua_pop(state, 1);
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

        // Set __eq metamethod for object identity
        LuaC.lua_pushcfunction(state, EqMetamethod);
        LuaC.lua_setfield(state, -2, "__eq");

        // Set __len metamethod for collections
        LuaC.lua_pushcfunction(state, LenMetamethod);
        LuaC.lua_setfield(state, -2, "__len");

        // Set __pairs metamethod for iteration
        LuaC.lua_pushcfunction(state, PairsMetamethod);
        LuaC.lua_setfield(state, -2, "__pairs");

        // Store the type name in the metatable
        LuaC.lua_pushstring(state, typeName);
        LuaC.lua_setfield(state, -2, "__typename");

        LuaC.lua_pop(state, 1); // Pop metatable
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int IndexMetamethod(lua_State state)
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

            var keyType = LuaC.lua_type(state, 2);
            var key = keyType == LuaType.String ? LuaC.lua_tostring(state, 2) : null;
            var type = dotnetObject.GetType();

            if (!string.IsNullOrEmpty(key))
            {
                // Handle special dumpJSON method
                if (key == "dumpJSON")
                {
                    // Create a closure that captures the object ID
                    LuaC.lua_pushnumber(state, objectId);
                    LuaC.lua_pushcclosure(state, DumpJsonMetamethod, 1);
                    return 1;
                }

                // Try to find property first
                var property = FindProperty(type, key);
                if (property?.CanRead == true)
                {
                    var value = property.GetValue(dotnetObject);
                    PushValue(state, value);
                    return 1;
                }

                // Try to find field
                var field = FindField(type, key);
                if (field is not null)
                {
                    var value = field.GetValue(dotnetObject);
                    PushValue(state, value);
                    return 1;
                }

                // Try to find method(s)
                var methods = FindMethods(type, key, BindingFlags.Public | BindingFlags.Instance);

                if (methods.Length > 0)
                {
                    // Create a closure that captures the object ID and method name
                    LuaC.lua_pushnumber(state, objectId);
                    LuaC.lua_pushstring(state, key);
                    LuaC.lua_pushcclosure(state, MethodCallMetamethod, 2);
                    return 1;
                }
            }

            // Try indexer (e.g., list[0], dict["key"])
            foreach (var indexer in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 1 && p.CanRead))
            {
                var paramType = indexer.GetIndexParameters()[0].ParameterType;
                var indexKey = PopValue(state, 2, paramType);

                if (indexKey is null && paramType.IsValueType && Nullable.GetUnderlyingType(paramType) is null)
                    continue;
                if (indexKey is not null && !paramType.IsAssignableFrom(indexKey.GetType()))
                    continue;

                try
                {
                    var result = indexer.GetValue(dotnetObject, [indexKey]);
                    PushValue(state, result);
                    return 1;
                }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException or IndexOutOfRangeException)
                {
                    // Out-of-range access is normal during ipairs iteration — return nil to signal end
                    LuaC.lua_pushnil(state);
                    return 1;
                }
            }

            LuaC.lua_pushnil(state);
            return 1;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushnil(state);
            return 1;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    private int NewIndexMetamethod(lua_State state)
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

            var keyType = LuaC.lua_type(state, 2);
            var key = keyType == LuaType.String ? LuaC.lua_tostring(state, 2) : null;
            var type = dotnetObject.GetType();

            if (!string.IsNullOrEmpty(key))
            {
                // Try to find property
                var property = FindProperty(type, key);
                if (property is not null)
                {
                    if (property.CanWrite)
                    {
                        var value = PopValue(state, 3, property.PropertyType);
                        property.SetValue(dotnetObject, value);
                        return 0;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Property '{key}' is read-only.");
                    }
                }

                // Try to find field
                var field = FindField(type, key);
                if (field != null)
                {
                    var value = PopValue(state, 3, field.FieldType);
                    field.SetValue(dotnetObject, value);
                    return 0;
                }
            }

            // Try indexer (e.g., list[0] = v, dict["key"] = v)
            foreach (var indexer in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 1 && p.CanWrite))
            {
                var paramType = indexer.GetIndexParameters()[0].ParameterType;
                var indexKey = PopValue(state, 2, paramType);

                if (indexKey is null && paramType.IsValueType && Nullable.GetUnderlyingType(paramType) is null)
                    continue;
                if (indexKey is not null && !paramType.IsAssignableFrom(indexKey.GetType()))
                    continue;

                var value = PopValue(state, 3, indexer.PropertyType);
                try
                {
                    indexer.SetValue(dotnetObject, value, [indexKey]);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException or IndexOutOfRangeException)
                {
                    return 0;
                }
                return 0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            return 0;
        }
    }

    private int ToStringMetamethod(lua_State state)
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

    private int GCMetamethod(lua_State state)
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

    private int EqMetamethod(lua_State state)
    {
        var aPtr = LuaC.lua_touserdata(state, 1);
        var bPtr = LuaC.lua_touserdata(state, 2);

        if (aPtr == nint.Zero || bPtr == nint.Zero)
        {
            LuaC.lua_pushboolean(state, 0);
            return 1;
        }

        var aId = Marshal.ReadIntPtr(aPtr);
        var bId = Marshal.ReadIntPtr(bPtr);
        LuaC.lua_pushboolean(state, aId == bId ? 1 : 0);
        return 1;
    }

    private int LenMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr == nint.Zero)
            {
                LuaC.lua_pushinteger(state, 0);
                return 1;
            }

            var dotnetObject = GetManagedObject(userDataPtr);

            if (dotnetObject is ICollection collection)
            {
                LuaC.lua_pushinteger(state, collection.Count);
                return 1;
            }

            if (dotnetObject is IEnumerable enumerable)
            {
                var count = 0;
                foreach (var _ in enumerable)
                    count++;
                LuaC.lua_pushinteger(state, count);
                return 1;
            }

            LuaC.lua_pushinteger(state, 0);
            return 1;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushinteger(state, 0);
            return 1;
        }
    }

    private sealed class EnumeratorState(IEnumerator enumerator)
    {
        public IEnumerator Enumerator { get; } = enumerator;
        public int Index { get; set; }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int PairsMetamethod(lua_State state)
    {
        try
        {
            var userDataPtr = LuaC.lua_touserdata(state, 1);

            if (userDataPtr == nint.Zero)
                throw new InvalidOperationException("Invalid object for pairs.");

            var dotnetObject = GetManagedObject(userDataPtr);

            if (dotnetObject is not IEnumerable enumerable)
                throw new InvalidOperationException($"Object of type '{dotnetObject?.GetType().Name}' is not enumerable.");

            var wrapper = new EnumeratorState(enumerable.GetEnumerator());
            var wrapperId = RegisterObject(wrapper);

            // Push iterator function as a closure capturing the wrapper ID
            LuaC.lua_pushnumber(state, wrapperId);
            LuaC.lua_pushcclosure(state, PairsIterator, 1);

            // Push nil for state and initial control variable (Lua generic for)
            LuaC.lua_pushnil(state);
            LuaC.lua_pushnil(state);

            return 3;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushcfunction(state, nilIterator);
            LuaC.lua_pushnil(state);
            LuaC.lua_pushnil(state);
            return 3;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int PairsIterator(lua_State state)
    {
        try
        {
            var wrapperId = (nint)LuaC.lua_tonumber(state, LuaC.lua_upvalueindex(1));
            var obj = GetObject(wrapperId);

            if (obj is not EnumeratorState wrapper)
            {
                LuaC.lua_pushnil(state);
                return 1;
            }

            if (!wrapper.Enumerator.MoveNext())
            {
                // Iteration complete — clean up
                UnregisterObject(wrapperId);
                (wrapper.Enumerator as IDisposable)?.Dispose();
                LuaC.lua_pushnil(state);
                return 1;
            }

            var current = wrapper.Enumerator.Current;
            wrapper.Index++;

            // Check for KeyValuePair-like types (dictionary iteration)
            if (current is not null)
            {
                var currentType = current.GetType();
                if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    var key = currentType.GetProperty("Key")!.GetValue(current);
                    var value = currentType.GetProperty("Value")!.GetValue(current);
                    PushValue(state, key);
                    PushValue(state, value);
                    return 2;
                }
            }

            // For lists/arrays, push 1-based index as key
            LuaC.lua_pushinteger(state, wrapper.Index);
            PushValue(state, current);
            return 2;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushnil(state);
            return 1;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int MethodCallMetamethod(lua_State state)
    {
        try
        {
            // Get the captured values from the closure
            var objectId = (nint)LuaC.lua_tonumber(state, LuaC.lua_upvalueindex(1));
            var methodName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(2));

            var dotnetObject = GetObject(objectId);

            if (dotnetObject is null)
            {
                throw new InvalidOperationException("Invalid object reference");
            }

            var type = dotnetObject.GetType();

            var methods = FindMethods(type, methodName, BindingFlags.Public | BindingFlags.Instance);

            if (methods.Length == 0)
            {
                throw new MissingMethodException(methodName);
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

            throw new Exception($"No matching method '{methodName}' found for the given arguments.");
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            return 0;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int DumpJsonMetamethod(lua_State state)
    {
        try
        {
            // Get the captured object ID from the closure
            var objectId = (nint)LuaC.lua_tonumber(state, LuaC.lua_upvalueindex(1));

            var dotnetObject = GetObject(objectId);

            if (dotnetObject is null)
            {
                LuaC.lua_pushstring(state, "null");
                return 1;
            }

            var json = JsonSerializer.Serialize(dotnetObject, jsonOptions);
            LuaC.lua_pushstring(state, json);
            return 1;
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushnil(state);
            return 1;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int StaticCallMetamethod(lua_State state)
    {
        try
        {
            var typeName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(1));
            var methodName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(2));

            if (string.IsNullOrEmpty(typeName) || !registeredTypes.TryGetValue(typeName, out var type))
                throw new InvalidOperationException("Static method called for an unregistered type.");

            var methods = FindMethods(type, methodName, BindingFlags.Public | BindingFlags.Static);

            if (methods.Length == 0)
                throw new MissingMethodException(type.Name, methodName);

            var argCount = LuaC.lua_gettop(state);

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();

                if (parameters.Length != argCount)
                    continue;

                var args = new object?[parameters.Length];
                var match = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var arg = PopValue(state, i + 1, paramType);

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
                    continue;

                var result = method.Invoke(null, args);

                if (method.ReturnType == typeof(void))
                    return 0;

                PushValue(state, result);
                return 1;
            }

            throw new Exception($"No matching static method '{methodName}' found for the given arguments.");
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            return 0;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private int ConstructorCallMetamethod(lua_State state)
    {
        try
        {
            var typeName = LuaC.lua_tostring(state, LuaC.lua_upvalueindex(1));

            if (string.IsNullOrEmpty(typeName) || !registeredTypes.TryGetValue(typeName, out var type))
                throw new InvalidOperationException("Constructor called for an unregistered type.");

            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            if (ctors.Length == 0)
                throw new MissingMethodException(type.Name, ".ctor");

            // When called as MyClass:new(...), Lua passes the table as arg 1 — skip it.
            var startIndex = LuaC.lua_type(state, 1) == LuaType.Table ? 2 : 1;
            var argCount = LuaC.lua_gettop(state) - (startIndex - 1);

            foreach (var ctor in ctors)
            {
                var parameters = ctor.GetParameters();

                if (parameters.Length != argCount)
                    continue;

                var args = new object?[parameters.Length];
                var match = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var arg = PopValue(state, startIndex + i, paramType);

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
                    continue;

                var instance = ctor.Invoke(args);
                CreateObjectUserData(state, instance);
                return 1;
            }

            throw new Exception($"No matching constructor found for '{type.Name}' with {argCount} argument(s).");
        }
        catch (Exception ex)
        {
            StorePendingException(ex);
            LuaC.lua_pushnil(state);
            return 1;
        }
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private void PushValue(lua_State state, object? value)
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

    private object? PopValue(lua_State state, int index, Type targetType)
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

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private void CreateObjectUserData(lua_State state, object obj)
    {
        var objectId = RegisterObject(obj);
        var type = obj.GetType();
        var typeName = GetTypeName(type);

        // Create userdata and store the object ID in it
        var userDataPtr = LuaC.lua_newuserdatauv(state, nint.Size);
        Marshal.WriteIntPtr(userDataPtr, objectId);

        // Create metatable if it doesn't exist
        CreateMetatable(type);

        // Set the metatable for the userdata
        LuaCAux.luaL_setmetatable(state, typeName);
    }

    [RequiresUnreferencedCode(UsesReflection)]
    [RequiresDynamicCode(UsesReflection)]
    private void CreateObjectUserData(object obj)
    {
        CreateObjectUserData(state, obj);
    }

    private object? GetUserDataObject(lua_State state, int index)
    {
        var userDataPtr = LuaC.lua_touserdata(state, index);

        if (userDataPtr == nint.Zero)
        {
            return null;
        }

        return GetManagedObject(userDataPtr);
    }
}