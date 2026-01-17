using NativeSharpLua;

// Example C# class to test with Lua
public class TestObject
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

    public int Add(int a, int b)
    {
        return a + b;
    }

    public override string ToString()
    {
        return $"TestObject(Name='{Name}', Value={Value}, Number={Number})";
    }
}

public class Calculator
{
    public double Add(double a, double b) => a + b;
    public double Subtract(double a, double b) => a - b;
    public double Multiply(double a, double b) => a * b;
    public double Divide(double a, double b) => b != 0 ? a / b : double.NaN;
}

public class Program
{
    public static void Main(string[] args)
    {
        var lua = new LuaEngine();

        // Create test objects
        var testObj = new TestObject();
        var calculator = new Calculator();

        // Register objects in Lua
        lua.RegisterObject(testObj, "testObj");
        lua.RegisterObject(calculator, "calc");

        // Test basic object access
        lua.Run("""
            print("=== Basic Object Access ===")
            print("Object:", testObj)
            print("Name:", testObj.Name)
            print("Value:", testObj.Value)
            print("Number:", testObj.Number)
            """);

        // Test method calls
        lua.Run("""
            print("\n=== Method Calls ===")
            testObj:SayHello()
            
            local message = testObj:GetMessage("Lua says")
            print(message)
            
            local sum = testObj:Add(10, 20)
            print("10 + 20 =", sum)
            """);

        // Test property modification
        lua.Run("""
            print("\n=== Property Modification ===")
            print("Before - Name:", testObj.Name, "Value:", testObj.Value)
            
            testObj.Name = "Modified from Lua"
            testObj.Value = 999
            
            print("After - Name:", testObj.Name, "Value:", testObj.Value)
            """);

        // Test calculator object
        lua.Run("""
            print("\n=== Calculator Tests ===")
            print("5.5 + 3.2 =", calc:Add(5.5, 3.2))
            print("10.0 - 4.0 =", calc:Subtract(10.0, 4.0))
            print("3.0 * 7.0 =", calc:Multiply(3.0, 7.0))
            print("15.0 / 3.0 =", calc:Divide(15.0, 3.0))
            """);

        // Test object return from method
        lua.Run("""
            print("\n=== Object Creation from Lua ===")
            -- You could extend this to create new objects from Lua if needed
            print("Test complete!")
            """);

        Console.WriteLine($"\nC# side - Final state:");
        Console.WriteLine($"TestObject: {testObj}");
        Console.WriteLine("All tests completed successfully!");
    }
}