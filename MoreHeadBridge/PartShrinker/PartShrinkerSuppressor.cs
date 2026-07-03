// Three layers silencing MoreHeadUtilities.PartShrinker noise: (1) ILogHandler filter (C# Debug.Log* path); (1b) Harmony prefix on UnityLogSource.OnUnityLogMessageReceived for when MHU is ABSENT (Unity's serializer warnings bypass ILogHandler) — both must run BEFORE LoadAll(); (2) Harmony NRE finalizer (MHU present) swallowing PartShrinker NREs on cosmetics in unexpected hierarchies (menu / death-head anchors).

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class PartShrinkerSuppressor
{
    // ── Layer 1: ILogHandler filter ───────────────────────────────────────────

    /// Install a filtering ILogHandler that suppresses MoreHeadUtilities.PartShrinker
    /// warnings coming through the C# Debug.Log* path.  Called early in Plugin.Awake
    /// before any .hhh files are loaded.
    internal static void InstallWarningFilter()
    {
        try
        {
            if (Debug.unityLogger.logHandler is PartShrinkerLogFilter) return;
            Debug.unityLogger.logHandler = new PartShrinkerLogFilter(Debug.unityLogger.logHandler);
            BridgeLog.Trace("PartShrinker ILogHandler filter installed");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Could not install PartShrinker ILogHandler filter: {ex.Message}");
        }
    }

    private sealed class PartShrinkerLogFilter : ILogHandler
    {
        private readonly ILogHandler _inner;
        private bool _suppressNext;

        internal PartShrinkerLogFilter(ILogHandler inner) => _inner = inner;

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            _suppressNext = false;
            _inner.LogException(exception, context);
        }

        public void LogFormat(LogType logType, UnityEngine.Object context,
                              string format, params object[] args)
        {
            if (logType == LogType.Warning)
            {
                var msg = (args?.Length > 0 ? args[0]?.ToString() : null) ?? format ?? "";

                if (msg.Contains("MoreHeadUtilities.PartShrinker"))
                {
                    _suppressNext = true;
                    return;
                }

                if (_suppressNext && msg.Contains("referenced script on this Behaviour"))
                {
                    _suppressNext = false;
                    return;
                }
            }

            _suppressNext = false;
            _inner.LogFormat(logType, context, format, args);
        }
    }

    // ── Layer 1b: BepInEx log-source patch ───────────────────────────────────

    private static bool _suppressNextUnityLog;
    // Accumulated stats since the last FlushSuppressedLog() call.
    private static int  _suppressedCount;
    private static readonly HashSet<string> _suppressedGoNames = new();

    /// Suppresses Unity's native "missing script" warnings for PartShrinker before they reach any log sink.
    /// Call BEFORE LoadAll() so bundle-load warnings are caught too; no-op when MHU is present (no warnings are emitted then).
    internal static void InstallNativeWarningFilter(Harmony harmony)
    {
        try
        {
            var bepInExType = AccessTools.TypeByName("BepInEx.Logging.UnityLogSource");
            var onUnityLog  = bepInExType != null
                              ? AccessTools.Method(bepInExType, "OnUnityLogMessageReceived")
                              : null;
            if (onUnityLog == null)
            {
                BridgeLog.Trace(
                    "BepInEx.Logging.UnityLogSource.OnUnityLogMessageReceived not found — " +
                    "native PartShrinker warning filter unavailable");
                return;
            }

            var prefix = typeof(PartShrinkerSuppressor).GetMethod(
                nameof(UnityLogSourcePrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(onUnityLog, prefix: new HarmonyMethod(prefix));
            BridgeLog.Trace("PartShrinker native warning filter installed (BepInEx log source patched)");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Could not install PartShrinker native warning filter: {ex.Message}");
        }
    }

    // ── Layer 2: Harmony NRE finalizer ───────────────────────────────────────

    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var t = MoreHeadUtilitiesTypes.FindType("MoreHeadUtilities.PartShrinker");
            if (t == null)
            {
                BridgeLog.Trace("MoreHeadUtilities not loaded — PartShrinker NRE suppressor skipped");
                return;
            }

            // Layer 2: swallow NREs from PartShrinker methods when MHU IS installed.
            var finalizer = typeof(PartShrinkerSuppressor).GetMethod(
                nameof(NreFinalizer),
                BindingFlags.Static | BindingFlags.NonPublic);

            int patched = 0;
            foreach (var methodName in new[] { "OnDisable", "Update" })
            {
                var target = AccessTools.Method(t, methodName);
                if (target == null) continue;
                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
                patched++;
            }

            if (patched > 0)
                BridgeLog.Trace($"PartShrinker NRE suppressor loaded ({patched} method(s))");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Could not install PartShrinker suppressor: {ex.Message}");
        }
    }

    /// Emit a [Warning] with the total count of suppressed PartShrinker warnings and
    /// [Debug] lines for each unique GO name collected since the last call.  Call this
    /// after any loading phase that generates these warnings (e.g. after LoadAll()).
    internal static void FlushSuppressedLog()
    {
        if (_suppressedCount == 0) return;

        BceConsole.LogWarning(
            $"Suppressed {_suppressedCount} PartShrinker 'missing script' warning(s) " +
            $"— MoreHeadUtilities is not installed.  " +
            $"Install it to fix this (no gameplay impact).");

        foreach (var name in _suppressedGoNames)
            BridgeLog.Trace($"  PartShrinker missing on GO: '{name}'");

        _suppressedCount = 0;
        _suppressedGoNames.Clear();
    }

    // Prefix on UnityLogSource.OnUnityLogMessageReceived — BepInEx's parameter is named "message", NOT "condition" (Harmony matches by name); false drops PartShrinker warnings. Unity emits up to THREE warnings per missing script (anonymous '<null>'-context line(s), the type-name warning, the named-GO follow-up); suppress all three and accumulate stats for FlushSuppressedLog().
    private static bool UnityLogSourcePrefix(string message, LogType type)
    {
        if (type == LogType.Warning && message != null)
        {
            // (b) Type-name warning — suppress, count, and flag for (c).
            if (message.Contains("MoreHeadUtilities.PartShrinker"))
            {
                _suppressedCount++;
                _suppressNextUnityLog = true;
                return false;
            }

            // (c) Named-GO follow-up after (b) — record the GO name.
            if (_suppressNextUnityLog && message.Contains("referenced script on this Behaviour"))
            {
                _suppressNextUnityLog = false;
                var goName = ExtractGoName(message);
                if (goName != null) _suppressedGoNames.Add(goName);
                return false;
            }

            // (a) Anonymous <null>-context warning that precedes (b).
            if (message.Contains("referenced script on this Behaviour") &&
                message.Contains("'<null>'"))
            {
                return false;
            }
        }

        _suppressNextUnityLog = false;
        return true;
    }

    // Extracts the GO name from "...(Game Object 'X') is missing!"; null for empty or '<null>'.
    private static string? ExtractGoName(string message)
    {
        const string marker = "(Game Object '";
        int start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = message.IndexOf("')", start, StringComparison.Ordinal);
        if (end <= start) return null;
        var name = message.Substring(start, end - start);
        return string.IsNullOrEmpty(name) || name == "<null>" ? null : name;
    }

    private static Exception? NreFinalizer(Exception? __exception)
    {
        if (__exception is NullReferenceException) return null;
        return __exception;
    }
}
