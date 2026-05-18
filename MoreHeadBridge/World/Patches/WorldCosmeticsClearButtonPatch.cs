using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// The "X" button in a cosmetic section calls ToggleCosmetic with cosmeticAsset == null,
// which makes vanilla loop over every equipped cosmetic whose type matches the section's
// subCategory and call CosmeticUnequip on each. Because world cosmetics share Hat as
// their vanilla type with real hats, clicking the Hat "X" clears both hat + world, and
// clicking the World "X" also clears both.
//
// World "X" (virtual subCategory 999 or vanilla WORLD category):
//   Intercept entirely — manually remove only worlds and call CosmeticPlayerUpdateLocal.
//   Hats are never touched, so their GOs are never destroyed and never re-animate.
//
// Hat "X" (Hat subCategory in a non-World section):
//   Let vanilla run but set IsUnequipAllRunning so WorldCosmeticsSetupPatch skips
//   intermediate SetupCosmeticsLogic calls (worlds may be transiently absent while
//   CosmeticUnequip loops remove Hat-type items before WorldCosmeticsUnequipPatch
//   restores them). The Postfix's CosmeticPlayerUpdateLocal with correct final state
//   then rebuilds pcEquipped properly — removing hat GOs, keeping world GOs.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "ToggleCosmetic")]
internal static class WorldCosmeticsClearButtonPatch
{
    // Read by WorldCosmeticsSetupPatch between calls — must stay static.
    internal static bool IsUnequipAllRunning { get; private set; }

    private sealed class PatchState
    {
        public List<int>? Backup;
        public bool UnequipAllWasActive;
    }

    private static readonly MethodInfo? _triggerClickAnimations =
        typeof(MenuElementCosmeticButton).GetMethod(
            "TriggerClickAnimations", BindingFlags.NonPublic | BindingFlags.Instance);

    [HarmonyPrefix]
    private static bool Prefix(MenuElementCosmeticButton __instance, ref PatchState __state)
    {
        __state = new PatchState();
        IsUnequipAllRunning = false;
        if (HhhCosmeticLoader.WorldAssetIds.Count == 0) return true;
        if (__instance.cosmeticAsset != null) return true;   // specific cosmetic button, not "X"
        if (MetaManager.instance == null) return true;

        var section = __instance.cosmeticSection;
        if (section == null) return true;

        // Virtual World section — synthetic subCategory so sticky-header doesn't collide with Hat.
        if (section.subCategory == CosmeticsFilterPatch.WorldSubCategory)
        {
            PlayClickFeedback(__instance);
            UnequipWorldsOnly(__instance);
            return false; // skip vanilla ToggleCosmetic
        }

        if (section.subCategory != SemiFunc.CosmeticType.Hat) return true;

        // Vanilla WORLD category — section is Hat-type (worlds are Hat internally).
        // Take the same manual path to avoid touching real hats.
        if (WorldCosmeticsMenuState.IsWorldCategory(CosmeticsMenuState.ActivePage?.selectedCategory)
            || section.gameObject.name == CosmeticsFilterPatch.WorldSectionName)
        {
            PlayClickFeedback(__instance);
            UnequipWorldsOnly(__instance);
            return false;
        }

        // Hat section "X" — protect worlds from vanilla's Hat-type sweep.
        WorldCosmeticsMenuState.PartitionHatCosmetics(MetaManager.instance, out _, out var worlds);
        if (worlds.Count > 0)
        {
            __state.Backup = worlds;
            IsUnequipAllRunning = true;
            __state.UnequipAllWasActive = true;
        }
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(PatchState __state)
    {
        IsUnequipAllRunning = false;
        if (__state == null) return;

        var backup = __state.Backup;
        bool wasActive = __state.UnequipAllWasActive;

        if (MetaManager.instance == null) return;

        bool any = false;
        if (backup != null)
        {
            foreach (int idx in backup)
            {
                if (!MetaManager.instance.cosmeticEquipped.Contains(idx))
                {
                    MetaManager.instance.cosmeticEquipped.Add(idx);
                    any = true;
                }
            }
        }

        if (any) MetaManager.instance.Save();

        // When IsUnequipAllRunning was active, intermediate SetupCosmeticsLogic calls were
        // skipped, leaving pcEquipped (the Cosmetic GO list) stale. Always refresh here so
        // hat GOs are correctly removed without waiting for the next LateUpdate.
        if (any || wasActive)
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
    }

    // Ensures IsUnequipAllRunning is always cleared even if ToggleCosmetic throws,
    // so SetupCosmeticsLogic isn't permanently suppressed for the rest of the session.
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        IsUnequipAllRunning = false;
        return __exception;
    }

    private static void PlayClickFeedback(MenuElementCosmeticButton btn)
    {
        btn.soundClick.Play(MenuManager.instance.soundPosition);
        _triggerClickAnimations?.Invoke(btn, null);
    }

    private static void UnequipWorldsOnly(MenuElementCosmeticButton btn)
    {
        WorldCosmeticsMenuState.PartitionHatCosmetics(MetaManager.instance!, out _, out var worlds);
        bool any = false;
        foreach (int idx in worlds)
        {
            MetaManager.instance!.cosmeticEquipped.Remove(idx);
            any = true;
        }
        if (any)
        {
            MetaManager.instance!.Save();
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
        }
        // Mirror vanilla's ToggleCosmetic end: clear the section's color picker button.
        btn.cosmeticSection?.UpdateColorButton(null);
    }
}
