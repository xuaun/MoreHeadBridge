using HarmonyLib;

namespace MoreHeadBridge;

// Skips AnimateInstant on a destroyed offset condition: a stale entry left in ConditionUpdateAll's list would
// NRE on its dead transform every frame and abort the rest of the loop (crown + later offsets stop applying).
[HarmonyPatch(typeof(CosmeticOffsetCondition), "AnimateInstant")]
internal static class CosmeticOffsetConditionGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticOffsetCondition __instance) => __instance != null;
}
