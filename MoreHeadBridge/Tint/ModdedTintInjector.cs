using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

// Gives per-part colour SLOTS to modded (non-bridge) cosmetics that paint through vanilla PlayerMaterial, by
// injecting BridgeTintMaterial on the renderers a ModdedSlotLayout selects and grouping them into slot ids.
// The bridge equip path (BridgeTintHelper.InjectBridgeTintMaterials) deliberately skips PlayerMaterial renderers;
// this is the opposite path, only for assets a layout handles (currently YoshiCarry).
internal static class ModdedTintInjector
{
    // Runs per cosmetic instance (local / menu / remote, incl. multi-equip extras): adds a BTM per layout-selected
    // tintable renderer, tags it with the renderer's slot id, and disables the vanilla PlayerMaterial so colour
    // control belongs to the BTM (its default type-colour comes from ApplyTypeColors, overrides from ApplyOverrides).
    internal static void Inject(Cosmetic? cosmetic)
    {
        if (cosmetic == null) return;
        var asset = cosmetic.cosmeticAsset;
        if (asset == null || !ModdedSlotLayout.Handles(asset)) return;

        // Master gate: when the per-cosmetic colour system is off, leave the cosmetic fully vanilla.
        if (!PerCosmeticColors.FeatureEnabled) return;

        int btmIndex = 0;
        foreach (var pm in cosmetic.GetComponentsInChildren<PlayerMaterial>(includeInactive: true))
        {
            if (pm == null || !pm.tintable) continue;

            var r = pm.GetComponent<Renderer>();
            if (r == null) continue;

            int slotId = ModdedSlotLayout.SlotIdForRenderer(asset, pm.gameObject);
            if (slotId < 0) continue;   // not part of any slot → keep its original/vanilla colour

            var btm = BridgeTintHelper.TryAddBridgeTintMaterial(r, asset, cosmetic, btmIndex, slotId, suppressEmission: true);
            if (btm == null) continue;
            btmIndex++;

            // Group this renderer's material(s) under the layout's slot id.
            int matCount = btm.materials?.Length ?? 1;
            var ids = new int[matCount];
            for (int i = 0; i < matCount; i++) ids[i] = slotId;
            btm.slotIds = ids;

            // Hand colour control to the BTM: stop vanilla PlayerMaterial.ColorSet from also tinting this part.
            pm.tintable = false;
        }
    }
}

// Per-instance hook: Cosmetic.Setup() runs once for every spawned cosmetic (InstantiateCosmetic), for local / menu /
// remote avatars and multi-equip extras alike. We patch it as a PREFIX, not a postfix: Setup() reparents the
// cosmetic's meshParents onto the player's body bones (and meshSwitch cosmetics like YoshiCarry's body-bottom-mesh
// deactivate/move their renderers), so by the postfix the renderers have left the Cosmetic's hierarchy and
// GetComponentsInChildren<PlayerMaterial> finds nothing. At prefix the meshes are still children, and cosmeticAsset
// is already assigned (PlayerCosmetics.InstantiateCosmetic sets it before calling Setup).
[HarmonyPatch(typeof(Cosmetic), "Setup")]
internal static class ModdedTintInjectorPatch
{
    [HarmonyPrefix]
    private static void Prefix(Cosmetic __instance) => ModdedTintInjector.Inject(__instance);
}
