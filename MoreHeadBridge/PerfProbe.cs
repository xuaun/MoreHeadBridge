// TEMP-TEST (remove before release, with its call in Plugin.cs): times the cosmetics menu and writes one [PERF] line per action (open menu, change tab, open popup) to the BepInEx log, plus idle samples while the menu is open.

using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

internal static class PerfProbe
{
    private const string OpenLabel = "open cosmetics";
    private const string IdleLabel = "idle";
    private const double SlowFrame = 0.1;        // a frame this long keeps the action's window open
    private const double CalmAfter = 2.0;        // seconds without a slow frame that close the window
    private const double MaxWindow = 30.0;
    private const double IdleWindow = 5.0;
    private const int TopMethods = 14;

    private sealed class Stat
    {
        public int Calls;
        public long Ticks;
        public long MaxTicks;
    }

    private static readonly Dictionary<System.Reflection.MethodBase, Stat> Stats = new();

    private static string? _window;
    private static double _windowStart, _lastSlow, _lastFrame, _worst;
    private static int _frames, _over33, _over100;
    private static int _iconWaits;
    private static double _iconWaitTotal, _iconWaitMax;

    private static readonly (string type, string method)[] GameTargets =
    {
        ("MenuManager", "PageOpen"),
        ("MenuPageCosmetics", "Start"),
        ("MenuPageCosmetics", "RefreshScrollContent"),
        ("MenuElementCosmeticButton", "Awake"),
        ("MenuElementCosmeticButton", "Start"),
        ("MenuElementCosmeticButton", "OnEnable"),
        ("MenuElementCosmeticButton", "OnDisable"),
        ("MenuElementCosmeticButton", "OnDestroy"),
        ("MenuElementCosmeticButton", "UpdateIcon"),
        ("MenuElementCosmeticSection", "UpdateHighlight"),
        ("MenuElementButtonCosmeticCategory", "UpdateHighlight"),
        ("MenuPageMain", "UpdateHighlight"),
        ("MenuPageLobby", "UpdateHighlight"),
        ("MenuPageEsc", "UpdateHighlight"),
        ("MenuScrollBox", "RecalculateScrollHeight"),
        ("CosmeticAsset", "GenerateIconAsync"),
        ("SemiIconMaker", "CreateIconReadback"),
        ("SemiIconMaker+IconReadback", "CreateSprite"),
        ("SemiFunc", "LoadSpriteFromFile"),
        ("SemiFunc", "SpriteFromBytes"),
        ("MoreHeadBridge.RenderedIconCache", "SpriteFromPixels"),
        ("MoreHeadBridge.RenderedIconCache", "SpriteFromPng"),
        ("MenuButton", "Awake"),
        ("MenuSelectableElement", "Awake"),
        ("MenuElementCosmeticSection", "Awake"),
        ("MenuElementCosmeticSection", "Start"),
        ("PlayerAvatarVisuals", "Start"),
        ("MoreHeadBridge.BridgeIconResolver", "Ensure"),
        ("MoreHeadBridge.CosmeticsMenuNav", "WireSections"),
        ("MoreHeadBridge.CosmeticsScrollBuilder", "BuildVirtualScrollContent"),
        ("MoreHeadBridge.CosmeticsScrollBuilder", "BuildWorldScrollContent"),
        ("MoreHeadBridge.CosmeticsScrollBuilder", "BuildVanillaScrollContent"),
        ("MoreHeadBridge.PopupInputGuard", "Init"),
        ("MoreHeadBridge.PopupNav", "Wire"),
        ("MenuLib.MonoBehaviors.REPOPopupPage", "OpenPage"),
    };

    private static readonly (string type, string method)[] MoreHeadTargets =
    {
        ("PlayerAvatarVisualsPatch", "Postfix"),
        ("PlayerAvatarVisualsPatch", "AddNewDecoration"),
        ("PlayerUpdatePatch", "AddMenuDecoration"),
    };

