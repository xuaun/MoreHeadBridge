using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

// Prefabs MoreHead already loaded, so the bridge doesn't open the same .hhh a second time.
internal static class MoreHeadPrefabs
{
    private static Dictionary<string, GameObject>? _byPath;
    private static Transform? _holder;

    internal static int Cloned { get; private set; }

    internal static bool Has(string path) => ByPath().ContainsKey(Path.GetFullPath(path));

    // A copy of MoreHead's prefab: meshes, textures and materials stay shared, only the objects are new. Null without MoreHead or for a file it didn't keep (its blacklist).
    internal static GameObject? Clone(string path)
    {
        if (!ByPath().TryGetValue(Path.GetFullPath(path), out var prefab) || prefab == null) return null;

        var clone = Object.Instantiate(prefab, Holder());
        clone.name = prefab.name;
        Cloned++;
        return clone;
    }

    // Inactive, so Awake doesn't run on the clones. The flags stay on the holder: Instantiate copies them to every instance.
    private static Transform Holder()
    {
        if (_holder != null) return _holder;

        var holder = new GameObject("MHB_PrefabHolder");
        holder.SetActive(false);
        holder.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(holder);
        return _holder = holder.transform;
    }

    // Same reflection as CosmeticPrefabFixer. Anything missing leaves the list empty, and every cosmetic opens its file as before.
    private static Dictionary<string, GameObject> ByPath()
    {
        if (_byPath != null) return _byPath;

        _byPath = new(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            var manager = System.Type.GetType("MoreHead.HeadDecorationManager, MoreHead");
            if (manager?.GetProperty("Decorations")?.GetValue(null) is not System.Collections.IList list || list.Count == 0)
                return _byPath;

            var type = list[0]!.GetType();
            var prefabProp = type.GetProperty("Prefab");
            var pathProp = type.GetProperty("BundlePath");
            foreach (var item in list)
                if (prefabProp?.GetValue(item) is GameObject prefab && pathProp?.GetValue(item) is string path)
                    _byPath[Path.GetFullPath(path)] = prefab;
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"MoreHeadPrefabs: could not read MoreHead's list: {ex.Message}");
        }
        return _byPath;
    }
}
