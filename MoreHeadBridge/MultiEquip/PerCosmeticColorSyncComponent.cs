using Photon.Pun;
using System.Collections.Generic;

namespace MoreHeadBridge;

// MonoBehaviourPun component added to every PlayerCosmetics GameObject
// via PerCosmeticColorSyncAwakePatch.
//
// Holds the remote player's { assetId → colorIndex } map received via RPC.
// ApplyToCosmetics() is called from ApplyRemoteColorsAfterSetupLogicPatch after each
// SetupColorsLogic / SetupColorsAllLogic so colors are re-applied whenever the
// vanilla color system rebuilds playerMaterials.
//
// The RPC is sent OthersBuffered + RemoveBufferedRPCs (same as REPOLib):
//   • OthersBuffered  → current remote players receive it immediately
//   • Buffered        → players who join later receive it on connect
//   • RemoveBuffered  → replaces the previous entry so only the latest is stored
internal sealed class PerCosmeticColorSyncComponent : MonoBehaviourPun
{
    private Dictionary<string, int> _remoteColors = new();

    // Called by PerCosmeticColorSyncPatches.SendPerCosmeticColorsSyncPatch on Confirm.
    // Receives the serialized { assetId → colorIndex } map from the owning player,
    // deserializes it, and stores it for re-application after every SetupColorsLogic.
    [PunRPC]
    internal void SyncPerCosmeticColorsRPC(string colorData)
    {
        _remoteColors = PerCosmeticColorSerializer.Deserialize(colorData);
        // Re-apply immediately in case SetupColorsLogic already ran before this RPC arrived
        // (Photon does not guarantee ordering across different PhotonViews).
        var pc = GetComponent<PlayerCosmetics>();
        if (pc != null) ApplyToCosmetics(pc);
    }

    // Applies stored remote color overrides on top of whatever vanilla SetupColorsLogic
    // just wrote into playerMaterials. Safe to call when _remoteColors is empty (no-op).
    internal void ApplyToCosmetics(PlayerCosmetics pc)
    {
        if (_remoteColors.Count == 0) return;
        if (pc?.playerMaterials == null) return;

        foreach (var pm in pc.playerMaterials)
        {
            if (pm?.cosmetic?.cosmeticAsset == null) continue;
            if (_remoteColors.TryGetValue(pm.cosmetic.cosmeticAsset.assetId, out int colorIdx))
                pm.ColorSet(PerCosmeticColors.PropAlbedo,
                            PerCosmeticColors.PropEmission,
                            PerCosmeticColors.PropFresnel,
                            colorIdx);
        }
    }
}
