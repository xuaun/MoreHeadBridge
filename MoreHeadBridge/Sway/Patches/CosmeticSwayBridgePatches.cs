using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Injects BridgeSwaySpring at instantiation and forwards PlayerCosmetics spring events to it — bridge cosmetics respond to movement like vanilla CosmeticSprings.

[HarmonyPatch(typeof(PlayerCosmetics), "InstantiateCosmetic")]
internal static class BridgeSwaySpringInjectPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance, CosmeticAsset _cosmeticAsset, GameObject __result)
    {
        if (__result == null || !BridgeIds.IsBridgeAsset(_cosmeticAsset)) return;
        if (!CosmeticSwayHelper.IsSwayEnabled(__instance, _cosmeticAsset.assetId)) return;
        if (__result.GetComponentsInChildren<CosmeticSprings>(includeInactive: true).Length > 0) return;

        var cosmetic = __result.GetComponent<Cosmetic>();
        if (cosmetic == null) return;

        float intensity = CosmeticSwayHelper.GetIntensityFactor(__instance, _cosmeticAsset.assetId);
        var spring = __result.GetComponent<BridgeSwaySpring>()
                     ?? __result.AddComponent<BridgeSwaySpring>();
        spring.Init(cosmetic, intensity);
    }
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringJump))]
internal static class BridgeSwaySpringJumpPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, 10f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringLand))]
internal static class BridgeSwaySpringLandPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, -10f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringStandToCrouch))]
internal static class BridgeSwaySpringStandToCrouchPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, 10f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringCrouchToStand))]
internal static class BridgeSwaySpringCrouchToStandPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, -10f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringCrouchToCrawl))]
internal static class BridgeSwaySpringCrouchToCrawlPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, 5f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringCrawlToCrouch))]
internal static class BridgeSwaySpringCrawlToCrouchPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, -5f, CosmeticSprings.CosmeticSpring.JumpDirection.Right);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.TumbleStart))]
internal static class BridgeSwaySpringTumbleStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, 10f, CosmeticSprings.CosmeticSpring.JumpDirection.Forward);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.TumbleStop))]
internal static class BridgeSwaySpringTumbleStopPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance, -10f, CosmeticSprings.CosmeticSpring.JumpDirection.Forward);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootRightUp))]
internal static class BridgeSwaySpringFootRightUpPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            2f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Right,
            SemiFunc.CosmeticType.LegRight,
            SemiFunc.CosmeticType.LegRightMesh,
            SemiFunc.CosmeticType.FootRight);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootRightDown))]
internal static class BridgeSwaySpringFootRightDownPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            -2f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Right,
            SemiFunc.CosmeticType.LegRight,
            SemiFunc.CosmeticType.LegRightMesh,
            SemiFunc.CosmeticType.FootRight);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootLeftUp))]
internal static class BridgeSwaySpringFootLeftUpPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            2f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Left,
            SemiFunc.CosmeticType.LegLeft,
            SemiFunc.CosmeticType.LegLeftMesh,
            SemiFunc.CosmeticType.FootLeft);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootLeftDown))]
internal static class BridgeSwaySpringFootLeftDownPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            -2f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Left,
            SemiFunc.CosmeticType.LegLeft,
            SemiFunc.CosmeticType.LegLeftMesh,
            SemiFunc.CosmeticType.FootLeft);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootRightUpSlow))]
internal static class BridgeSwaySpringFootRightUpSlowPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            1f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Right,
            SemiFunc.CosmeticType.LegRight,
            SemiFunc.CosmeticType.LegRightMesh,
            SemiFunc.CosmeticType.FootRight);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootRightDownSlow))]
internal static class BridgeSwaySpringFootRightDownSlowPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            -1f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Right,
            SemiFunc.CosmeticType.LegRight,
            SemiFunc.CosmeticType.LegRightMesh,
            SemiFunc.CosmeticType.FootRight);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootLeftUpSlow))]
internal static class BridgeSwaySpringFootLeftUpSlowPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            1f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Left,
            SemiFunc.CosmeticType.LegLeft,
            SemiFunc.CosmeticType.LegLeftMesh,
            SemiFunc.CosmeticType.FootLeft);
}

[HarmonyPatch(typeof(PlayerCosmetics), nameof(PlayerCosmetics.CosmeticSpringFootLeftDownSlow))]
internal static class BridgeSwaySpringFootLeftDownSlowPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
        => CosmeticSwayHelper.ImpulseBridgeSprings(
            __instance,
            -1f,
            CosmeticSprings.CosmeticSpring.JumpDirection.Left,
            SemiFunc.CosmeticType.LegLeft,
            SemiFunc.CosmeticType.LegLeftMesh,
            SemiFunc.CosmeticType.FootLeft);
}
