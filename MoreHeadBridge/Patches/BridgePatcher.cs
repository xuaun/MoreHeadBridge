// Applies every [HarmonyPatch] class individually — PatchAll aborts EVERY patch in the assembly when
// a single target method is missing after a game update. Here a failed class is logged and skipped,
// so the rest of the mod keeps working; if a cosmetics-menu takeover class fails, the whole takeover
// steps aside (surviving members defer to the vanilla menu) instead of rendering half-built.

using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

internal static class BridgePatcher
{
    // Structurally interdependent menu patches: Start injects the virtual/WORLD tabs whose content
    // RefreshScrollContent builds, Update/LateUpdate/Buttons drive the injected UI. One missing member
    // leaves the others pointing at UI that never gets built (or tabs vanilla can't render) — so any
    // failure here disables the takeover as a unit. Peripheral menu patches (favorites, randomize
    // filter, world highlight/equip polish) degrade safely on their own and stay out of the cluster.
    private static readonly HashSet<string> MenuTakeoverCluster = new()
    {
        nameof(CosmeticsFilterPatch),
        nameof(CosmeticsMenuStartPatch),
        nameof(CosmeticsMenuUpdatePatch),
        nameof(CosmeticsMenuLateUpdatePatch),
        nameof(ToolsButtonOnSelectPatch),
        nameof(TogglePopupColorPatch),
        nameof(TogglePopupRandomizePatch),
        nameof(TogglePopupResetPatch),
        nameof(VirtualCategoryHighlightPatch),
        nameof(WorldCosmeticsMenuStartPatch),
    };

    /// True when a menu-takeover patch failed to apply — surviving members defer to the vanilla menu.
    internal static bool MenuTakeoverBroken { get; private set; }

    /// PatchAll semantics (same type enumeration and order), isolated per class.
    internal static void ApplyAll(Harmony harmony)
    {
        int applied = 0;
        List<string>? failed = null;

        foreach (var type in AccessTools.GetTypesFromAssembly(System.Reflection.Assembly.GetExecutingAssembly()))
        {
            try
            {
                if (harmony.CreateClassProcessor(type).Patch() != null) applied++;
            }
            catch (System.Exception ex)
            {
                (failed ??= new List<string>()).Add(type.Name);
                BceConsole.LogWarning($"Patch {type.Name} failed to apply (game update?) — its feature is disabled: {ex.Message}");
                if (MenuTakeoverCluster.Contains(type.Name))
                    MenuTakeoverBroken = true;
            }
        }

        if (MenuTakeoverBroken)
            BceConsole.LogWarning("A cosmetics-menu patch failed — menu enhancements disabled, using the vanilla menu.");
        BridgeLog.Debug($"BridgePatcher: {applied} patch classes applied"
                        + (failed != null ? $", {failed.Count} failed: {string.Join(", ", failed)}" : ""));
    }
}
