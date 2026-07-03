using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// MonoBehaviourPun added to every PlayerCosmetics via PerCosmeticColorSyncAwakePatch. Holds a remote player's synced colour data (BridgeNetMux): whole-asset + per-slot palette indexes, custom RGBs, animation specs.
// ApplyToCosmetics() re-runs after each SetupColorsLogic / SetupColorsAllLogic, re-applying colours whenever vanilla rebuilds playerMaterials.
// Legacy RPC receivers kept so players on the previous build still sync here; new sends use PerCosmeticColorNetworkSync.
internal sealed class PerCosmeticColorSyncComponent : MonoBehaviourPun
{
    private Dictionary<string, int>                                     _remoteColors           = new();
    private Dictionary<string, Dictionary<int, int>>                    _remoteSlotColors       = new();
    private Dictionary<string, ColorAnimation>                          _remoteAnimations       = new();
    private Dictionary<string, Color>                                   _remoteCustomColors     = new();
    private Dictionary<string, Dictionary<int, Color>>                  _remoteCustomSlotColors = new();
    private Dictionary<string, Dictionary<int, ColorAnimation>>         _remoteSlotAnimations   = new();

    internal void SetRemoteColors(
        Dictionary<string, int> colors,
        Dictionary<string, Dictionary<int, int>> slotColors)
    {
        _remoteColors = new Dictionary<string, int>(colors);
        _remoteSlotColors = new Dictionary<string, Dictionary<int, int>>();
        foreach (var kv in slotColors)
            _remoteSlotColors[kv.Key] = new Dictionary<int, int>(kv.Value);
    }

    internal void SetRemoteAnimations(Dictionary<string, ColorAnimation> animations)
        => _remoteAnimations = new Dictionary<string, ColorAnimation>(animations);

    internal void SetRemoteCustomColors(
        Dictionary<string, Color> whole,
        Dictionary<string, Dictionary<int, Color>> perSlot)
    {
        _remoteCustomColors = new Dictionary<string, Color>(whole);
        _remoteCustomSlotColors = new Dictionary<string, Dictionary<int, Color>>();
        foreach (var kv in perSlot)
            _remoteCustomSlotColors[kv.Key] = new Dictionary<int, Color>(kv.Value);
    }

    internal void SetRemoteSlotAnimations(Dictionary<string, Dictionary<int, ColorAnimation>> slotAnims)
    {
        _remoteSlotAnimations = new Dictionary<string, Dictionary<int, ColorAnimation>>();
        foreach (var kv in slotAnims)
            _remoteSlotAnimations[kv.Key] = new Dictionary<int, ColorAnimation>(kv.Value);
    }

    // Remote whole-asset custom RGB lookup (incl. "__base_N__" keys) — used to colour the lobby head to match the remote's custom head colour.
    internal bool TryGetRemoteCustomColor(string assetId, out Color color)
        => _remoteCustomColors.TryGetValue(assetId, out color);

    // Legacy receiver for previous MoreHeadBridge versions: deserialises whole-asset + per-slot maps and stores them for re-application after every SetupColorsLogic.
    [PunRPC]
    internal void SyncPerCosmeticColorsRPC(string colorData)
    {
        const int MaxChars = 16 * 1024;
        if (colorData == null || colorData.Length > MaxChars) return;

        PerCosmeticColorSerializer.DeserializeWithSlots(colorData,
            out _remoteColors, out _remoteSlotColors);

        // Re-apply immediately in case SetupColorsLogic already ran before this RPC arrived (Photon doesn't guarantee ordering across PhotonViews).
        var pc = GetComponent<PlayerCosmetics>();
        if (pc != null) ApplyToCosmetics(pc);
    }

    // Legacy receiver for previous MoreHeadBridge versions.
    [PunRPC]
    internal void SyncColorAnimationsRPC(string animData)
    {
        const int MaxChars = 16 * 1024;
        if (animData == null || animData.Length > MaxChars) return;

        _remoteAnimations = PerCosmeticColorSerializer.DeserializeAnimations(animData);

        var pc = GetComponent<PlayerCosmetics>();
        if (pc != null) RefreshAnimators(pc);
    }

