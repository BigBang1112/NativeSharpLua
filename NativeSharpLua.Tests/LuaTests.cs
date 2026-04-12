namespace NativeSharpLua.Tests;

public class LuaTests
{
    [Fact]
    public void LuaEngine_ShouldRunCodeSuccessfully()
    {
        var luaEngine = new LuaEngine();
        luaEngine.Run("print('Hello, Lua!')");
        
        // If no exception is thrown, the test passes.
        Assert.True(true);
    }

    [Fact]
    public void LuaEngine_ShouldThrowExceptionOnError()
    {
        var luaEngine = new LuaEngine();
        
        Assert.Throws<LuaException>(() => luaEngine.Run("print('Hello, Lua!')\nerror('Test error')"));
    }

    [Fact]
    public void LuaEngine_RegisterObject_ShouldWorkCorrectly()
    {
        var luaEngine = new LuaEngine();

        var obj = new TestObject { Name = "TestObject", Value = 42 };
        luaEngine.ObjectRegistry.RegisterObject(obj, "obj");
        luaEngine.Run(@"
print('Object Name: ' .. obj.Name)
print('Object Value: ' .. obj.Value)
obj.Name = 'UpdatedName'
obj.Value = obj.Value + 1
obj.NestedObject.Value = 3.6
        ");

        Assert.Equal("UpdatedName", obj.Name);
        Assert.Equal(43, obj.Value);
        Assert.Equal(3.6, obj.NestedObject.Value);
    }

    private class TestObject
    {
        public string Name { get; set; } = "Test Object";
        public int Value { get; set; } = 42;
        public TestObject2 NestedObject { get; set; } = new TestObject2();
    }

    private class TestObject2
    {
        public double Value { get; set; } = 1.5;
    }
}
