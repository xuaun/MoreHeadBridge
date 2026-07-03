using System;
using System.Reflection;

namespace MoreHeadBridge;

internal static class BceConsole
{
    private const string InfoPrefix  = "[Info   :  MoreHead Bridge] ";
    private const string WarnPrefix  = "[Warning:  MoreHead Bridge] ";
    private const string ErrorPrefix = "[Error  :  MoreHead Bridge] ";
    private const string DebugPrefix = "[Debug  :  MoreHead Bridge] ";

    private static readonly Action<string, ConsoleColor>? _writeLineDelegate;
    private static readonly Action<string, ConsoleColor>? _writeDelegate;

    static BceConsole()
    {
        // optional — located via reflection, no hard dep on BCE.dll
        var t = Type.GetType("BCE.console, BCE");
        if (t == null) return;

        try
        {
            var wlMethod = t.GetMethod("WriteLine", [typeof(string), typeof(ConsoleColor)]);
            var wMethod  = t.GetMethod("Write",     [typeof(string), typeof(ConsoleColor)]);

            if (wlMethod != null)
                _writeLineDelegate = (Action<string, ConsoleColor>)
                    Delegate.CreateDelegate(typeof(Action<string, ConsoleColor>), null, wlMethod);
            if (wMethod != null)
                _writeDelegate = (Action<string, ConsoleColor>)
                    Delegate.CreateDelegate(typeof(Action<string, ConsoleColor>), null, wMethod);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogWarning(
                $"BceConsole: delegate creation failed ({ex.Message}). " +
                "BCE output disabled — falling back to BepInEx logger.");
        }
    }

    internal static bool IsAvailable => _writeLineDelegate != null;

    internal static void WriteLine(string msg, ConsoleColor color)
        => _writeLineDelegate?.Invoke(msg, color);

    internal static void Write(string msg, ConsoleColor color)
        => _writeDelegate?.Invoke(msg, color);

    // default: Cyan
    internal static void LogInfo(string msg)
        => LogInfo(msg, ConsoleColor.Cyan);

    internal static void LogInfo(string msg, ConsoleColor color)
    {
        if (IsAvailable)
            WriteLine(InfoPrefix + msg, color);
        else
            Plugin.Logger.LogInfo(msg);
    }

    // default: Yellow
    internal static void LogWarning(string msg)
        => LogWarning(msg, ConsoleColor.Yellow);

    internal static void LogWarning(string msg, ConsoleColor color)
    {
        if (IsAvailable)
            WriteLine(WarnPrefix + msg, color);
        else
            Plugin.Logger.LogWarning(msg);
    }

    internal static void LogError(string msg)
    {
        if (IsAvailable)
            WriteLine(ErrorPrefix + msg, ConsoleColor.Red);
        else
            Plugin.Logger.LogError(msg);
    }

    // Gray Debug channel — user-facing diagnostics gated by ShowBridgeDebugLogs (kept visually
    // distinct from real Warnings). Internal-only diagnostics use Plugin.Logger.LogDebug instead.
    internal static void LogDebug(string msg)
    {
        if (IsAvailable)
            WriteLine(DebugPrefix + msg, ConsoleColor.DarkGray);
        else
            Plugin.Logger.LogDebug(msg);
    }
}
