namespace MoreHeadBridge;

// The mod's three log channels, named for their audience:
//   User*  — player-facing messages (coloured BCE console when installed, BepInEx log otherwise).
//   Debug  — bridge diagnostics gated by the ShowBridgeDebugLogs config (gray BCE lines).
//   Trace  — internal/low-level detail; BepInEx logger only, visible when BepInEx itself logs Debug.
internal static class BridgeLog
{
    internal static void UserInfo(string msg) => BceConsole.LogInfo(msg);
    internal static void UserInfo(string msg, System.ConsoleColor color) => BceConsole.LogInfo(msg, color);
    internal static void UserWarning(string msg) => BceConsole.LogWarning(msg);
    internal static void UserWarning(string msg, System.ConsoleColor color) => BceConsole.LogWarning(msg, color);
    internal static void UserError(string msg) => BceConsole.LogError(msg);

    internal static void Debug(string msg)
    {
        if (Plugin.ShowBridgeDebugLogs.Value) BceConsole.LogDebug(msg);
    }

    internal static void Trace(string msg) => Plugin.Logger.LogDebug(msg);
}
