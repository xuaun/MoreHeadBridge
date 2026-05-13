using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// REPOLib sends RPCs in this order:
//   1. SetupCosmeticsRPC   (vanilla indices)   ← arrives first on remote
//   2. SetupCosmeticsModdedRPC (all assetIds)  ← arrives second
//
// REPOLib's SetupCosmeticsLogicPatch intercepts SetupCosmeticsLogic and injects modded
// cosmetics from cosmeticEquipped — but cosmeticEquipped isn't populated until the
// modded RPC fires. Result: bridge cosmetics appear one equip behind.
//
// Fix: patch SetupCosmeticsModdedRPC (postfix). When it arrives with bridge cosmetics,
// immediately call SetupCosmeticsLogic again so SetupCosmeticsLogicPatch can inject the
// now-populated cosmeticEquipped (vanilla + bridge assetIds) into the cosmetics logic.
internal static class ModdedRpcRetrigger
{
    private static FieldInfo? _cosmeticEquippedField;

    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("REPOLib.Objects.PlayerCosmeticsModded");
            if (type == null)
            {
                Plugin.Logger.LogDebug("PlayerCosmeticsModded not found — multiplayer bridge sync fix skipped.");
                return;
            }

            var method = AccessTools.Method(type, "SetupCosmeticsModdedRPC");
            if (method == null) return;

            _cosmeticEquippedField = AccessTools.Field(type, "cosmeticEquipped");
            if (_cosmeticEquippedField == null) return;

            var postfix = typeof(ModdedRpcRetrigger).GetMethod(
                nameof(Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Plugin.Logger.LogDebug("Multiplayer bridge sync fix applied.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Could not apply multiplayer bridge sync fix: {ex.Message}");
        }
    }

    private static void Postfix(MonoBehaviourPun __instance)
    {
        // Only re-trigger for remote players; local player is already set up by SetupCosmetics.
        if (__instance.photonView == null || __instance.photonView.IsMine) return;

        var cosmeticEquipped = _cosmeticEquippedField?.GetValue(__instance) as List<string>;
        if (cosmeticEquipped == null || cosmeticEquipped.Count == 0) return;

        // Only act when bridge cosmetics are present — don't interfere with vanilla-only players.
        bool hasBridge = false;
        foreach (var id in cosmeticEquipped)
        {
            if (BridgeIds.IsBridgeAsset(id)) { hasBridge = true; break; }
        }
        if (!hasBridge) return;

        // Pass an empty array. SetupCosmeticsLogicPatch (REPOLib) will replace it with the
        // full index list derived from cosmeticEquipped (which now includes vanilla + bridge
        // assetIds), then the original SetupCosmeticsLogic applies them all at once.
        var playerCosmetics = __instance.GetComponent<PlayerCosmetics>();
        playerCosmetics?.SetupCosmeticsLogic(Array.Empty<int>(), false);
    }
}
