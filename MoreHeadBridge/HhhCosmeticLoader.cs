using REPOLib.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

internal static class HhhCosmeticLoader
{
    internal static readonly List<string> RegisteredAssetIds = [];
    internal static readonly HashSet<string> WorldAssetIds = [];

    private static readonly Dictionary<string, string> _sourcePath = new(StringComparer.Ordinal);

    // True when the bridge cosmetic loaded from a plugin subfolder whose path contains <folderName> (case-insensitive). Used by border-theme pack detection.
    internal static bool IsFromFolder(CosmeticAsset? asset, string folderName)
    {
        if (asset == null || string.IsNullOrEmpty(folderName)) return false;
        return _sourcePath.TryGetValue(asset.assetId, out var path)
            && path.IndexOf(folderName, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // The Thunderstore package folder a bridge cosmetic loaded from — the path segment right under
    // "plugins" (e.g. "Author-CosmeticPack"). Null for non-bridge assets or unknown paths.
    internal static string? SourceModFolder(CosmeticAsset? asset)
    {
        if (asset == null) return null;
        if (!_sourcePath.TryGetValue(asset.assetId, out var path) || string.IsNullOrEmpty(path)) return null;

        string[] parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
            if (string.Equals(parts[i], "plugins", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1].Length > 0 ? parts[i + 1] : null;
        return null;
    }

    // assetId → main texture from the prefab's materials, for GetIconPatch to render a real icon instead of the placeholder.
    internal static readonly Dictionary<string, Texture2D> BridgeIconTextures = new();

    // Unified tag → (vanilla CosmeticType, OverrideCosmeticType) map. Worlds use Hat as vanilla type (keeps type-indexed structures in-bounds); WorldAssetIds drives world logic.
    private static readonly Dictionary<string, (SemiFunc.CosmeticType VanillaType, OverrideCosmeticType OverrideType)> TagMap = new()
    {
        ["head"]     = (SemiFunc.CosmeticType.Hat,        OverrideCosmeticType.Hat),
        ["neck"]     = (SemiFunc.CosmeticType.HeadBottom, OverrideCosmeticType.HeadBottom),
        ["body"]     = (SemiFunc.CosmeticType.BodyTop,    OverrideCosmeticType.BodyTop),
        ["hip"]      = (SemiFunc.CosmeticType.BodyBottom, OverrideCosmeticType.BodyBottom),
        ["rightarm"] = (SemiFunc.CosmeticType.ArmRight,   OverrideCosmeticType.ArmRight),
        ["leftarm"]  = (SemiFunc.CosmeticType.ArmLeft,    OverrideCosmeticType.ArmLeft),
        ["rightleg"] = (SemiFunc.CosmeticType.LegRight,   OverrideCosmeticType.LegRight),
        ["leftleg"]  = (SemiFunc.CosmeticType.LegLeft,    OverrideCosmeticType.LegLeft),
        ["world"]    = (SemiFunc.CosmeticType.Hat,        OverrideCosmeticType.World),
    };

    private static readonly HashSet<string> ValidTags = [.. TagMap.Keys];

    // Tracks names already registered to handle duplicates across mods (like MoreHead does)
    private static readonly HashSet<string> _usedPrefabIds = [];
    private static readonly HashSet<string> _usedInternalNames = [];

    // Blacklist seeding (first run only): MoreHead display names to migrate while registering.
    private static bool _seeding;
    private static HashSet<string>? _seedNames;

    private static bool _moreHeadFixDone;
    // Last rarity applied during OnMenuOpen, to skip the linear scan when the config is unchanged.
    private static SemiFunc.Rarity _lastAppliedRarity = (SemiFunc.Rarity)(-1);

    // Same sentinel for EnableBridgeTinting so OnMenuOpen only re-scans on change (null initial → first open always applies).
    private static bool? _lastAppliedTinting = null;

    // Original override-type per assetId (derived from the .hhh filename tag at load time).
    private static readonly Dictionary<string, OverrideCosmeticType> _originalTypes = new();

    // Auto-detected tintable per assetId (scanner result, ignoring config). ReapplyDefaults uses it to restore the config-gated value instead of hard-coding.
    private static readonly Dictionary<string, bool> _originalTintable = new();

    public static void LoadAll()
    {
        BridgeBlacklist.EnsureLoaded();
        _seeding   = !BridgeBlacklist.ExistedAtStartup;
        _seedNames = _seeding ? BridgeBlacklist.ReadMoreHeadNames() : null;

        string pluginsPath = BepInEx.Paths.PluginPath;
        string[] files = Directory.GetFiles(pluginsPath, "*.hhh", SearchOption.AllDirectories);

        // Optional folder filter: only keep files whose path contains one of the listed subfolder names.
        string filterRaw = Plugin.SpecificFolders.Value ?? "";
        if (!string.IsNullOrWhiteSpace(filterRaw))
        {
            var invalidChars = Path.GetInvalidPathChars();

            var allowed = filterRaw.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(s =>
                {
                    string clean = new string(s.Where(c => !invalidChars.Contains(c)).ToArray());
                    if (clean != s)
                        BceConsole.LogWarning($"SpecificFolders: '{s}' contained invalid path characters — changed to '{clean}'");
                    return clean;
                })
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Determine which of the requested folders actually matched any file.
            var matched  = allowed.Where(a => files.Any(f => f.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            var missing  = allowed.Except(matched, StringComparer.OrdinalIgnoreCase).ToArray();

            if (matched.Length == 0)
            {
                // None of the specified folders found — fall back to loading everything.
                BceConsole.LogWarning(
                    $"SpecificFolders: none of the specified folders were found " +
                    $"({string.Join(", ", allowed)}). Loading all .hhh files instead");
            }
            else
            {
                if (missing.Length > 0)
                    BceConsole.LogWarning(
                        $"SpecificFolders: folder(s) not found and skipped: {string.Join(", ", missing)}");

                int before = files.Length;
                files = files.Where(f => matched.Any(a => f.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                BceConsole.LogInfo($"SpecificFolders: loaded from {string.Join(", ", matched)} — kept {files.Length}/{before} files");
            }
        }

        BceConsole.LogInfo($"Found {files.Length} .hhh file(s). Translating cosmetics from MoreHead to Vanilla REPO...");

        int registered = 0;

        // Bounded read-ahead pipeline: prefetch bytes on the thread pool while the main thread registers earlier files (LoadFromMemory is main-thread). Overlaps I/O with decompression, caps memory, and preserves registration order (dedup/assetIds depend on it).
        int window = System.Math.Max(2, System.Environment.ProcessorCount);
        var inflight = new Queue<(string path, Task<byte[]?> read)>(window);
        int next = 0;
        while (next < files.Length && inflight.Count < window)
        {
            string p = files[next++];
            inflight.Enqueue((p, Task.Run(() => ReadBytesOrNull(p))));
        }
        while (inflight.Count > 0)
        {
            var (file, read) = inflight.Dequeue();
            byte[]? bytes = read.GetAwaiter().GetResult();   // null → TryRegister falls back to LoadFromFile
            if (TryRegister(file, bytes))
                registered++;
            if (next < files.Length)
            {
                string p = files[next++];
                inflight.Enqueue((p, Task.Run(() => ReadBytesOrNull(p))));
            }
        }

        int total = files.Length;
        int skipped = total - registered;

        BceConsole.LogInfo($"Done — {registered}/{total} registered, {skipped} error(s)");

        if (_seeding) BridgeBlacklist.Save();   // always write (even empty) so the next run never re-seeds
        BridgeBlacklist.MirrorToMoreHead();
    }

    // Reads a .hhh file's bytes on a background thread; null on failure or if too small (TryRegister then falls back to LoadFromFile or skips).
    private static byte[]? ReadBytesOrNull(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length >= 1024 ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            // Non-fatal — TryRegister will handle the error with a proper log message.
            return null;
        }
    }

    // bytes == null → fall back to AssetBundle.LoadFromFile (used when pre-read failed).
    private static bool TryRegister(string path, byte[]? bytes = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024)
        {
            BceConsole.LogWarning($"Skipped (too small/missing): {Path.GetFileName(path)}");
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        ParseFileName(fileName, out string internalName, out string tag);

        if (!TagMap.TryGetValue(tag, out var tagEntry))
            return false;

        SemiFunc.CosmeticType cosmeticType = tagEntry.VanillaType;

        // LoadFromMemory when bytes are available (background pre-read); fall back to LoadFromFile if it failed/skipped.
        AssetBundle? bundle = bytes != null
            ? AssetBundle.LoadFromMemory(bytes)
            : AssetBundle.LoadFromFile(path);

        if (bundle == null)
        {
            BceConsole.LogError($"Failed to load bundle: {fileName}");
            return false;
        }

        GameObject? prefab = LoadFirstPrefab(bundle);
        bundle.Unload(false);

        if (prefab == null)
        {
            BceConsole.LogError($"No GameObject in bundle: {fileName}");
            return false;
        }

        // Some .hhh prefabs ship their ROOT GameObject disabled (MoreHead activates it on equip). Instantiate preserves activeSelf, so force the root active once here; children left as authored (variant meshes may start disabled).
        if (!prefab.activeSelf)
            prefab.SetActive(true);

        // Deduplicate prefab network ID (same as MoreHead's EnsureUniqueName logic)
        string basePrefabName = prefab.name;
        prefab.name = EnsureUniqueId(basePrefabName, _usedPrefabIds);

        // Deduplicate internal name used for assetId
        string baseInternal = internalName;
        internalName = EnsureUniqueId(internalName, _usedInternalNames);

        // Emit a single warning even when both names changed (avoids two log lines for the same file).
        bool prefabRenamed   = prefab.name  != basePrefabName;
        bool internalRenamed = internalName != baseInternal;

        if (prefabRenamed && internalRenamed && basePrefabName == baseInternal)
        {
            // Both names shared the same base and both got the same suffix — one line suffices.
            if (prefab.name == internalName)
                BceConsole.LogWarning(
                    $"Duplicate name '{basePrefabName}' — renamed internal and prefab to '{internalName}'.",
                    ConsoleColor.DarkYellow);
            else
                BceConsole.LogWarning(
                    $"Duplicate name '{basePrefabName}' — prefab renamed to '{prefab.name}', " +
                    $"internal id to '{internalName}'.",
                    ConsoleColor.DarkYellow);
        }
        else
        {
            if (prefabRenamed)
                BceConsole.LogWarning($"Duplicate prefab name '{basePrefabName}' → renamed to '{prefab.name}'", ConsoleColor.DarkYellow);
            if (internalRenamed)
                BceConsole.LogWarning($"Duplicate internal id '{baseInternal}' → renamed to '{internalName}'", ConsoleColor.DarkYellow);
        }

        string assetId = $"{BridgeIds.Prefix}{internalName.ToLowerInvariant()}";

        // Blacklist: seed from MoreHead on the first run (no bridge list yet), then honor our own list.
        bool freshSeed = _seeding && _seedNames != null && _seedNames.Contains(basePrefabName);
        if (freshSeed) BridgeBlacklist.Add(assetId, basePrefabName);

        bool blacklistHidden = false;
        if (BridgeBlacklist.Contains(assetId))
        {
            if (Plugin.BridgeBlacklistMode.Value == BlacklistLoadMode.NotLoadIngame)
            {
                UnityEngine.Object.Destroy(prefab);   // skip registration entirely
                return false;
            }
            blacklistHidden = true;   // LoadHidden: register, then hide on a fresh seed
        }

        var cosmetic = prefab.GetComponent<Cosmetic>();
        if (cosmetic == null)
        {
            cosmetic = prefab.AddComponent<Cosmetic>();
        }
        cosmetic.type = cosmeticType;

        PrefabRef? prefabRef = NetworkPrefabs.RegisterNetworkPrefab($"Cosmetics/{prefab.name}", prefab);
        if (prefabRef == null)
        {
            BceConsole.LogError($"Failed to register network prefab: {internalName}");
            return false;
        }

        var cosmeticAsset = ScriptableObject.CreateInstance<CosmeticAsset>();
        cosmeticAsset.name = internalName;
        // Display name stays the clean base name (no "(1)" suffix) so translation dictionaries still match; uniqueness lives in assetId/prefab.name (vanilla check uses Contains()).
        cosmeticAsset.assetName = basePrefabName;
        cosmeticAsset.type = cosmeticType;
        cosmeticAsset.prefab = prefabRef;
        cosmeticAsset.assetId = assetId;
        cosmeticAsset.rarity = Plugin.BridgeDefaultRarity.Value;
        cosmeticAsset.customTypeList = [];
        // Tintable if any renderer exposes a known colour property (_AlbedoColor/_BaseColor/_Color/_TintColor) AND EnableBridgeTinting is on; BridgeTintHelper injects a BridgeTintMaterial per renderer at equip.
        bool detectedTintable = BridgeTintHelper.DetectTintable(prefab);
        _originalTintable[assetId] = detectedTintable;
        cosmeticAsset.tintable = Plugin.EnableBridgeTinting.Value && detectedTintable;

        _originalTypes[assetId] = tagEntry.OverrideType;

        // Apply any per-cosmetic override (rarity / type) before registering.
        CustomizerStore.ApplyIfPresent(cosmeticAsset);

        Cosmetics.RegisterCosmetic(cosmeticAsset);

        // LoadHidden: start a freshly-seeded cosmetic hidden in the menu (user can un-hide it later).
        if (blacklistHidden && freshSeed)
            BridgeFavoritesManager.EnsureHidden(cosmeticAsset);

        RegisteredAssetIds.Add(assetId);
        _sourcePath[assetId] = path;   // remember the source folder for pack-based border theming
        if (tag == "world")
            WorldAssetIds.Add(assetId);

        // Pull the prefab's albedo/main texture for use as the UI icon.
        var iconTex = TryExtractIconTexture(prefab);
        if (iconTex != null)
            BridgeIconTextures[assetId] = iconTex;

        return true;
    }

    // Loads the first GameObject asset out of a bundle (bridge/MoreHead .hhh hold exactly one).
    private static GameObject? LoadFirstPrefab(AssetBundle bundle)
    {
        foreach (string assetName in bundle.GetAllAssetNames())
        {
            var obj = bundle.LoadAsset<GameObject>(assetName);
            if (obj != null) return obj;
        }
        return null;
    }

    // Walks the prefab's renderers and pulls the first usable Texture2D off their shared materials.
    private static Texture2D? TryExtractIconTexture(GameObject prefab)
    {
        var renderers = prefab.GetComponentsInChildren<Renderer>(includeInactive: true);
        string[] propsToTry =
        {
            "_MainTex",
            "_BaseMap",
            "_BaseColorMap",
            "_Albedo",
            "_AlbedoMap",
        };

        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;

                foreach (string prop in propsToTry)
                {
                    if (!mat.HasProperty(prop)) continue;
                    if (mat.GetTexture(prop) is Texture2D t && t != null)
                        return t;
                }
            }
        }
        return null;
    }

    private static void ParseFileName(string fileName, out string name, out string tag)
    {
        int lastUnderscore = fileName.LastIndexOf('_');
        if (lastUnderscore >= 0)
        {
            string candidate = fileName[(lastUnderscore + 1)..].ToLowerInvariant();
            if (ValidTags.Contains(candidate))
            {
                name = fileName[..lastUnderscore];
                tag = candidate;
                return;
            }
        }
        name = fileName;
        tag = "head";
    }

    private static string EnsureUniqueId(string baseName, HashSet<string> used)
    {
        string name = baseName;
        int counter = 1;
        while (!used.Add(name))
        {
            name = $"{baseName}({counter})";
            counter++;
        }
        return name;
    }

    internal static bool IsWorldAsset(CosmeticAsset? asset)
        => asset != null && BridgeIds.IsBridgeAsset(asset) && WorldAssetIds.Contains(asset.assetId);

    /// The loader-original type for a bridge assetId (from the .hhh filename tags), independent of
    /// any per-cosmetic Type override currently applied to the shared CosmeticAsset.
    internal static bool TryGetOriginalType(string assetId, out OverrideCosmeticType type)
        => _originalTypes.TryGetValue(assetId, out type);

    /// Re-applies the EnableBridgeTinting config to every bridge cosmetic that has no
    /// per-cosmetic Tintable override. Called immediately from the SettingChanged event
    /// (when MetaManager may be ready) and again from OnMenuOpen (guaranteed ready).
    internal static void RefreshTintableFlags()
    {
        if (MetaManager.instance == null) return;
        bool enabled = Plugin.EnableBridgeTinting.Value;
        _lastAppliedTinting = enabled;

        foreach (var asset in MetaManager.instance.cosmeticAssets)
        {
            if (asset == null || !BridgeIds.IsBridgeAsset(asset)) continue;
            // Per-cosmetic override takes priority — leave those alone.
            if (CustomizerStore.TryGet(asset.assetId, out var data) && data.Tintable != null) continue;
            if (_originalTintable.TryGetValue(asset.assetId, out bool detected))
                asset.tintable = enabled && detected;
        }
    }

    internal static void RefreshDefaultRarity()
    {
        if (MetaManager.instance == null) return;
        var rarity = Plugin.BridgeDefaultRarity.Value;
        _lastAppliedRarity = rarity;

        foreach (var asset in MetaManager.instance.cosmeticAssets)
        {
            if (asset == null || !BridgeIds.IsBridgeAsset(asset)) continue;
            if (CustomizerStore.TryGet(asset.assetId, out var data) && data.Rarity != null) continue;
            asset.rarity = rarity;
        }
    }

    internal static bool TryGetDefaultTintable(CosmeticAsset asset, out bool tintable)
    {
        tintable = false;
        if (asset == null || !BridgeIds.IsBridgeAsset(asset)) return false;
        if (!_originalTintable.TryGetValue(asset.assetId, out bool detected)) return false;
        tintable = Plugin.EnableBridgeTinting.Value && detected;
        return true;
    }

    /// Called on each menu open.
    /// Re-applies BridgeDefaultRarity to bridge cosmetics that have no per-cosmetic override,
    /// and runs the one-time MoreHead prefab fix pass if enabled.
    internal static void OnMenuOpen(MenuPageCosmetics page)
    {
        if (MetaManager.instance == null) return;
        var rarity = Plugin.BridgeDefaultRarity.Value;

        // Only re-scan when BridgeDefaultRarity changed since the last menu open.
        if (rarity != _lastAppliedRarity)
            RefreshDefaultRarity();

        // Re-scan EnableBridgeTinting when it changed since the last menu open (covers SettingChanged firing before MetaManager was ready).
        bool tinting = Plugin.EnableBridgeTinting.Value;
        if (_lastAppliedTinting != tinting)
            RefreshTintableFlags();

        if (!_moreHeadFixDone
            && (Plugin.RemoveBridgePhysics.Value || Plugin.LoopBridgeAnimation.Value))
        {
            _moreHeadFixDone = true;
            page.StartCoroutine(CosmeticPrefabFixer.TryFixMoreHeadPrefabsAsync());
        }
    }

    /// Restores a bridge cosmetic's rarity, type, and tintable flag to the loader defaults.
    internal static void ReapplyDefaults(CosmeticAsset asset)
    {
        asset.rarity = Plugin.BridgeDefaultRarity.Value;

        if (!_originalTypes.TryGetValue(asset.assetId, out var originalType))
            return;

        var (cosmeticType, isWorld) = CustomizerStore.MapOverrideToVanilla(originalType);
        asset.type = cosmeticType;

        if (isWorld)
            WorldAssetIds.Add(asset.assetId);
        else
            WorldAssetIds.Remove(asset.assetId);

        // Restore tintable to the config-gated auto-detected value.
        if (_originalTintable.TryGetValue(asset.assetId, out bool detectedTintable))
            asset.tintable = Plugin.EnableBridgeTinting.Value && detectedTintable;
    }
}
