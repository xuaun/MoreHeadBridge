using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MoreHeadBridge;

// REPOLib populates cosmeticEquipped only when SetupCosmeticsModdedRPC arrives, after the
// vanilla RPC — so bridge cosmetics appear one equip behind. Postfix re-triggers SetupCosmeticsLogic
// immediately after the modded RPC so REPOLib's own patch can inject the full equipped list.
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
                Plugin.Logger.LogDebug("PlayerCosmeticsModded not found — multiplayer bridge sync fix skipped.");
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
        if (cosmeticEquipped == null) return;

        var playerCosmetics = __instance.GetComponent<PlayerCosmetics>();
        playerCosmetics?.SetupCosmeticsLogic(Array.Empty<int>(), false);
    }
}
