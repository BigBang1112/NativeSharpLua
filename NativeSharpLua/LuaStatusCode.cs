namespace NativeSharpLua;

public enum LuaStatusCode
{
    Ok,
    Yield,
    ErrRun,
    ErrSyntax,
    ErrMem,
    ErrGcmm,
    ErrErr
}