    // True when this remote has any override for the assetId — BridgeTintHelper.ApplyTypeColors uses it so it does NOT reset a customised remote cosmetic to its original colour.
    internal bool HasRemoteOverride(string assetId)
        => _remoteColors.ContainsKey(assetId)
           || _remoteAnimations.ContainsKey(assetId)
           || _remoteCustomColors.ContainsKey(assetId)
           || _remoteSlotAnimations.ContainsKey(assetId)
           || (_remoteSlotColors.TryGetValue(assetId, out var slots)        && slots.Count > 0)
           || (_remoteCustomSlotColors.TryGetValue(assetId, out var cslots) && cslots.Count > 0);

    // Synced whole-asset override for the assetId, else fallbackTypeColor — used by the death-head mount so the local store never leaks onto a remote's death head. Custom RGBs don't map to an index; full fidelity → ApplyRemoteToBridgeTint.
    internal int GetEffectiveRemoteColorIndex(string assetId, int fallbackTypeColor)
        => _remoteColors.TryGetValue(assetId, out int c) ? c : fallbackTypeColor;

    // Applies the remote's synced whole-asset overrides to vanilla PlayerMaterials (remote death head). Bridge cosmetics skipped — handled via BridgeTintMaterial.
    internal void ApplyVanillaOverridesTo(IEnumerable<PlayerMaterial> playerMaterials)
    {
        if (!PerCosmeticColors.FeatureEnabled || playerMaterials == null) return;
        bool hasAny = _remoteColors.Count > 0 || _remoteCustomColors.Count > 0;
        if (!hasAny) return;

        foreach (var pm in playerMaterials)
        {
            if (pm == null) continue;
            if (pm.cosmetic == null)
            {
                string baseId = VanillaTintHelper.BaseMeshAssetId((int)pm.cosmeticType);
                if (_remoteCustomColors.TryGetValue(baseId, out var baseCustom))
                    VanillaTintHelper.ApplyCustomRGB(pm, baseCustom);
                continue;
            }
            if (pm.cosmetic.cosmeticAsset is not { } asset || BridgeIds.IsBridgeAsset(asset)) continue;
            string assetId = asset.assetId;
            if (_remoteCustomColors.TryGetValue(assetId, out var customColor))
                VanillaTintHelper.ApplyCustomRGB(pm, customColor);
            else if (_remoteColors.TryGetValue(assetId, out int idx)
                     && idx != PerCosmeticColors.OriginalColorSentinel)
                pm.ColorSet(PerCosmeticColors.PropAlbedo,
                            PerCosmeticColors.PropEmission,
                            PerCosmeticColors.PropFresnel,
                            idx);
        }
    }

