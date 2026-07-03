using HarmonyLib;

namespace MoreHeadBridge;

// Thin prefixes that skip vanilla CosmeticSprings impulses for bridge cosmetics when sway is disabled. Equip/remove/color impulses intentionally untouched.

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.JumpImpulse))]
internal static class CosmeticSpringsJumpPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.LandImpulse))]
internal static class CosmeticSpringsLandPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.StandToCrouch))]
internal static class CosmeticSpringsStandToCrouchPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.CrouchToStand))]
internal static class CosmeticSpringsCrouchToStandPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.CrouchToCrawl))]
internal static class CosmeticSpringsCrouchToCrawlPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.CrawlToCrouch))]
internal static class CosmeticSpringsCrawlToCrouchPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.Impulse))]
internal static class CosmeticSpringsImpulsePatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.TumbleStart))]
internal static class CosmeticSpringsTumbleStartPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}

[HarmonyPatch(typeof(CosmeticSprings), nameof(CosmeticSprings.TumbleStop))]
internal static class CosmeticSpringsTumbleStopPatch
{
  [HarmonyPrefix]
  private static bool Prefix(CosmeticSprings __instance)
    => !CosmeticSwayHelper.ShouldSuppressSway(__instance);
}
