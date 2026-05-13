using HarmonyLib;

namespace MoreHeadBridge;

// Cosmetic.Setup() logs:
//   "''<name>'' has no CosmeticPlayerCrown!"
// as an Error whenever a Hat or HeadTopMesh cosmetic lacks a CosmeticPlayerCrown
// component. Bridge cosmetics don't ship with
// this component since it's vanilla-game-specific. We add an empty one right before
// Setup runs — its Awake just reads the parent Cosmetic.type, no rendering side
// effects. The vanilla null-check then passes silently.
[HarmonyPatch(typeof(Cosmetic), nameof(Cosmetic.Setup))]
internal static class CosmeticSetupPatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance)
    {
        if (__instance == null) return;
        if (!BridgeIds.IsBridgeAsset(__instance.cosmeticAsset)) return;
        if (__instance.type != SemiFunc.CosmeticType.Hat &&
            __instance.type != SemiFunc.CosmeticType.HeadTopMesh)
            return;

        if (__instance.GetComponentInChildren<CosmeticPlayerCrown>(true) == null)
            __instance.gameObject.AddComponent<CosmeticPlayerCrown>();
    }
}
