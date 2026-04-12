using System.Collections.Generic;

namespace NativeSharpLua.Tests;

public class LuaObjectRegistryTests
{
    private static LuaEngine CreateEngine(LuaLibrary libs = LuaLibrary.None)
        => new(libs, LuaLibrary.None);

    private static LuaEngine CreateEngineWithBase()
        => CreateEngine(LuaLibrary.Base | LuaLibrary.String);

    [Fact]
    public void RegisterObject_ReadProperties()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();
        var obj = new SimpleObject { Name = "Alice", IntValue = 7 };

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(obj.Name)");
        Assert.Equal("Alice", capture.StringValue);

        engine.Run("cap:SetInt(obj.IntValue)");
        Assert.Equal(7, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_WriteProperties()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.Name = 'Bob'");
        engine.Run("obj.IntValue = 99");

        Assert.Equal("Bob", obj.Name);
        Assert.Equal(99, obj.IntValue);
    }

    [Fact]
    public void RegisterObject_NestedObjectAccess()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.Nested.DoubleValue = 9.5");

        Assert.Equal(9.5, obj.Nested.DoubleValue);
    }

    [Fact]
    public void RegisterObject_NullName_Throws()
    {
        var engine = CreateEngine();

        Assert.Throws<ArgumentException>(() =>
            engine.ObjectRegistry.RegisterObject(new SimpleObject(), ""));
    }

    [Fact]
    public void RegisterObject_NullObject_Throws()
    {
        var engine = CreateEngine();

        Assert.Throws<ArgumentNullException>(() =>
            engine.ObjectRegistry.RegisterObject(null!, "x"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Boolean properties
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_BooleanProperty_ReadWrite()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { BoolValue = false };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetBool(obj.BoolValue)");
        Assert.False(capture.BoolValue);

        engine.Run("obj.BoolValue = true");
        Assert.True(obj.BoolValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Numeric type conversions
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_DoubleProperty()
    {
        var engine = CreateEngine();
        var obj = new NumericObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.DoubleVal = 3.14");

        Assert.Equal(3.14, obj.DoubleVal, 5);
    }

    [Fact]
    public void RegisterObject_FloatProperty()
    {
        var engine = CreateEngine();
        var obj = new NumericObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.FloatVal = 2.5");

        Assert.Equal(2.5f, obj.FloatVal, 3);
    }

    [Fact]
    public void RegisterObject_LongProperty()
    {
        var engine = CreateEngine();
        var obj = new NumericObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.LongVal = 1000000");

        Assert.Equal(1000000L, obj.LongVal);
    }

    [Fact]
    public void RegisterObject_ShortProperty()
    {
        var engine = CreateEngine();
        var obj = new NumericObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.ShortVal = 123");

        Assert.Equal((short)123, obj.ShortVal);
    }

    [Fact]
    public void RegisterObject_ByteProperty()
    {
        var engine = CreateEngine();
        var obj = new NumericObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.ByteVal = 255");

        Assert.Equal((byte)255, obj.ByteVal);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Field access
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_PublicFieldRead()
    {
        var engine = CreateEngine();
        var obj = new ObjectWithFields { PublicField = "hello" };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(obj.PublicField)");
        Assert.Equal("hello", capture.StringValue);
    }

    [Fact]
    public void RegisterObject_PublicFieldWrite()
    {
        var engine = CreateEngine();
        var obj = new ObjectWithFields();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.PublicField = 'world'");

        Assert.Equal("world", obj.PublicField);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Instance method calls
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_MethodCall_NoArgs()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(obj:GetValue())");
        Assert.Equal(42, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_WithArgs()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(obj:Add(10, 20))");
        Assert.Equal(30, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_StringArg()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(obj:Greet('World'))");
        Assert.Equal("Hello, World!", capture.StringValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_VoidReturn()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj:SetState(100)");

        Assert.Equal(100, obj.State);
    }

    [Fact]
    public void RegisterObject_MethodCall_ReturnsObject()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local inner = obj:GetNested()
            cap:SetDouble(inner.DoubleValue)
        ");
        Assert.Equal(1.5, capture.DoubleValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_PassesObjectArg()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var nested = new NestedObject { DoubleValue = 7.7 };

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(nested, "n");

        engine.Run("obj:AcceptNested(n)");
        Assert.Equal(7.7, obj.LastNestedValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_Overloaded()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        // Call the 1-arg overload
        engine.Run("cap:Set(obj:Overloaded('test'))");
        Assert.Equal("single:test", capture.StringValue);

        // Call the 2-arg overload
        engine.Run("cap:Set(obj:Overloaded('a', 'b'))");
        Assert.Equal("double:a+b", capture.StringValue);
    }

    [Fact]
    public void RegisterObject_MethodCall_InvalidMethod_Throws()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        Assert.Throws<LuaException>(() => engine.Run("obj:NoSuchMethod()"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // __tostring metamethod
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_ToString()
    {
        var engine = CreateEngineWithBase();
        var obj = new ToStringObject("MyObj");
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(tostring(obj))");
        Assert.Equal("ToStringObject:MyObj", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // __eq metamethod
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Equality_SameObject()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "a");
        engine.ObjectRegistry.RegisterObject(obj, "b");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetBool(a == b)");
        Assert.True(capture.BoolValue);
    }

    [Fact]
    public void RegisterObject_Equality_DifferentObjects()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(new SimpleObject(), "a");
        engine.ObjectRegistry.RegisterObject(new SimpleObject(), "b");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetBool(a == b)");
        Assert.False(capture.BoolValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // __len metamethod
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Len_List()
    {
        var engine = CreateEngine();
        var list = new List<int> { 1, 2, 3 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(#lst)");
        Assert.Equal(3, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_Len_Dictionary()
    {
        var engine = CreateEngine();
        var dict = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(dict, "d");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(#d)");
        Assert.Equal(2, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_Len_NonCollection_ReturnsZero()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(#obj)");
        Assert.Equal(0, capture.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // __pairs metamethod — list iteration
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Pairs_List()
    {
        var engine = CreateEngineWithBase();
        var list = new List<string> { "alpha", "beta", "gamma" };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local result = ''
            for k, v in pairs(lst) do
                result = result .. tostring(k) .. '=' .. v .. ';'
            end
            cap:Set(result)
        ");
        Assert.Equal("1=alpha;2=beta;3=gamma;", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // __pairs metamethod — dictionary iteration
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Pairs_Dictionary()
    {
        var engine = CreateEngineWithBase();
        // Use a SortedDictionary for deterministic iteration order
        var dict = new SortedDictionary<string, int> { ["x"] = 10, ["y"] = 20 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(dict, "d");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local result = ''
            for k, v in pairs(d) do
                result = result .. k .. '=' .. tostring(v) .. ';'
            end
            cap:Set(result)
        ");
        Assert.Equal("x=10;y=20;", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Indexer access — List<T>
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_ListIndexer_Read()
    {
        var engine = CreateEngine();
        var list = new List<string> { "first", "second", "third" };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(lst[1])");
        Assert.Equal("second", capture.StringValue);
    }

    [Fact]
    public void RegisterObject_ListIndexer_Write()
    {
        var engine = CreateEngine();
        var list = new List<string> { "first", "second", "third" };

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.Run("lst[0] = 'replaced'");

        Assert.Equal("replaced", list[0]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Indexer access — Dictionary<TK,TV>
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_DictIndexer_Read()
    {
        var engine = CreateEngine();
        var dict = new Dictionary<string, int> { ["key1"] = 111 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(dict, "d");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(d['key1'])");
        Assert.Equal(111, capture.IntValue);
    }

    [Fact]
    public void RegisterObject_DictIndexer_Write()
    {
        var engine = CreateEngine();
        var dict = new Dictionary<string, int> { ["key1"] = 0 };

        engine.ObjectRegistry.RegisterObject(dict, "d");
        engine.Run("d['key1'] = 999");

        Assert.Equal(999, dict["key1"]);
    }

    [Fact]
    public void RegisterObject_DictIndexer_AddNew()
    {
        var engine = CreateEngine();
        var dict = new Dictionary<string, string>();

        engine.ObjectRegistry.RegisterObject(dict, "d");
        engine.Run("d['newKey'] = 'newVal'");

        Assert.Equal("newVal", dict["newKey"]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Read-only property
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_ReadOnlyProperty_ThrowsOnWrite()
    {
        var engine = CreateEngine();
        var obj = new ReadOnlyPropertyObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        Assert.Throws<LuaException>(() => engine.Run("obj.ReadOnly = 'fail'"));
    }

    [Fact]
    public void RegisterObject_ReadOnlyProperty_CanRead()
    {
        var engine = CreateEngine();
        var obj = new ReadOnlyPropertyObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(obj.ReadOnly)");
        Assert.Equal("immutable", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // dumpJSON
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_DumpJSON()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { Name = "JsonTest", IntValue = 5 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set(obj:dumpJSON())");
        Assert.Contains("\"Name\": \"JsonTest\"", capture.StringValue);
        Assert.Contains("\"IntValue\": 5", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // RegisterType — constructor
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterType_ConstructorDotNew()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<ConstructableObject>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local o = ConstructableObject.new('hello', 42)
            cap:Set(o.Label)
            cap:SetInt(o.Count)
        ");
        Assert.Equal("hello", capture.StringValue);
        Assert.Equal(42, capture.IntValue);
    }

    [Fact]
    public void RegisterType_ConstructorColonNew()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<ConstructableObject>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local o = ConstructableObject:new('hi', 7)
            cap:Set(o.Label)
            cap:SetInt(o.Count)
        ");
        Assert.Equal("hi", capture.StringValue);
        Assert.Equal(7, capture.IntValue);
    }

    [Fact]
    public void RegisterType_ParameterlessConstructor()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<SimpleObject>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local o = SimpleObject.new()
            cap:Set(o.Name)
        ");
        Assert.Equal("default", capture.StringValue);
    }

    [Fact]
    public void RegisterType_NoMatchingConstructor_Throws()
    {
        var engine = CreateEngine();
        engine.ObjectRegistry.RegisterType<ConstructableObject>();

        // ConstructableObject has (string, int) — pass wrong arity
        Assert.Throws<LuaException>(() =>
            engine.Run("ConstructableObject.new('only one')"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // RegisterType — static methods
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterType_StaticMethod()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<StaticMethodClass>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(StaticMethodClass.StaticAdd(3, 4))");
        Assert.Equal(7, capture.IntValue);
    }

    [Fact]
    public void RegisterType_StaticMethodVoid()
    {
        var engine = CreateEngine();
        engine.ObjectRegistry.RegisterType<StaticMethodClass>();

        StaticMethodClass.Counter = 0;
        engine.Run("StaticMethodClass.Increment()");
        Assert.Equal(1, StaticMethodClass.Counter);
    }

    [Fact]
    public void RegisterType_StaticMethodNoMatch_Throws()
    {
        var engine = CreateEngine();
        engine.ObjectRegistry.RegisterType<StaticMethodClass>();

        Assert.Throws<LuaException>(() =>
            engine.Run("StaticMethodClass.StaticAdd(1)"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // RegisterType — duplicate registration is safe
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterType_Duplicate_NoError()
    {
        var engine = CreateEngine();
        engine.ObjectRegistry.RegisterType<SimpleObject>();
        engine.ObjectRegistry.RegisterType<SimpleObject>();

        // Should not throw
    }

    // ──────────────────────────────────────────────────────────────────────
    // RegisterType<T> generic variant
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterType_Generic()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<SimpleObject>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local o = SimpleObject.new()
            o.IntValue = 55
            cap:SetInt(o.IntValue)
        ");
        Assert.Equal(55, capture.IntValue);
    }

    [Fact]
    public void RegisterType_NullType_Throws()
    {
        var engine = CreateEngine();
        Assert.Throws<ArgumentNullException>(() =>
            engine.ObjectRegistry.RegisterType(null!));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Nil handling
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_SetPropertyToNil_SetsNull()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { Name = "NotNull" };

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.Name = nil");

        Assert.Null(obj.Name);
    }

    [Fact]
    public void RegisterObject_AccessMissingProperty_ReturnsNil()
    {
        var engine = CreateEngineWithBase();
        var obj = new SimpleObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local v = obj.NonExistent
            cap:SetBool(v == nil)
        ");
        Assert.True(capture.BoolValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Method returning bool
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_MethodReturningBool()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetBool(obj:IsPositive(5))");
        Assert.True(capture.BoolValue);

        engine.Run("cap:SetBool(obj:IsPositive(-1))");
        Assert.False(capture.BoolValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Multiple objects in same engine
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_MultipleObjects_Independent()
    {
        var engine = CreateEngine();
        var a = new SimpleObject { IntValue = 10 };
        var b = new SimpleObject { IntValue = 20 };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(a, "a");
        engine.ObjectRegistry.RegisterObject(b, "b");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(a.IntValue + b.IntValue)");
        Assert.Equal(30, capture.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // String concatenation with object properties
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_StringConcatFromProperties()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { Name = "Lua" };
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:Set('Hello ' .. obj.Name)");
        Assert.Equal("Hello Lua", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Arithmetic on properties
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_ArithmeticOnProperties()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { IntValue = 10 };

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.Run("obj.IntValue = obj.IntValue * 3 + 1");

        Assert.Equal(31, obj.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Registered object used as method argument and return
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_PassBetweenMethods()
    {
        var engine = CreateEngine();
        var factory = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(factory, "fac");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local n = fac:GetNested()
            n.DoubleValue = 42.5
            cap:SetDouble(n.DoubleValue)
        ");
        Assert.Equal(42.5, capture.DoubleValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // RegisterType + instance methods on constructed objects
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterType_InstanceMethodOnConstructedObject()
    {
        var engine = CreateEngine();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterType<ConstructableObject>();
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local o = ConstructableObject.new('test', 5)
            cap:Set(o:Describe())
        ");
        Assert.Equal("test:5", capture.StringValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Pairs on non-enumerable throws
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Pairs_NonEnumerable_Throws()
    {
        var engine = CreateEngineWithBase();
        var obj = new SimpleObject();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        Assert.Throws<LuaException>(() => engine.Run("for k,v in pairs(obj) do end"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Pairs on empty collection
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Pairs_EmptyList()
    {
        var engine = CreateEngineWithBase();
        var list = new List<int>();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local count = 0
            for k, v in pairs(lst) do count = count + 1 end
            cap:SetInt(count)
        ");
        Assert.Equal(0, capture.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // __len on empty list
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_Len_EmptyList()
    {
        var engine = CreateEngine();
        var list = new List<int>();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(list, "lst");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run("cap:SetInt(#lst)");
        Assert.Equal(0, capture.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Same object registered twice shares identity
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_SameObjectTwice_SharesIdentity()
    {
        var engine = CreateEngine();
        var obj = new SimpleObject { IntValue = 10 };

        engine.ObjectRegistry.RegisterObject(obj, "a");
        engine.ObjectRegistry.RegisterObject(obj, "b");

        engine.Run("a.IntValue = 99");
        Assert.Equal(99, obj.IntValue);

        // b points to same object
        var capture = new CaptureHelper();
        engine.ObjectRegistry.RegisterObject(capture, "cap");
        engine.Run("cap:SetInt(b.IntValue)");
        Assert.Equal(99, capture.IntValue);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Object returned from method can be passed to another method
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterObject_ReturnedObjectPassedToMethod()
    {
        var engine = CreateEngine();
        var obj = new MethodObject();
        var capture = new CaptureHelper();

        engine.ObjectRegistry.RegisterObject(obj, "obj");
        engine.ObjectRegistry.RegisterObject(capture, "cap");

        engine.Run(@"
            local n = obj:GetNested()
            obj:AcceptNested(n)
            cap:SetDouble(obj.LastNestedValue)
        ");
        Assert.Equal(1.5, capture.DoubleValue);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Test helper classes
    // ══════════════════════════════════════════════════════════════════════

    private class CaptureHelper
    {
        public string? StringValue { get; private set; }
        public int IntValue { get; private set; }
        public double DoubleValue { get; private set; }
        public bool BoolValue { get; private set; }

        public void Set(string s) => StringValue = s;
        public void SetInt(int i) => IntValue = i;
        public void SetDouble(double d) => DoubleValue = d;
        public void SetBool(bool b) => BoolValue = b;
    }

    public class SimpleObject
    {
        public string? Name { get; set; } = "default";
        public int IntValue { get; set; }
        public bool BoolValue { get; set; }
        public NestedObject Nested { get; set; } = new();
    }

    public class NestedObject
    {
        public double DoubleValue { get; set; } = 1.5;
    }

    public class NumericObject
    {
        public double DoubleVal { get; set; }
        public float FloatVal { get; set; }
        public long LongVal { get; set; }
        public short ShortVal { get; set; }
        public byte ByteVal { get; set; }
    }

    public class ObjectWithFields
    {
        public string PublicField = "initial";
    }

    public class MethodObject
    {
        public int State { get; set; }
        public double LastNestedValue { get; set; }

        public int GetValue() => 42;
        public int Add(int a, int b) => a + b;
        public string Greet(string name) => $"Hello, {name}!";
        public void SetState(int v) => State = v;
        public bool IsPositive(int n) => n > 0;
        public NestedObject GetNested() => new();
        public void AcceptNested(NestedObject n) => LastNestedValue = n.DoubleValue;
        public string Overloaded(string a) => $"single:{a}";
        public string Overloaded(string a, string b) => $"double:{a}+{b}";
    }

    public class ToStringObject(string label)
    {
        public override string ToString() => $"ToStringObject:{label}";
    }

    public class ReadOnlyPropertyObject
    {
        public string ReadOnly { get; } = "immutable";
    }

    public class ConstructableObject
    {
        public string Label { get; set; }
        public int Count { get; set; }

        public ConstructableObject(string label, int count)
        {
            Label = label;
            Count = count;
        }

        public string Describe() => $"{Label}:{Count}";
    }

    public class StaticMethodClass
    {
        public static int Counter { get; set; }
        public static int StaticAdd(int a, int b) => a + b;
        public static void Increment() => Counter++;
    }
}
