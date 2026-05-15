using System.Collections.Generic;

namespace MoreHeadBridge;

// The set of CosmeticTypes that support multiple simultaneous equips when
// AllowMultipleCosmetics is enabled. World assets are handled via IsWorldAsset()
// and are included conceptually but not listed here (they use Hat internally).
internal static class MultiEquipTypes
{
    internal static readonly HashSet<SemiFunc.CosmeticType> All = new()
    {
        SemiFunc.CosmeticType.Hat,
        SemiFunc.CosmeticType.HeadBottom,
        SemiFunc.CosmeticType.FaceTop,
        SemiFunc.CosmeticType.FaceBottom,
        SemiFunc.CosmeticType.Eyewear,
        SemiFunc.CosmeticType.Ears,
        SemiFunc.CosmeticType.BodyTop,
        SemiFunc.CosmeticType.BodyBottom,
        SemiFunc.CosmeticType.ArmRight,
        SemiFunc.CosmeticType.ArmLeft,
        SemiFunc.CosmeticType.LegRight,
        SemiFunc.CosmeticType.FootRight,
        SemiFunc.CosmeticType.LegLeft,
        SemiFunc.CosmeticType.FootLeft,
    };

    // True when the feature is on and the asset's type supports multi-equip.
    // World assets always qualify (they have their own slot via IsWorldAsset).
    internal static bool IsEnabled(CosmeticAsset? asset)
    {
        if (!Plugin.AllowMultipleCosmetics.Value) return false;
        if (asset == null) return false;
        if (HhhCosmeticLoader.IsWorldAsset(asset)) return true;
        return All.Contains(asset.type);
    }
}
