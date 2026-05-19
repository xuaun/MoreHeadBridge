using REPOLib.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

internal static class HhhCosmeticLoader
{
    internal static readonly List<string> RegisteredAssetIds = [];
    internal static readonly HashSet<string> WorldAssetIds = [];

    // assetId → main texture pulled from the prefab's materials. Used by GetIconPatch to
    // render a real per-cosmetic icon instead of the generic placeholder.
    internal static readonly Dictionary<string, Texture2D> BridgeIconTextures = new();

    private static readonly Dictionary<string, SemiFunc.CosmeticType> TagToType = new()
    {
        ["head"] = SemiFunc.CosmeticType.Hat,
        ["neck"] = SemiFunc.CosmeticType.HeadBottom,
        ["body"] = SemiFunc.CosmeticType.BodyTop,
        ["hip"] = SemiFunc.CosmeticType.BodyBottom,
        ["rightarm"] = SemiFunc.CosmeticType.ArmRight,
        ["leftarm"] = SemiFunc.CosmeticType.ArmLeft,
        ["rightleg"] = SemiFunc.CosmeticType.LegRight,
        ["leftleg"] = SemiFunc.CosmeticType.LegLeft,
        // World cosmetics use Hat as their vanilla type so CosmeticEquip/Unequip and
        // all other type-indexed vanilla structures stay in-bounds. Rendering conflicts
        // are avoided by WorldCosmeticsSetupPatch, which filters world indices from
        // SetupCosmeticsLogic and spawns them separately. Equip conflicts are avoided
        // by GetCosmeticsToUnequipPatch. WorldAssetIds drives all world-specific logic.
        ["world"] = SemiFunc.CosmeticType.Hat,
    };

    private static readonly HashSet<string> ValidTags = [.. TagToType.Keys];

    // Tracks names already registered to handle duplicates across mods (like MoreHead does)
    private static readonly HashSet<string> _usedPrefabIds = [];
    private static readonly HashSet<string> _usedInternalNames = [];

    // Original type per assetId (derived from the .hhh filename tag at load time).
    private static readonly Dictionary<string, OverrideCosmeticType> _originalTypes = new();

    private static readonly Dictionary<string, OverrideCosmeticType> TagToOverrideType = new()
    {
        ["head"]     = OverrideCosmeticType.Hat,
        ["neck"]     = OverrideCosmeticType.HeadBottom,
        ["body"]     = OverrideCosmeticType.BodyTop,
        ["hip"]      = OverrideCosmeticType.BodyBottom,
        ["rightarm"] = OverrideCosmeticType.ArmRight,
        ["leftarm"]  = OverrideCosmeticType.ArmLeft,
        ["rightleg"] = OverrideCosmeticType.LegRight,
        ["leftleg"]  = OverrideCosmeticType.LegLeft,
        ["world"]    = OverrideCosmeticType.World,
    };