    // Applies the remote's colour for the assetId to one BTM. Priority mirrors local: per-slot custom > per-slot index > whole custom > whole index. False = no override (caller restores original). Used by the death-head mount.
    internal bool ApplyRemoteToBridgeTint(BridgeTintMaterial btm, string assetId)
    {
        if (!PerCosmeticColors.FeatureEnabled) return false;

        _remoteSlotColors.TryGetValue(assetId, out var slots);
        _remoteCustomSlotColors.TryGetValue(assetId, out var customSlots);
        bool hasWhole       = _remoteColors.TryGetValue(assetId, out int whole);
        bool hasWholeCustom = _remoteCustomColors.TryGetValue(assetId, out var wholeCustom);

        int matCount = btm.materials?.Length ?? 0;
        bool applied = false;
        for (int i = 0; i < matCount; i++)
        {
            int flatSlot = btm.SlotIdOf(i);
            Color? slotCustom  = customSlots != null && customSlots.TryGetValue(flatSlot, out var scv) ? scv : (Color?)null;
            int?   slotIndex   = slots != null && slots.TryGetValue(flatSlot, out int siv) ? siv : (int?)null;
            Color? wholeCustomC = hasWholeCustom ? wholeCustom : (Color?)null;
            int?   wholeIndex  = hasWhole ? whole : (int?)null;
            if (PerCosmeticColors.ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustomC, wholeIndex))
                applied = true;
        }
        return applied;
    }

    // Attaches/detaches BridgeColorAnimator on this remote avatar per the synced specs. Animators read PhotonNetwork.Time, so their phase matches the owner's.
    internal void RefreshAnimators(PlayerCosmetics pc)
    {
        if (pc == null) return;
        // Feature gate: when the per-cosmetic colour system or animations are off, strip animators (also hides remote players' animations).
        if (!PerCosmeticColors.FeatureEnabled || !Plugin.EnableBridgeColorAnimations.Value)
        {
            ColorAnimatorRefresher.Apply(pc, _ => default);
            return;
        }
        // Accessibility gate (hide remote animated colours): a REMOTE player's Mini-Semibot is a menu/world avatar — the plain check would treat it as local and always animate, so count it as remote too.
        var visuals = pc.playerAvatarVisuals;
        bool isRemote = visuals != null
            && ((!visuals.isMenuAvatar && visuals.playerAvatar?.isLocal != true)
                || AvatarIdentity.IsRemoteMini(pc));
        if (isRemote && !Plugin.SeeRemoteColorAnimations.Value)
        {
            ColorAnimatorRefresher.Apply(pc, _ => default);
            return;
        }
        ColorAnimatorRefresher.Apply(pc, GetRemoteAnimSet);
    }

    // Combined whole-asset + per-slot animation specs for this remote; slots with static colours punch holes in the whole-asset animation.
    internal AnimSet GetRemoteAnimSet(string assetId)
    {
        _remoteAnimations.TryGetValue(assetId, out var whole);

        IReadOnlyDictionary<int, ColorAnimation>? perSlot = null;
        if (_remoteSlotAnimations.TryGetValue(assetId, out var slotAnims) && slotAnims.Count > 0)
            perSlot = slotAnims;

        // Collect flat slots with static colours — they punch holes in the whole-asset animation.
        HashSet<int>? statics = null;
        if (whole != null || perSlot != null)
        {
            if (_remoteSlotColors.TryGetValue(assetId, out var slots) && slots.Count > 0)
            {
                statics ??= new HashSet<int>();
                foreach (var k in slots.Keys) statics.Add(k);
            }
            if (_remoteCustomSlotColors.TryGetValue(assetId, out var cslots) && cslots.Count > 0)
            {
                statics ??= new HashSet<int>();
                foreach (var k in cslots.Keys) statics.Add(k);
            }
        }
        return new AnimSet(whole, perSlot, statics);
    }

    // Applies the cached remote overrides over what SetupColorsLogic wrote. Idempotent: a bridge BTM with no
    // override is restored to its original; a vanilla PM with no override is repainted to the palette.
    internal void ApplyToCosmetics(PlayerCosmetics pc)
    {
        if (!PerCosmeticColors.FeatureEnabled || pc == null) return;

        // ── Vanilla cosmetics via PlayerMaterial ─────────────────────────────
        // Custom RGB > palette index (same order as the local apply). PMs with NO override are explicitly
        // repainted to the palette: vanilla SetupColorsLogic diff-skips PMs whose colorsEquipped entry didn't
        // change, so it never clears a colour we painted on top — the "one paint behind" repaint gap.
        if (pc.playerMaterials != null)
        {
            foreach (var pm in pc.playerMaterials)
            {
                if (pm == null) continue;
                if (pm.cosmetic == null)
                {
                    // Base mesh PM — synced synthetic base-key custom colour, else back to the palette.
                    string baseId = VanillaTintHelper.BaseMeshAssetId((int)pm.cosmeticType);
                    if (_remoteCustomColors.TryGetValue(baseId, out var baseCustom))
                        VanillaTintHelper.ApplyCustomRGB(pm, baseCustom);
                    else
                        RepaintPalette(pm, pc);
                    continue;
                }
                if (pm.cosmetic.cosmeticAsset == null) continue;
                string assetId = pm.cosmetic.cosmeticAsset.assetId;
                if (_remoteCustomColors.TryGetValue(assetId, out var customColor))
                    VanillaTintHelper.ApplyCustomRGB(pm, customColor);
                else if (_remoteColors.TryGetValue(assetId, out int colorIdx)
                         && colorIdx != PerCosmeticColors.OriginalColorSentinel)
                    pm.ColorSet(PerCosmeticColors.PropAlbedo,
                                PerCosmeticColors.PropEmission,
                                PerCosmeticColors.PropFresnel,
                                colorIdx);
                else if (!BridgeIds.IsBridgeAsset(pm.cosmetic.cosmeticAsset))
                    RepaintPalette(pm, pc);
            }
        }

        // ── Bridge cosmetics via BridgeTintMaterial ───────────────────────────
        var visuals = pc.playerAvatarVisuals;
        if (visuals == null) return;

        foreach (var btm in visuals.GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true))
        {
            if (btm?.cosmetic?.cosmeticAsset == null) continue;
            string assetId = btm.cosmetic.cosmeticAsset.assetId;
            // Only bridge cosmetics default to the author original; modded/vanilla BTMs default to the type colour.
            bool isBridge = BridgeIds.IsBridgeAsset(btm.cosmetic.cosmeticAsset);

            _remoteSlotColors.TryGetValue(assetId, out var slots);
            _remoteCustomSlotColors.TryGetValue(assetId, out var customSlots);
            bool hasWhole       = _remoteColors.TryGetValue(assetId, out int colorIdx);
            bool hasWholeCustom = _remoteCustomColors.TryGetValue(assetId, out var wholeCustom);

            bool hasStatic = hasWhole || hasWholeCustom
                             || (slots != null && slots.Count > 0)
                             || (customSlots != null && customSlots.Count > 0);
            if (!hasStatic)
            {
                // No static colour: an animation drives it, else a bridge BTM reverts to original.
                bool hasAnim = _remoteAnimations.ContainsKey(assetId)
                               || (_remoteSlotAnimations.TryGetValue(assetId, out var sa) && sa.Count > 0);
                if (!hasAnim && isBridge) btm.RestoreOriginalColor();
                continue;
            }

            // Per-material-slot pass. Priority: per-slot custom RGB > per-slot palette > whole custom RGB > whole palette.
            int matCount = btm.materials?.Length ?? 0;
            for (int i = 0; i < matCount; i++)
            {
                int flatSlot = btm.SlotIdOf(i);
                Color? slotCustom   = customSlots != null && customSlots.TryGetValue(flatSlot, out var scv) ? scv : (Color?)null;
                int?   slotIndex    = slots != null && slots.TryGetValue(flatSlot, out int siv) ? siv : (int?)null;
                Color? wholeCustomC = hasWholeCustom ? wholeCustom : (Color?)null;
                int?   wholeIndex   = hasWhole ? colorIdx : (int?)null;
                // No colour for this slot → revert a bridge slot to original (non-bridge keeps its type colour).
                if (!PerCosmeticColors.ApplySlotPrecedence(btm, i, slotCustom, slotIndex, wholeCustomC, wholeIndex)
                    && isBridge)
                    btm.RestoreOriginalColorInSlot(i);
            }
        }
    }

    // Shared palette repaint — see VanillaTintHelper.RepaintPalette (vanilla's colorsEquipped diff never
    // repaints a PM we painted over, so removals must repaint explicitly).
    private static void RepaintPalette(PlayerMaterial pm, PlayerCosmetics pc)
        => VanillaTintHelper.RepaintPalette(pm, pc);
}
