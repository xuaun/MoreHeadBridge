using System;
using System.Reflection;

namespace MoreHeadBridge;

internal static class BceConsole
{
    private static readonly MethodInfo? _writeLine;
    private static readonly MethodInfo? _write;

    static BceConsole()
    {
        // BCE is optional — locate it via reflection so the assembly has no hard dependency on BCE.dll.
        // If BCE is absent, IsAvailable == false and all calls become no-ops.
        var t = Type.GetType("BCE.console, BCE");
        if (t == null) return;
        _writeLine = t.GetMethod("WriteLine", [typeof(string), typeof(ConsoleColor)]);
        _write = t.GetMethod("Write", [typeof(string), typeof(ConsoleColor)]);
    }

    internal static bool IsAvailable => _writeLine != null;

    internal static void WriteLine(string msg, ConsoleColor color)
        => _writeLine?.Invoke(null, [msg, color]);

    internal static void Write(string msg, ConsoleColor color)
        => _write?.Invoke(null, [msg, color]);
}
