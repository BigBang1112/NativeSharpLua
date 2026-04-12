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
}
