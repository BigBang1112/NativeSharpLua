using NativeSharpLua;

var lua = new LuaEngine(LuaLibrary.Base | LuaLibrary.Debug, LuaLibrary.Base | LuaLibrary.Debug);

lua.Run(@"
    function greet(name)
        return 'Hello, ' .. name .. '!'
    end
    print(greet('World'))
");

lua.ObjectRegistry.RegisterObject(new TestObject(), "testObj");

lua.Run(@"
print(testObj)
testObj:SayHello()
local message = testObj:GetMessage('Info')
print(message)
print('Nested Object Description: ' .. testObj.NestedObject.Description)
print('Dump of testObj: ' .. testObj:dumpJSON())
");

Console.ReadLine();

internal class TestObject
{
    public string Name { get; set; } = "Test Object";
    public int Value { get; set; } = 42;
    public double Number { get; set; } = 3.14;
    public AnotherTestObject NestedObject { get; set; } = new AnotherTestObject();

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

internal class AnotherTestObject
{
    public string Description { get; set; } = "Another Test Object";
}