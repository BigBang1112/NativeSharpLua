namespace NativeSharpLua.Tests;

public class LuaEngineTests
{
    private static LuaEngine CreateEngine(LuaLibrary libs = LuaLibrary.None)
        => new(libs, LuaLibrary.None);

    private static LuaEngine CreateEngineWithBase()
        => CreateEngine(LuaLibrary.Base | LuaLibrary.String | LuaLibrary.Math);

    // ──────────────────────────────────────────────────────────────────────
    // Run
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Run_ExecutesSuccessfully()
    {
        var engine = CreateEngine();
        engine.Run("print('Hello, Lua!')");
    }

    [Fact]
    public void Run_SyntaxError_Throws()
    {
        var engine = CreateEngine();
        Assert.Throws<LuaException>(() => engine.Run("if then end"));
    }

    [Fact]
    public void Run_RuntimeError_Throws()
    {
        var engine = CreateEngineWithBase();
        Assert.Throws<LuaException>(() =>
            engine.Run("error('Test error')"));
    }

    [Fact]
    public void Run_MultipleCalls_Isolated()
    {
        var engine = CreateEngine();
        engine.Run("x = 10");
        engine.Run("x = x + 5");

        var result = engine.Eval("return x");
        Assert.Equal(15, Assert.IsType<int>(result));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Eval — basic types
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eval_Integer()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return 42");

        Assert.Equal(42, Assert.IsType<int>(result));
    }

    [Fact]
    public void Eval_Double()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return 3.14");

        Assert.IsType<double>(result);
        Assert.Equal(3.14, (double)result!, 10);
    }

    [Fact]
    public void Eval_String()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return 'hello'");

        Assert.Equal("hello", result);
    }

    [Fact]
    public void Eval_BoolTrue()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return true");

        Assert.Equal(true, result);
    }

    [Fact]
    public void Eval_BoolFalse()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return false");

        Assert.Equal(false, result);
    }

    [Fact]
    public void Eval_Nil()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return nil");

        Assert.Null(result);
    }

    [Fact]
    public void Eval_NoReturn_ReturnsNull()
    {
        var engine = CreateEngine();
        var result = engine.Eval("local x = 1");

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Eval — expressions
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eval_ArithmeticExpression()
    {
        var engine = CreateEngine();
        Assert.Equal(15, Assert.IsType<int>(engine.Eval("return 5 * 3")));
    }

    [Fact]
    public void Eval_StringConcatenation()
    {
        var engine = CreateEngine();
        Assert.Equal("foobar", engine.Eval("return 'foo' .. 'bar'"));
    }

    [Fact]
    public void Eval_Comparison()
    {
        var engine = CreateEngine();
        Assert.Equal(true, engine.Eval("return 10 > 5"));
        Assert.Equal(false, engine.Eval("return 10 < 5"));
    }

    [Fact]
    public void Eval_IntegerDivision()
    {
        var engine = CreateEngine();
        Assert.Equal(3, Assert.IsType<int>(engine.Eval("return 7 // 2")));
    }

    [Fact]
    public void Eval_FloatDivision()
    {
        var engine = CreateEngine();
        var result = engine.Eval("return 7 / 2");
        Assert.IsType<double>(result);
        Assert.Equal(3.5, (double)result!);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Eval — state persistence across calls
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eval_GlobalsPersistedAcrossCalls()
    {
        var engine = CreateEngine();
        engine.Run("myVar = 'persisted'");

        Assert.Equal("persisted", engine.Eval("return myVar"));
    }

    [Fact]
    public void Eval_FunctionDefinedThenCalled()
    {
        var engine = CreateEngine();
        engine.Run("function double(x) return x * 2 end");

        Assert.Equal(20, Assert.IsType<int>(engine.Eval("return double(10)")));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Eval — errors
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eval_SyntaxError_Throws()
    {
        var engine = CreateEngine();
        Assert.Throws<LuaException>(() => engine.Eval("return +++"));
    }

    [Fact]
    public void Eval_RuntimeError_Throws()
    {
        var engine = CreateEngineWithBase();
        Assert.Throws<LuaException>(() => engine.Eval("return error('boom')"));
    }

    [Fact]
    public void Eval_StackCleanAfterError()
    {
        var engine = CreateEngineWithBase();

        try { engine.Eval("return error('fail')"); } catch { }

        // Engine should still work after error
        Assert.Equal(42, Assert.IsType<int>(engine.Eval("return 42")));
    }

    // ──────────────────────────────────────────────────────────────────────
    // EvalMultiple
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvalMultiple_MultipleValues()
    {
        var engine = CreateEngine();
        var results = engine.EvalMultiple("return 1, 'two', true");

        Assert.Equal(3, results.Length);
        Assert.Equal(1, Assert.IsType<int>(results[0]));
        Assert.Equal("two", results[1]);
        Assert.Equal(true, results[2]);
    }

    [Fact]
    public void EvalMultiple_SingleValue()
    {
        var engine = CreateEngine();
        var results = engine.EvalMultiple("return 99");

        Assert.Single(results);
        Assert.Equal(99, Assert.IsType<int>(results[0]));
    }

    [Fact]
    public void EvalMultiple_NoReturn()
    {
        var engine = CreateEngine();
        var results = engine.EvalMultiple("local x = 1");

        Assert.Empty(results);
    }

    [Fact]
    public void EvalMultiple_MixedTypes()
    {
        var engine = CreateEngine();
        var results = engine.EvalMultiple("return nil, 3.14, false, 'end'");

        Assert.Equal(4, results.Length);
        Assert.Null(results[0]);
        Assert.Equal(3.14, (double)results[1]!);
        Assert.Equal(false, results[2]);
        Assert.Equal("end", results[3]);
    }

    [Fact]
    public void EvalMultiple_StackCleanedUp()
    {
        var engine = CreateEngine();

        // Call multiple times — stack should not grow
        for (int i = 0; i < 100; i++)
        {
            var results = engine.EvalMultiple("return 1, 2, 3");
            Assert.Equal(3, results.Length);
        }

        // Should still work
        Assert.Equal(42, Assert.IsType<int>(engine.Eval("return 42")));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Eval with libraries
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eval_WithMathLibrary()
    {
        var engine = CreateEngineWithBase();
        var result = engine.Eval("return math.abs(-42)");

        Assert.Equal(42, Assert.IsType<int>(result));
    }

    [Fact]
    public void Eval_WithStringLibrary()
    {
        var engine = CreateEngineWithBase();
        var result = engine.Eval("return string.upper('hello')");

        Assert.Equal("HELLO", result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Constructor / libraries
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultNoLibs()
    {
        var engine = new LuaEngine();
        // math should not be available
        Assert.Throws<LuaException>(() => engine.Eval("return math.abs(-1)"));
    }

    [Fact]
    public void Constructor_WithLibs()
    {
        var engine = new LuaEngine(LuaLibrary.Math, LuaLibrary.None);
        Assert.Equal(42, Assert.IsType<int>(engine.Eval("return math.abs(-42)")));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Run stack cleanup
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Run_StackCleanedUpAfterExecution()
    {
        var engine = CreateEngine();

        for (int i = 0; i < 100; i++)
        {
            engine.Run("local x = 1");
        }

        Assert.Equal(1, Assert.IsType<int>(engine.Eval("return 1")));
    }

    [Fact]
    public void Run_StackCleanedUpAfterError()
    {
        var engine = CreateEngineWithBase();

        try { engine.Run("error('fail')"); } catch { }

        Assert.Equal("ok", engine.Eval("return 'ok'"));
    }
}
