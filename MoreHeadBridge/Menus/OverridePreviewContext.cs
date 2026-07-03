// ============================================================================
// Static context used to pass pending override data to MoreHeadCosmeticMountPatch
// while it instantiates cosmetics for the live-preview avatar.
//
// Set by CosmeticOverridePreview.RefreshFull() around its synchronous SetupCosmetics call (try/finally) — non-null only for that duration.
// ============================================================================

namespace MoreHeadBridge;

internal static class OverridePreviewContext
{
    /// The PlayerCosmetics being used for the live preview — null when no preview active.
    internal static PlayerCosmetics? Pc { get; private set; }

    /// The assetId of the cosmetic currently being configured.
    internal static string? AssetId { get; private set; }

    /// The pending override data to inject onto that cosmetic (offsets, customTypes, crown).
    internal static CosmeticOverrideData? Data { get; private set; }

    internal static void Set(PlayerCosmetics pc, string assetId, CosmeticOverrideData? data)
    {
        Pc     = pc;
        AssetId = assetId;
        Data   = data;
    }

    internal static void Clear()
    {
        Pc      = null;
        AssetId = null;
        Data    = null;
    }

    /// Returns true when <paramref name="pc"/> is the active preview PlayerCosmetics
    /// AND the cosmetic being instantiated matches the configured asset.
    internal static bool IsActiveFor(PlayerCosmetics? pc, string? assetId)
        => pc != null && pc == Pc && assetId == AssetId;

    /// Returns true when any live-preview is active for the given assetId,
    /// regardless of which PlayerCosmetics is in context.
    /// Used by CosmeticPrefabFixer.FixInstance which does not have the PC reference.
    internal static bool IsPreviewActiveForAsset(string? assetId)
        => Pc != null && assetId == AssetId;
}
