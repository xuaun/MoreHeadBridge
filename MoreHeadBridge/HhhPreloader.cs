using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

// Opens .hhh files in the background at startup, so MoreHead's or the bridge's next LoadFromFile finds its file ready.
// Nothing opens before the first request, and a normal call only runs with nothing of ours open.
// Runs before Plugin.Awake, so it logs through the preload plugin instead of BridgeLog.
internal static class HhhPreloader
{
    internal const string OptionKey = "ParallelCosmeticLoading";
    internal const bool EnabledByDefault = true;

    private const int MinFiles = 16;
    private const int MaxOpenAtOnce = 24;
    private const long MaxOpenBytes = 128L * 1024 * 1024;   // Size on disk. An open file takes about 6 times that in memory.
    private const int MaxStops = 3;

    private static readonly Dictionary<string, (AssetBundleCreateRequest Request, long Size)> _inFlight = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly List<(string Path, long Size)> _files = [];
    private static ManualLogSource? _log;
    private static Harmony? _harmony;
    private static int _next, _openAtOnce, _served, _stops;
    private static long _openBytes;
    private static bool _started;

    // True while the next LoadFromFile may be served from here. LoadFromMemory would collide with a file open here.
    internal static bool Active => _harmony != null && (_next < _files.Count || _inFlight.Count > 0);

    internal static void Begin(ManualLogSource log)
    {
        _log = log;
        try
        {
            int threads = System.Environment.ProcessorCount;
            if (threads < 2 || !OptionEnabled()) return;

            var files = FilesInOpeningOrder();
            if (files.Count < MinFiles) return;

            _files.AddRange(files);
            _openAtOnce = Mathf.Min(MaxOpenAtOnce, threads * 4);
            _harmony = new Harmony(MyPluginInfo.PRELOAD_GUID);
            _harmony.Patch(AccessTools.Method(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile), [typeof(string)]),
                prefix: new HarmonyMethod(typeof(HhhPreloader), nameof(LoadFromFilePrefix)));
        }
        catch (System.Exception ex)
        {
            log.LogWarning($"Could not start: {ex.Message}");
            Finish();
        }
    }

    internal static void Finish()
    {
        if (_harmony == null) return;

        try
        {
            CloseInFlight();
            _files.Clear();
            _harmony.UnpatchSelf();
        }
        catch (System.Exception ex)
        {
            _log?.LogWarning($"Could not finish: {ex.Message}");
        }
        _harmony = null;
        if (_served > 0) _log?.LogInfo($"Opened {_served} .hhh file(s) in the background, up to {_openAtOnce} at a time");
    }

    private static bool LoadFromFilePrefix(string path, ref AssetBundle? __result)
    {
        if (_files.Count == 0 || path == null || !path.EndsWith(".hhh", System.StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            if (!_started && !StartAt(path)) return true;

            if (_inFlight.TryGetValue(path, out var open))
            {
                _inFlight.Remove(path);
                _openBytes -= open.Size;
                var bundle = open.Request.assetBundle;
                if (bundle != null)
                {
                    __result = bundle;
                    _served++;
                    TopUp();
                    return false;
                }
            }

            Stop(path);
        }
        catch (System.Exception ex)
        {
            _log?.LogWarning($"Error on '{Path.GetFileName(path)}': {ex.Message}");
            _files.Clear();
        }
        return true;
    }

    // The first file of the list to be requested says where to start. Before it nothing is open, so other .hhh files load as usual.
    private static bool StartAt(string path)
    {
        int first = _files.FindIndex(file => string.Equals(file.Path, path, System.StringComparison.OrdinalIgnoreCase));
        if (first < 0) return false;

        _started = true;
        _next = first;
        TopUp();
        return true;
    }

    // A file over the size limit still opens, alone.
    private static void TopUp()
    {
        while (_inFlight.Count < _openAtOnce && _next < _files.Count)
        {
            var (file, size) = _files[_next];
            if (_inFlight.Count > 0 && _openBytes + size > MaxOpenBytes) return;

            _next++;
            _openBytes += size;
            _inFlight[file] = (AssetBundle.LoadFromFileAsync(file), size);
        }
    }

    // Out of order, or the background open failed (a broken file, or two files with the same internal name). The next request starts over from its own place.
    // Several files failing together, or too many stops, leave the rest to load one by one.
    private static void Stop(string path)
    {
        bool drained = _next >= _files.Count && _inFlight.Count == 0;
        int failed = CloseInFlight();
        _started = false;

        if (drained)
        {
            _files.Clear();
        }
        else if (failed > 0 || ++_stops > MaxStops)
        {
            _files.Clear();
            _log?.LogWarning($"Stopped at '{Path.GetFileName(path)}': the remaining .hhh files load one by one");
        }
    }

    // Returns how many of them failed to open.
    private static int CloseInFlight()
    {
        var open = _inFlight.Values.ToArray();
        _inFlight.Clear();
        _openBytes = 0;

        int failed = 0;
        foreach (var (request, _) in open)
        {
            var bundle = request.assetBundle;
            if (bundle != null) bundle.Unload(true);
            else failed++;
        }
        return failed;
    }

    // The bridge's Config doesn't exist yet, so this reads the option's line from its .cfg.
    private static bool OptionEnabled()
    {
        string cfg = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID + ".cfg");
        if (!File.Exists(cfg)) return EnabledByDefault;

        foreach (string line in File.ReadAllLines(cfg))
        {
            if (!line.StartsWith(OptionKey, System.StringComparison.Ordinal)) continue;

            int equals = line.IndexOf('=');
            if (equals >= 0 && bool.TryParse(line.Substring(equals + 1).Trim(), out bool value)) return value;
        }
        return EnabledByDefault;
    }

    // MoreHead's order: the Decorations folder next to its DLL, then all of plugins. Without MoreHead, the bridge's order. Both skip files under 1 KB.
    private static List<(string Path, long Size)> FilesInOpeningOrder()
    {
        var files = new List<string>();
        foreach (string dll in Directory.GetFiles(Paths.PluginPath, "MoreHead.dll", SearchOption.AllDirectories))
        {
            string own = Path.Combine(Path.GetDirectoryName(dll)!, "Decorations");
            if (Directory.Exists(own)) files.AddRange(Directory.GetFiles(own, "*.hhh"));
        }
        files.AddRange(Directory.GetFiles(Paths.PluginPath, "*.hhh", SearchOption.AllDirectories));
        return files.Distinct()
            .Select(file => (Path: file, Size: new FileInfo(file).Length))
            .Where(file => file.Size >= 1024)
            .ToList();
    }
}
