// Priority-600 prefix on SetupCosmetics: send SetupCosmeticsModdedRPC BEFORE REPOLib's prefix (400) sends the vanilla RPC. Fixes the "equip lags by one" for REPOLib-only friends (their logic otherwise fires on the OLD cosmeticEquipped). Order becomes modded → vanilla → REPOLib's harmless duplicate (identical data; its RemoveBufferedRPCs only clears our buffer slot). Registered via TryApply (depends on REPOLib types).

using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MoreHeadBridge;

internal static class REPOLibRpcOrderPatch
{
    private static Type? _playerCosmeticsModdedType;

    internal static void TryApply(Harmony harmony)
    {
        try
        {
            _playerCosmeticsModdedType = AccessTools.TypeByName("REPOLib.Objects.PlayerCosmeticsModded");
            if (_playerCosmeticsModdedType == null)
            {
                BridgeLog.Trace("REPOLibRpcOrderPatch: PlayerCosmeticsModded not found — skipped");
                return;
            }

            var target = AccessTools.Method(typeof(PlayerCosmetics), "SetupCosmetics");
            if (target == null)
            {
                BridgeLog.Trace("REPOLibRpcOrderPatch: PlayerCosmetics.SetupCosmetics not found — skipped");
                return;
            }

            var prefix = typeof(REPOLibRpcOrderPatch).GetMethod(
                nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

            // Must outrank REPOLib's own prefix (priority 400) so our modded RPC goes out first.
            harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.High });
            BridgeLog.Trace("REPOLibRpcOrderPatch: modded-before-vanilla RPC fix applied");
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"REPOLibRpcOrderPatch: skipped — {ex.Message}");
        }
    }

    // Runs before REPOLib's prefix (400): sends the modded RPC so it reaches remotes before the vanilla RPC REPOLib sends.
    private static void Prefix(PlayerCosmetics __instance, bool _synced, List<int>? _cosmetics)
    {
        if (!_synced) return;
        if (!SemiFunc.IsMultiplayer()) return;

        // Only the owning local player sends sync RPCs.
        var photonView = __instance.GetComponent<PhotonView>();
        if (photonView == null || !photonView.IsMine) return;

        if (_playerCosmeticsModdedType == null) return;

        // Broadcast override data BEFORE any RPC is queued: at game start AddToRoomCache events beat buffered RPCs (why late-join works), but mid-game the normal BridgeSyncBroadcastPatch fires only after SetupCosmeticsLogic — too late for TryGetRemote in the InstantiateCosmetic postfix. This guarantees late-join ordering for every equip change.
        CustomizerSync.BroadcastAll();

        // PlayerCosmeticsModded is added by REPOLib's AwakePatch — it has its own PhotonView.
        var moddedComponent = __instance.GetComponent(_playerCosmeticsModdedType) as MonoBehaviourPun;
        if (moddedComponent == null) return;

        var moddedView = moddedComponent.photonView;
        if (moddedView == null) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        // Use _cosmetics when provided (explicit list), else fall back to the live equipped list — same logic REPOLib uses inside its own prefix.
        IEnumerable<int> equipped = _cosmetics ?? (IEnumerable<int>)meta.cosmeticEquipped;

        // Build the same "\x1F"-separated assetId string that REPOLib builds.
        var sb = new StringBuilder();
        bool first = true;
        foreach (int idx in equipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset?.assetId == null) continue;
            if (!first) sb.Append('\x1F');
            sb.Append(asset.assetId);
            first = false;
        }

        // Send the modded RPC early, clearing the server buffer first (REPOLib's pattern) so late joiners only see the latest. REPOLib re-sends right after — harmless identical duplicate.
        PhotonNetwork.RemoveBufferedRPCs(moddedView.ViewID, "SetupCosmeticsModdedRPC");
        moddedView.RPC("SetupCosmeticsModdedRPC", RpcTarget.OthersBuffered, sb.ToString());
    }
}