    internal static void TryApply(Harmony harmony)
    {
        foreach (var (type, method) in GameTargets)
            Wrap(harmony, Find(type), method);

        var moreHead = AccessTools.AllAssemblies().FirstOrDefault(a => a.GetName().Name == "MoreHead");
        if (moreHead != null)
        {
            var types = AccessTools.GetTypesFromAssembly(moreHead);
            foreach (var (type, method) in MoreHeadTargets)
                Wrap(harmony, types.FirstOrDefault(t => t.Name == type), method);
        }

        Mark(harmony, typeof(MenuManager), "PageOpen", nameof(OnPageOpen));
        Mark(harmony, typeof(MenuPageCosmetics), "RefreshScrollContent", nameof(OnRefresh));
        Mark(harmony, Find("MenuLib.MonoBehaviors.REPOPopupPage"), "OpenPage", nameof(OnPopup));

        var update = AccessTools.DeclaredMethod(typeof(MenuManager), "Update");
        if (update != null)
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(PerfProbe), nameof(OnFrame)));

        Plugin.Logger.LogInfo("[PERF] probe active");
    }

    private static System.Type? Find(string name)
        => typeof(MenuManager).Assembly.GetType(name)
           ?? typeof(PerfProbe).Assembly.GetType(name)
           ?? AccessTools.TypeByName(name);

    private static void Wrap(Harmony harmony, System.Type? type, string name)
    {
        try
        {
            var method = type != null ? AccessTools.DeclaredMethod(type, name) : null;
            if (method == null)
            {
                Plugin.Logger.LogInfo($"[PERF] not measured: {type?.Name ?? "?"}.{name}");
                return;
            }
            harmony.Patch(method,
                prefix:  new HarmonyMethod(typeof(PerfProbe), nameof(Before)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(PerfProbe), nameof(After))  { priority = Priority.Last });
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogInfo($"[PERF] not measured: {type?.Name ?? "?"}.{name} ({ex.Message})");
        }
    }

    private static void Mark(Harmony harmony, System.Type? type, string name, string marker)
    {
        var method = type != null ? AccessTools.DeclaredMethod(type, name) : null;
        if (method != null)
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PerfProbe), marker) { priority = Priority.First });
    }

    private static void Before(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();

    private static void After(long __state, System.Reflection.MethodBase __originalMethod)
    {
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - __state;
        if (!Stats.TryGetValue(__originalMethod, out var stat))
            Stats[__originalMethod] = stat = new Stat();
        stat.Calls++;
        stat.Ticks += elapsed;
        if (elapsed > stat.MaxTicks) stat.MaxTicks = elapsed;
    }

    private static void OnPageOpen(MenuPageIndex menuPageIndex)
    {
        if (menuPageIndex == MenuPageIndex.Cosmetics) Begin(OpenLabel);
    }

    private static void OnRefresh(MenuPageCosmetics __instance)
    {
        // The first refresh belongs to the menu opening.
        if (_window == OpenLabel && Time.realtimeSinceStartupAsDouble - _windowStart < 3.0) return;
        var category = __instance.selectedCategory;
        Begin($"refresh {(category != null ? category.categoryName : "virtual tab")} / {__instance.selectedSubCategory}");
    }

    private static void OnPopup() => Begin("open popup");

    private static void OnFrame(MenuManager __instance)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double frame = now - _lastFrame;
        _lastFrame = now;

        if (_window == null)
        {
            if (__instance.currentMenuPageIndex == MenuPageIndex.Cosmetics) Begin(IdleLabel);
            return;
        }

        _frames++;
        if (frame > _worst) _worst = frame;
        if (frame > 0.033) _over33++;
        if (frame > SlowFrame)
        {
            _over100++;
            _lastSlow = now;
        }

        double elapsed = now - _windowStart;
        bool done = _window == IdleLabel
            ? elapsed >= IdleWindow
            : elapsed >= MaxWindow || now - System.Math.Max(_lastSlow, _windowStart) >= CalmAfter;
        if (done) Flush(now);
    }

    private static void Begin(string label)
    {
        if (_window != null) Flush(Time.realtimeSinceStartupAsDouble);

        double now = Time.realtimeSinceStartupAsDouble;
        Stats.Clear();
        _window = label;
        _windowStart = now;
        _lastSlow = now;
        _worst = 0;
        _frames = _over33 = _over100 = 0;
        _iconWaits = 0;
        _iconWaitTotal = _iconWaitMax = 0;
    }

    private static void Flush(double now)
    {
        var text = new StringBuilder(512);
        double ticksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double duration = now - _windowStart;

        text.Append(System.FormattableString.Invariant($"[PERF] '{_window}' {duration:F2} s, settled in {_lastSlow - _windowStart:F2} s"));
        text.Append(System.FormattableString.Invariant($" | {_frames} frames, avg {(_frames > 0 ? duration * 1000.0 / _frames : 0):F1} ms, worst {_worst * 1000.0:F1} ms, >33ms {_over33}, >100ms {_over100}"));
        if (MenuManager.instance != null)
            text.Append($" | active buttons {MenuManager.instance.allMenuButtons.Count}");
        if (_iconWaits > 0)
            text.Append(System.FormattableString.Invariant($" | on-screen icon wait {_iconWaits}x, avg {_iconWaitTotal * 1000.0 / _iconWaits:F0} ms, max {_iconWaitMax * 1000.0:F0} ms"));

        foreach (var pair in Stats.OrderByDescending(p => p.Value.Ticks).Take(TopMethods))
        {
            var stat = pair.Value;
            text.Append(System.FormattableString.Invariant($" | {pair.Key.DeclaringType?.Name}.{pair.Key.Name} {stat.Calls}x {stat.Ticks * ticksToMs:F1} ms (max {stat.MaxTicks * ticksToMs:F1})"));
        }

        // Quiet idle samples would only fill the log.
        if (_window != IdleLabel || _over33 > 0 || Stats.Count > 0)
            Plugin.Logger.LogInfo(text.ToString());

        _window = null;
    }

    internal static void NoteIconWait(double seconds)
    {
        _iconWaits++;
        _iconWaitTotal += seconds;
        if (seconds > _iconWaitMax) _iconWaitMax = seconds;
    }
}
