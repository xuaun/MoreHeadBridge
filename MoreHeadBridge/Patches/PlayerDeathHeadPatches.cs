using HarmonyLib;

namespace MoreHeadBridge;

// ── PlayerDeathHead.Trigger ───────────────────────────────────────────────────
// Fires when the player-death-head model becomes active (player dies in-level) — mount bridge cosmetics on it so they appear correctly.
[HarmonyPatch(typeof(PlayerDeathHead), nameof(PlayerDeathHead.Trigger))]
internal static class PlayerDeathHeadTriggerPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerDeathHead __instance)
    {
        try { BridgeDeathHeadGameplayMount.GetOrAdd(__instance).Remount(); }
        catch { /* best-effort */ }
    }
}

// ── PlayerDeathHead.Reset ────────────────────────────────────────────────────
// Fires when the player returns to normal (death head deactivated) — remove mounted bridge cosmetics from the death head model.
[HarmonyPatch(typeof(PlayerDeathHead), nameof(PlayerDeathHead.Reset))]
internal static class PlayerDeathHeadResetPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerDeathHead __instance)
    {
        try
        {
            var mount = __instance.GetComponent<BridgeDeathHeadGameplayMount>();
            mount?.ClearMounts();
        }
        catch { /* best-effort */ }
    }
}

// ── SetupCosmeticsLogic postfix (death head remount) ─────────────────────────
// Re-instantiation while the death head is triggered → remount bridge cosmetics there too. Priority.Last runs after the WorldCosmetics patch so world cosmetics are spawned, then SetupColors(false) so the death head's freshly-rebuilt PlayerMaterials get per-type colours immediately.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupCosmeticsLogic")]
internal static class SetupCosmeticsLogicDeathHeadPatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        try
        {
            var dh = __instance.deathHead;
            if (dh == null || !dh.triggered) return;
            BridgeDeathHeadGameplayMount.GetOrAdd(dh).Remount();
            // Re-apply per-type colours to the death head's PMs (just rebuilt by PlayerMaterialSetup). SetupColorsLogic(pc.colorsEquipped) directly — SetupColors() reads the LOCAL player's global array and may collapse per-type colours via SetupColorsAllLogic.
            MoreHeadCosmeticMountPatch.InvokeSetupColorsLogic(__instance);
        }
        catch { /* best-effort */ }
    }
}

// ── SetupColorsLogic postfix (death head bridge recolor) ──────────────────────
// On per-type colour changes vanilla updates the death head's own PMs, but our mounted BridgeTintMaterials live outside playerAvatarVisuals — re-colour those GOs with the updated colorsEquipped.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsLogic")]
internal static class SetupColorsLogicDeathHeadColorPatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        try
        {
            var dh = __instance.deathHead;
            if (dh == null || !dh.triggered) return;
            dh.GetComponent<BridgeDeathHeadGameplayMount>()?.RecolorMountedBridge();
        }
        catch { /* best-effort */ }
    }
}

// ── SetupColorsAllLogic postfix (death head bridge recolor) ───────────────────
// Same as above but for the "paint all" path that sets every type to one color.
[HarmonyPatch(typeof(PlayerCosmetics), "SetupColorsAllLogic")]
internal static class SetupColorsAllLogicDeathHeadColorPatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(PlayerCosmetics __instance)
    {
        try
        {
            var dh = __instance.deathHead;
            if (dh == null || !dh.triggered) return;
            dh.GetComponent<BridgeDeathHeadGameplayMount>()?.RecolorMountedBridge();
        }
        catch { /* best-effort */ }
    }
}
