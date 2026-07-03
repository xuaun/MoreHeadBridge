using System.Collections.Generic;

namespace MoreHeadBridge;

// CosmeticTypes that MultiEquipTypeFlags flips canEquipMultiple on (AllowMultipleCosmetics). Worlds get
// multi-equip via their own path (WorldCosmeticsSetupPatch), so they're not in this list despite being Hat.
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
}
