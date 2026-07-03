using System;
using System.Collections.Generic;

namespace MoreHeadBridge;

internal static class BridgeIds
{
    internal const string Prefix = "morehead-bridge:";

    // O(1) membership cache for the non-bridge modded set. RegisteredCosmetics is append-only
    // (REPOLib has no unregister), so a Count change is a sufficient — and cheap — invalidation
    // signal. Avoids the O(n) List.Contains this used to do per cosmetic inside menu sort/filter.
    private static HashSet<CosmeticAsset>? _registeredSet;
    private static int _registeredCount = -1;

    private static HashSet<CosmeticAsset> RegisteredSet()
    {
        var registered = REPOLib.Modules.Cosmetics.RegisteredCosmetics;
        if (_registeredSet == null || _registeredCount != registered.Count)
        {
            _registeredSet = new HashSet<CosmeticAsset>(registered);
            _registeredCount = registered.Count;
        }
        return _registeredSet;
    }

    internal static bool IsBridgeAsset(string? assetId)
        => !string.IsNullOrEmpty(assetId) && assetId.StartsWith(Prefix, StringComparison.Ordinal);

    internal static bool IsBridgeAsset(CosmeticAsset? asset)
        => asset != null && IsBridgeAsset(asset.assetId);

    internal static bool IsModdedCosmetic(CosmeticAsset? asset)
        => asset != null
           && !IsBridgeAsset(asset)
           && RegisteredSet().Contains(asset);

    internal static bool HasAnyNonBridgeModded()
        => REPOLib.Modules.Cosmetics.RegisteredCosmetics.Count > 0;

    internal static bool IsCustomizable(CosmeticAsset? asset)
        => IsBridgeAsset(asset)
           || (Plugin.AllowModdedOverrides.Value && IsModdedCosmetic(asset));
}
