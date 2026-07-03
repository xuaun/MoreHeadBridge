using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// REPOLib populates cosmeticEquipped only when the modded RPC arrives (after the vanilla one), so bridge cosmetics show one equip behind. This postfix re-triggers SetupCosmeticsLogic right after the modded RPC.
// Array.Empty<int>() and NOT cosmeticEquippedRaw: REPOLib's prefix runs before Raw is written, so Raw holds the MERGED array and would re-spawn just-unequipped cosmetics. Empty base is safe: non-empty list → REPOLib injects the full list; empty → REPOLib early-outs and vanilla clears everything.
// Registered manually because the target type (REPOLib.Objects.PlayerCosmeticsModded) may be absent.
internal static class SetupCosmeticsModdedRpcPatch
{
    private static FieldInfo? _cosmeticEquippedField;

    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("REPOLib.Objects.PlayerCosmeticsModded");
            if (type == null)
            {
                BridgeLog.Trace("PlayerCosmeticsModded not found — multiplayer bridge sync fix skipped");
                return;
            }

            var method = AccessTools.Method(type, "SetupCosmeticsModdedRPC");
            if (method == null) return;

            _cosmeticEquippedField = AccessTools.Field(type, "cosmeticEquipped");
            if (_cosmeticEquippedField == null) return;

            var postfix = typeof(SetupCosmeticsModdedRpcPatch).GetMethod(
                nameof(Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            BridgeLog.Trace("Multiplayer bridge sync fix applied");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Could not apply multiplayer bridge sync fix: {ex.Message}");
        }
    }

    private static void Postfix(MonoBehaviourPun __instance)
    {
        // Only re-trigger for remote players; local player is already set up by SetupCosmetics.
        if (__instance.photonView == null || __instance.photonView.IsMine) return;

        // Run even when cosmeticEquipped is empty: on unequip-all, REPOLib's prefix may have used the STALE list and re-spawned old cosmetics — this second call lets it re-check and remove them.
        var cosmeticEquipped = _cosmeticEquippedField?.GetValue(__instance) as List<string>;
        if (cosmeticEquipped == null) return;

        var playerCosmetics = __instance.GetComponent<PlayerCosmetics>();
        if (playerCosmetics == null) return;

        // Empty base array so REPOLib's SetupCosmeticsLogicPatch uses the just-updated PlayerCosmeticsModded.cosmeticEquipped as the sole source.
        // cosmeticEquippedRaw holds REPOLib's merged array (its prefix runs before Raw is written) and would re-spawn just-unequipped cosmetics; with Array.Empty, non-empty → REPOLib injects the full list, empty → early-out + vanilla clears.
        playerCosmetics.SetupCosmeticsLogic(Array.Empty<int>(), false);

        // Re-instantiation makes FRESH BridgeTintMaterials at prefab-default colours, and only the SetupColorsLogic postfixes colour them — a remote whose colours came via our event would show defaults unless SetupColorsLogic re-runs.
        // Cached override data → RefreshRemoteCosmetics re-runs both Setup and Colors; otherwise (colour-only remote) re-run SetupColorsLogic ourselves, or the synced colours are lost until re-equip.
        int actorNumber = __instance.photonView.Owner?.ActorNumber ?? -1;
        var cached = actorNumber > 0 ? CustomizerSync.GetRemotePlayerData(actorNumber) : null;
        if (cached != null)
            MoreHeadCosmeticMountPatch.RefreshRemoteCosmetics(actorNumber);
        else
            playerCosmetics.SetupColorsLogic(playerCosmetics.colorsEquipped);
    }
}
