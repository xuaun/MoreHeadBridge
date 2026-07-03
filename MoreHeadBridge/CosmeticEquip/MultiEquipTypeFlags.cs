using System.Collections.Generic;

namespace MoreHeadBridge;

// Backs up and flips the native CosmeticTypeAsset.canEquipMultiple flag for the multi-equip types so vanilla
// handles multi-equip itself. cosmeticTypeAssets are shared assets, so originals are restored on toggle-off.
internal static class MultiEquipTypeFlags
{
    // Original canEquipMultiple values, captured before the first mutation (keyed by the shared asset).
    private static readonly Dictionary<CosmeticTypeAsset, bool> _originals = new();

    // Applies or restores the flag to match AllowMultipleCosmetics. Idempotent — safe to call on MetaManager load and on config change.
    internal static void Sync()
    {
        if (!Plugin.AllowMultipleCosmetics.Value)
        {
            Restore();
            return;
        }
        Apply();
    }

    private static void Apply()
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticTypeAssets == null) return;

        foreach (var typeAsset in meta.cosmeticTypeAssets)
        {
            if (typeAsset == null) continue;
            if (!MultiEquipTypes.All.Contains(typeAsset.type)) continue;

            if (!_originals.ContainsKey(typeAsset))
                _originals[typeAsset] = typeAsset.canEquipMultiple;

            typeAsset.canEquipMultiple = true;
        }
    }

    // Restores every mutated type asset to its captured value and forgets the backup.
    internal static void Restore()
    {
        if (_originals.Count == 0) return;

        foreach (var kv in _originals)
        {
            if (kv.Key != null)
                kv.Key.canEquipMultiple = kv.Value;
        }
        _originals.Clear();
    }
}