    public static void LoadAll()
    {
        string pluginsPath = BepInEx.Paths.PluginPath;
        string[] files = Directory.GetFiles(pluginsPath, "*.hhh", SearchOption.AllDirectories);

        // Optional folder filter: only keep files whose path contains one of the listed subfolder names.
        string filterRaw = Plugin.SpecificFolders.Value ?? "";
        if (!string.IsNullOrWhiteSpace(filterRaw))
        {
            var allowed = filterRaw.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            int before = files.Length;
            files = files.Where(f => allowed.Any(a => f.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            LogInfo($"SpecificFolders filter active ({string.Join(", ", allowed)}) — kept {files.Length}/{before} files.");
        }

        LogInfo($"Found {files.Length} .hhh file(s). Translating cosmetics from MoreHead to Vanilla REPO...");

        int registered = 0;

        foreach (string file in files)
        {
            if (TryRegister(file))
                registered++;
        }

        int total = files.Length;
        int skipped = total - registered;

        LogInfo($"Done — {registered}/{total} registered, {skipped} error(s).");
    }

    private static bool TryRegister(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024)
        {
            Plugin.Logger.LogWarning($"Skipped (too small/missing): {Path.GetFileName(path)}");
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(path);
        ParseFileName(fileName, out string internalName, out string tag);

        if (!TagToType.TryGetValue(tag, out SemiFunc.CosmeticType cosmeticType))
            return false;

        AssetBundle? bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            Plugin.Logger.LogError($"Failed to load bundle: {fileName}");
            return false;
        }

        GameObject? prefab = null;
        foreach (string assetName in bundle.GetAllAssetNames())
        {
            var obj = bundle.LoadAsset<GameObject>(assetName);
            if (obj != null) { prefab = obj; break; }
        }

        bundle.Unload(false);

        if (prefab == null)
        {
            Plugin.Logger.LogError($"No GameObject in bundle: {fileName}");
            return false;
        }

        // Deduplicate prefab network ID (same as MoreHead's EnsureUniqueName logic)
        string basePrefabName = prefab.name;
        prefab.name = EnsureUniqueId(basePrefabName, _usedPrefabIds);
        if (prefab.name != basePrefabName)
            Plugin.Logger.LogWarning($"Duplicate prefab name '{basePrefabName}' → renamed to '{prefab.name}'");

        // Deduplicate internal name used for assetId
        string baseInternal = internalName;
        internalName = EnsureUniqueId(internalName, _usedInternalNames);
        if (internalName != baseInternal)
            Plugin.Logger.LogWarning($"Duplicate internal name '{baseInternal}' → renamed to '{internalName}'");

        var cosmetic = prefab.GetComponent<Cosmetic>();
        if (cosmetic == null)
        {
            cosmetic = prefab.AddComponent<Cosmetic>();
        }
        cosmetic.type = cosmeticType;

        PrefabRef? prefabRef = NetworkPrefabs.RegisterNetworkPrefab($"Cosmetics/{prefab.name}", prefab);
        if (prefabRef == null)
        {
            Plugin.Logger.LogError($"Failed to register network prefab: {internalName}");
            return false;
        }

        string assetId = $"{BridgeIds.Prefix}{internalName.ToLowerInvariant()}";

        var cosmeticAsset = ScriptableObject.CreateInstance<CosmeticAsset>();
        cosmeticAsset.name = internalName;
        cosmeticAsset.assetName = prefab.name;
        cosmeticAsset.type = cosmeticType;
        cosmeticAsset.prefab = prefabRef;
        cosmeticAsset.assetId = assetId;
        cosmeticAsset.rarity = Plugin.DefaultRarity.Value;
        cosmeticAsset.customTypeList = [];
        // .hhh cosmetics don't have tintable PlayerMaterials, so disable the paint icon.
        cosmeticAsset.tintable = false;

        if (TagToOverrideType.TryGetValue(tag, out var originalOverrideType))
            _originalTypes[assetId] = originalOverrideType;

        // Apply any per-cosmetic override (rarity / type) before registering.
        PerCosmeticOverrides.ApplyIfPresent(cosmeticAsset);

        Cosmetics.RegisterCosmetic(cosmeticAsset);

        RegisteredAssetIds.Add(assetId);
        if (tag == "world")
            WorldAssetIds.Add(assetId);

        // Pull the prefab's albedo/main texture for use as the UI icon.
        var iconTex = TryExtractIconTexture(prefab);
        if (iconTex != null)
            BridgeIconTextures[assetId] = iconTex;

        return true;
    }

    // Walks the prefab's renderers and pulls the first usable Texture2D off their shared
    // materials. Always checks HasProperty BEFORE GetTexture — otherwise Unity logs errors
    // like "Material 'X' with Shader 'Y' doesn't have a texture property '_MainTex'" for
    // shaders that simply don't declare _MainTex (Unlit/Color, custom shaders, etc.).
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

    private static void LogInfo(string msg)
    {
        if (BceConsole.IsAvailable)
            BceConsole.WriteLine($"[Info   :  MoreHead Bridge] {msg}", ConsoleColor.Cyan);
        else
            Plugin.Logger.LogInfo($"{msg}");
    }

    internal static bool IsWorldAsset(CosmeticAsset? asset)
        => asset != null && BridgeIds.IsBridgeAsset(asset) && WorldAssetIds.Contains(asset.assetId);

    /// Restores a bridge cosmetic's rarity and type to the loader defaults
    internal static void ReapplyDefaults(CosmeticAsset asset)
    {
        asset.rarity = Plugin.DefaultRarity.Value;

        if (!_originalTypes.TryGetValue(asset.assetId, out var originalType))
            return;

        var (cosmeticType, isWorld) = PerCosmeticOverrides.ResolveType(originalType);
        asset.type = cosmeticType;

        if (isWorld)
            WorldAssetIds.Add(asset.assetId);
        else
            WorldAssetIds.Remove(asset.assetId);
    }
}
