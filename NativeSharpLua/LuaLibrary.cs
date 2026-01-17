namespace NativeSharpLua;

[Flags]
public enum LuaLibrary
{
    None = 0,
    Base = 1,
    Package = 2,
    Coroutine = 4,
    Debug = 8,
    IO = 16,
    Math = 32,
    OS = 64,
    String = 128,
    Table = 256,
    UTF8 = 512
}
