using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// The section "X" calls ToggleCosmetic(null) → vanilla unequips every cosmetic of the section's subCategory; since worlds share Hat, the Hat "X" cleared both hat + world (and vice-versa).
// World "X" (999 / WORLD category): intercept entirely — remove only worlds + CosmeticPlayerUpdateLocal, never touching hats. Hat "X": let vanilla run but set IsUnequipAllRunning so WorldCosmeticsSetupPatch skips the intermediate SetupCosmeticsLogic calls (worlds are transiently absent mid-loop); the Postfix rebuilds pcEquipped with the correct final state.

[HarmonyPatch(typeof(MenuElementCosmeticButton), "ToggleCosmetic")]
[HarmonyPriority(Priority.High)]
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

        // Vanilla WORLD category — section is Hat-type (worlds are Hat internally); take the same manual path to avoid touching real hats.
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

        // Intermediate SetupCosmeticsLogic calls were skipped, leaving pcEquipped stale — always refresh here so hat GOs are removed without waiting for the next LateUpdate.
        if (any || wasActive)
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
    }

    // Ensures IsUnequipAllRunning is always cleared even if ToggleCosmetic throws, so SetupCosmeticsLogic isn't permanently suppressed for the session.
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
