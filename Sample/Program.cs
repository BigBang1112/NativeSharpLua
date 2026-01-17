using NativeSharpLua;

var lua = new LuaEngine();

lua.Run(@"
    function greet(name)
        return 'Hello, ' .. name .. '!'
    end
    print(greet('World'))
");

lua.RegisterObject(new TestObject(), "testObj");

lua.Run(@"
    print(testObj)
    testObj:SayHello()
    local message = testObj:GetMessage('Info')
    print(message)
");

internal class TestObject
{
    public string Name { get; set; } = "Test Object";
    public int Value { get; set; } = 42;
    public double Number { get; set; } = 3.14;

    public void SayHello()
    {
        Console.WriteLine($"Hello from {Name}!");
    }

    public string GetMessage(string prefix)
    {
        return $"{prefix}: {Name} has value {Value}";
    }

    public override string ToString()
    {
        return $"TestObject(Name='{Name}', Value={Value}, Number={Number})";
    }
}