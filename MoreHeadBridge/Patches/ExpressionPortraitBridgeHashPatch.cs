using HarmonyLib;

namespace MoreHeadBridge;

// The expression wheel only re-renders its portraits when vanilla equips/colors change; mixes in the bridge's local look (per-cosmetic colors, overrides, mini, MoreHead hide).
[HarmonyPatch(typeof(ExpressionWheelPortraitCache), "AppearanceHashGet")]
internal static class ExpressionPortraitBridgeHashPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref int __result) => __result = __result * 31 + BridgeNetMux.LocalStateHash();
}
