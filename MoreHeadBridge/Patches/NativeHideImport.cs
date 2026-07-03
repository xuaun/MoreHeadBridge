using System.Collections.Generic;

namespace MoreHeadBridge;

// Hide-self equivalent of NativeOffsetImport: imports a non-bridge cosmetic's native CosmeticHideCondition.
internal static class NativeHideImport
{
    // Merge the cosmetic's native hide rules into the pending config (reads the prefab), add-if-absent.
    internal static void MergeIntoPending(CosmeticAsset asset, CosmeticHideConfig config)
    {
        if (BridgeIds.IsBridgeAsset(asset) || asset.prefab?.Prefab is not { } prefab) return;

        foreach (var comp in prefab.GetComponentsInChildren<CosmeticHideCondition>(true))
        {
            if (comp == null) continue;

            if (comp.cosmeticTypeList != null)
                foreach (var t in comp.cosmeticTypeList)
                    Add(config.WhenTypes ??= new List<SemiFunc.CosmeticType>(), t);

            if (comp.customList != null)
                foreach (var c in comp.customList)
                    Add(config.WhenConditions ??= new List<CosmeticCustomCondition.Type>(), c);

            if (comp.playerPosesList != null)
                foreach (var p in comp.playerPosesList)
                    Add(config.WhenPoses ??= new List<PlayerAvatarVisuals.Pose>(), p);

            if (comp.cosmeticList != null)
                foreach (var a in comp.cosmeticList)
                    if (a != null) Add(config.WhenCosmetics ??= new List<string>(), a.name);
        }
    }

    private static void Add<T>(List<T> list, T value)
    {
        if (!list.Contains(value)) list.Add(value);
    }
}
