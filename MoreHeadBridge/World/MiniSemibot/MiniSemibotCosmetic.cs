using REPOLib.Modules;
using System;
using UnityEngine;

namespace MoreHeadBridge;

// Registers the SYNTHETIC Mini-Semibot world cosmetic (no .hhh) through REPOLib's Cosmetics module, so equip/unequip syncs via the normal modded pipeline. The placeholder prefab is intentionally empty (Cosmetic component, no mesh) — the visual is the mini avatar spawned by WorldCosmeticsSetupPatch, which special-cases this asset id. Mirrors HhhCosmeticLoader.RegisterCosmetic.
internal static class MiniSemibotCosmetic
{
    // Keeps the pre-rename "MiniMe" name: it IS the asset's identity (assetId, unlock tracking, saved equips, icon cache key) — changing it would orphan existing saves.
    internal const string InternalName = "MHB_MiniMe";
    internal const string DisplayName = "Mini-Semibot";

    // Same shape as HhhCosmeticLoader's assetIds: "morehead-bridge:" + lowercased internal name.
    internal static readonly string AssetId = $"{BridgeIds.Prefix}{InternalName.ToLowerInvariant()}";

    private static bool _registered;
    private static GameObject? _prefab;   // strong ref so the runtime placeholder is never collected

    // The registered CosmeticAsset, exposed so the icon-capture pass can overwrite its icon with a real avatar photo once captured.
    internal static CosmeticAsset? Asset { get; private set; }

    internal static void Register()
    {
        if (_registered) return;
        // User opted out. Re-checked when the config flips ON mid-session (OnEnableMiniSemibotChanged) — REPOLib supports late registration.
        if (!Plugin.EnableMiniSemibot.Value) return;

        try
        {
            // The placeholder must survive scene loads (PrefabRef.Prefab resolves to the registered GO only while it's alive). DontDestroyOnLoad alone is unreliable this early in Awake — ALSO set HideAndDontSave + keep a strong static reference.
            var prefab = new GameObject(InternalName);
            prefab.SetActive(false);
            prefab.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(prefab);
            _prefab = prefab;

            // World cosmetics ride the Hat CosmeticType so type-indexed vanilla structures stay in-bounds (see HhhCosmeticLoader); WorldAssetIds is what actually marks it as a world.
            var cosmetic = prefab.AddComponent<Cosmetic>();
            cosmetic.type = SemiFunc.CosmeticType.Hat;

            var prefabRef = NetworkPrefabs.RegisterNetworkPrefab($"Cosmetics/{prefab.name}", prefab);
            if (prefabRef == null)
            {
                BceConsole.LogError("Mini-Semibot: failed to register network prefab");
                UnityEngine.Object.Destroy(prefab);
                return;
            }

            var asset = ScriptableObject.CreateInstance<CosmeticAsset>();
            asset.name = InternalName;
            asset.assetName = DisplayName;
            asset.type = SemiFunc.CosmeticType.Hat;
            asset.prefab = prefabRef;
            asset.assetId = AssetId;
            asset.rarity = Plugin.BridgeDefaultRarity.Value;
            asset.customTypeList = [];
            asset.tintable = false;
            // Icon: the captured preset-style photo when available, else the drawn placeholder until MiniSemibotIconCapture overwrites asset.icon on the next menu open.
            asset.icon = IconCapture.HasCache(asset)
                ? SemiFunc.LoadSpriteFromFile(IconCapture.CachePathFor(asset))
                : MiniSemibotIcon.Create();   // honoured by GetIconPatch (asset.icon shortcut)
            Asset = asset;

            Cosmetics.RegisterCosmetic(asset);

            // Mark it registered + world so it appears in the WORLD tab and IsWorldAsset() returns true.
            HhhCosmeticLoader.RegisteredAssetIds.Add(AssetId);
            HhhCosmeticLoader.WorldAssetIds.Add(AssetId);

            _registered = true;
            BceConsole.LogInfo("Mini-Semibot world cosmetic registered", ConsoleColor.Cyan);
        }
        catch (Exception ex)
        {
            BceConsole.LogError($"Mini-Semibot registration failed: {ex.Message}");
        }
    }
}
