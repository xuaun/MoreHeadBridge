using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// The lobby head's top/bottom colours come from palette indices colorsEquipped[HeadTopMesh/HeadBottomMesh]; a CUSTOM head RGB (head-mesh cosmetic or "Default" base mesh) should show instead. SetColor() writes palette colours into private playerColorTop/Bottom and Update() lerps toward them — override those fields after SetColor so the vanilla lerp carries the custom colour with no fighting. Works for the local player (local PerCosmeticColors store) and remote players (their synced PerCosmeticColorSyncComponent), so everyone's lobby head matches their custom head colour.
[HarmonyPatch(typeof(MenuPlayerHead), "SetColor")]
internal static class LobbyHeadCustomColorPatch
{
    private static readonly LazyFieldRef<MenuPlayerHead, Color> _topRef =
        new("playerColorTop", "lobby-head custom colors");
    private static readonly LazyFieldRef<MenuPlayerHead, Color> _botRef =
        new("playerColorBottom", "lobby-head custom colors");

    /// Re-applies every active lobby head's colour (runs vanilla SetColor + this postfix), so a
    /// custom-colour change made while in the lobby shows immediately. Needed because MenuPlayerHead
    /// only re-runs SetColor on a palette-index change, which a custom RGB edit does not produce.
    internal static void RefreshAllHeads()
    {
        if (!PerCosmeticColors.FeatureEnabled) return;
        foreach (var head in Object.FindObjectsOfType<MenuPlayerHead>())
        {
            if (head == null || head.playerAvatar == null
                || head.playerAvatar.playerAvatarVisuals == null) continue;
            head.SetColor();
        }
    }

    [HarmonyPostfix]
    private static void Postfix(MenuPlayerHead __instance)
    {
        if (!PerCosmeticColors.FeatureEnabled) return;

        var pc = __instance.playerAvatar?.playerCosmetics;
        if (pc == null) return;

        // All-or-nothing: with only one target field the RawImage recolour below would fight the vanilla lerp.
        if (!_topRef.TryResolve() || !_botRef.TryResolve()) return;

        // Top = HeadTopMesh (colorsEquipped[5]); Bottom = HeadBottomMesh (colorsEquipped[6]).
        bool hasTop = TryResolveHeadCustom(pc, (int)SemiFunc.CosmeticType.HeadTopMesh, out var top);
        bool hasBottom = TryResolveHeadCustom(pc, (int)SemiFunc.CosmeticType.HeadBottomMesh, out var bottom);
        if (!hasTop && !hasBottom) return;

        // Set the private targets AND the raw images directly: SetColor just wrote palette colours into the images, and the lerp would visibly flash palette→custom on every ~2 s SetColor re-fire. Only override channels that have a custom.
        if (hasTop) _topRef.TrySet(__instance, top);
        if (hasBottom) _botRef.TrySet(__instance, bottom);

        foreach (var raw in __instance.GetComponentsInChildren<RawImage>(includeInactive: true))
        {
            if (raw == null) continue;
            string n = raw.name;
            if (n == "Arena Crown" || n == "Steam Icon") continue;
            if (n == "Jaw Sprite") { if (hasBottom) raw.color = bottom; }
            else if (hasTop) raw.color = top;
        }
    }

    // Effective custom head colour for a head-mesh type: an equipped head-mesh cosmetic's custom RGB wins, else the "Default" base mesh custom ("__base_N__"). Local store for the local player, synced component for remotes.
    private static bool TryResolveHeadCustom(PlayerCosmetics pc, int typeIndex, out Color color)
    {
        color = default;

        bool isLocal = pc.photonView == null
                       || pc.photonView.IsMine
                       || pc.playerAvatarVisuals == null
                       || pc.playerAvatarVisuals.isMenuAvatar;
        var sync = isLocal ? null : pc.GetComponent<PerCosmeticColorSyncComponent>();

        bool Lookup(string assetId, out Color c)
        {
            if (isLocal) return PerCosmeticColors.TryGetCustomColor(assetId, out c);
            c = default;
            return sync != null && sync.TryGetRemoteCustomColor(assetId, out c);
        }

        // 1. Equipped cosmetic of this head-mesh type with a custom colour (replaces the default).
        var visuals = pc.playerAvatarVisuals;
        if (visuals != null)
        {
            foreach (var cos in visuals.GetComponentsInChildren<Cosmetic>(includeInactive: true))
            {
                if (cos == null || (int)cos.type != typeIndex) continue;
                var assetId = cos.cosmeticAsset?.assetId;
                if (assetId != null && Lookup(assetId, out color)) return true;
            }
        }

        // 2. The "Default" head base mesh custom colour.
        return Lookup(VanillaTintHelper.BaseMeshAssetId(typeIndex), out color);
    }
}
