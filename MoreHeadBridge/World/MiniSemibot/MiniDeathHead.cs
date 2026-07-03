using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Wraps the death-head PREVIEW pipeline to turn the mini into a static, visual-only death head in its CURRENT outfit ("When You Die → Death Head"). Built once on the first ShowAt (outfit is fixed at death), positioned each frame.
internal sealed class MiniDeathHead
{

    private DeathHeadPreviewInstance? _instance;
    private GameObject? _holder;
    private bool _built;
    private bool _failed;   // remember a build failure so we don't retry (and re-instantiate) every frame

    // Colour-store version baked into the model; -1 for remote minis (their colours come from the sync
    // cache — RefreshRemoteMiniColors invalidates on receive instead).
    private int _builtStoreVersion = -1;

    // (Re)positions the model at worldPos/rot/scale, building it once. False if it couldn't build (caller falls back to a non-death-head pose); reset by Destroy on revive.
    internal bool ShowAt(PlayerAvatarVisuals miniVisuals, Vector3 worldPos, Quaternion rot, float scale, bool crown)
    {
        if (_failed) return false;

        // Local mini: a colour edit (per-cosmetic paint, custom, animation) bumps the store version — rebuild so the baked model shows it.
        if (_built && _builtStoreVersion >= 0 && _builtStoreVersion != PerCosmeticColors.StoreVersion)
            Destroy();

        if (!_built && !Build(miniVisuals, crown)) return false;

        if (_holder != null)
        {
            _holder.transform.position = worldPos;
            _holder.transform.rotation = rot;
            _holder.transform.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
        }
        return true;
    }

    private bool Build(PlayerAvatarVisuals miniVisuals, bool crown)
    {
        _built = true;
        var miniPc = miniVisuals != null ? miniVisuals.playerCosmetics : null;
        if (miniPc == null) { _failed = true; return false; }

        _builtStoreVersion = AvatarIdentity.IsRemoteMini(miniPc) ? -1 : PerCosmeticColors.StoreVersion;

        // The mini is dressed/coloured in DATA even while "Hide" keeps the body SetActive(false), so the gather and ApplyBodyColors work straight off its PlayerCosmetics, hidden or not.
        try
        {
            _holder = new GameObject("MHB_MiniDeathHead");
            _instance = new DeathHeadPreviewInstance();

            // TryEnsure colours the body too: a RandomPreset mini's colours live in the PRESET store, so resolve under the preset context (matches how the mini was dressed); SameAsPlayer reads the live store.
            int presetSlot = MiniSemibotSpawner.PresetSlotOf(miniPc);
            bool ensured;
            if (presetSlot >= 0)
            {
                bool ok = false;
                PerCosmeticColors.RunWithPresetContext(presetSlot, () => ok = _instance.TryEnsure(_holder.transform, miniPc));
                ensured = ok;
            }
            else
            {
                ensured = _instance.TryEnsure(_holder.transform, miniPc);
            }
            if (!ensured) { Destroy(); _failed = true; return false; }

            // Feed the mounter an animation-spec resolver for THIS mini (local store / rolled preset / owner's synced specs) — without it the death-head clones bake a static colour.
            _instance.SetAnimOverride(BuildAnimResolver(miniPc));

            // Hide → Death Head builds while the body is STILL inactive, and cloning from an inactive-in-hierarchy source yields empty clones (no hats). Reactivate just for gather + clone, then restore — inside LateUpdate, so no flicker.
            var bodyGo = miniVisuals != null ? miniVisuals.gameObject : null;
            bool reactivatedForBuild = false;
            if (bodyGo != null && !bodyGo.activeSelf) { bodyGo.SetActive(true); reactivatedForBuild = true; }
            try
            {
                var gathered = GatherMiniCosmetics(miniPc);

                // Cosmetics dressed while hidden never ran Cosmetic.Update — the equip animation is frozen at scale 0, so clones would be invisible. Fast-forward each one before cloning.
                foreach (var (go, _) in gathered)
                    if (go != null) CosmeticEquipAnimation.Finish(go);

                _instance.MountCosmetics(gathered, configuredLiveGo: null);
            }
            finally
            {
                if (reactivatedForBuild && bodyGo != null) bodyGo.SetActive(false);
            }
            _instance.ApplyOffset(null);
            // Static stand-in for the live CosmeticOffsetCondition: apply each cosmetic's Player_DeathHead offset so the mini's death head matches the real one.
            _instance.ApplyDeathHeadOffsets(BuildDeathHeadOffsetResolver(miniPc));
            _instance.SetCrownVisible(crown);
            _instance.Show(true);
            return true;
        }
        catch
        {
            Destroy();
            _failed = true;
            return false;
        }
    }

    // Every supported cosmetic the mini wears (multi-equip extras live in cosmeticEquipped) — same gather as the menu preview but sourced from the MINI's cosmetics, so it works for SameAsPlayer/RandomPreset, local/remote.
    private static List<(GameObject go, CosmeticAsset asset)> GatherMiniCosmetics(PlayerCosmetics pc)
    {
        var list = new List<(GameObject, CosmeticAsset)>();

        // Type filter uses the WEARER's effective type: a remote mini follows its owner's broadcast Type override, never the viewer's local one. "Show on Death Head = No" honoured like the real death head (owner-authoritative).
        bool isRemote = AvatarIdentity.TryGetRemoteActor(pc, out int actor);
        bool Supported(CosmeticAsset asset)
        {
            SemiFunc.CosmeticType t;
            if (isRemote)
            {
                CustomizerSync.TryGetRemote(actor, asset.assetId, out var rd);
                t = MoreHeadCosmeticMountPatch.GetRemoteEffectiveType(asset, rd).cosmeticType;
            }
            else
            {
                t = asset.type;
            }
            return DeathHeadPrefabProvider.SupportedTypes.Contains(t)
                && !BridgeDeathHeadGameplayMount.IsHiddenOnDeathHead(asset, isRemote, actor);
        }

        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(pc);
        if (equipped != null)
            foreach (var c in equipped)
            {
                if (c == null) continue;
                var asset = MoreHeadCosmeticMountPatch.GetCosmeticAsset(c);
                if (asset != null && Supported(asset))
                    list.Add((c.gameObject, asset));
            }

        return list;
    }

    // assetId → its Player_DeathHead offset entry: the owner's broadcast for a remote mini, else the local override store. Null when no death-head offset.
    private static System.Func<string, CosmeticOffsetEntry?> BuildDeathHeadOffsetResolver(PlayerCosmetics miniPc)
    {
        bool isRemote = AvatarIdentity.TryGetRemoteActor(miniPc, out int actor);
        return id =>
        {
            List<CosmeticOffsetEntry>? offsets;
            if (isRemote)
            {
                CustomizerSync.TryGetRemote(actor, id, out var rd);
                offsets = rd?.Offsets;
            }
            else
            {
                CustomizerStore.TryGet(id, out var d);
                offsets = d?.Offsets;
            }
            if (offsets == null) return null;
            foreach (var e in offsets)
                if (e.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead) return e;
            return null;
        };
    }

    // Animation-spec resolver for this mini's death head; empty-set resolver when animations are globally off (death head stays static).
    private static System.Func<string, AnimSet> BuildAnimResolver(PlayerCosmetics miniPc)
    {
        if (!PerCosmeticColors.FeatureEnabled || !Plugin.EnableBridgeColorAnimations.Value)
            return _ => default;

        // Remote player's mini: specs come from the owner's synced data on the mini's sync component; the viewer's "see remote animated colours" preference still applies.
        if (AvatarIdentity.IsRemoteMini(miniPc))
        {
            if (!Plugin.SeeRemoteColorAnimations.Value) return _ => default;
            var sync = miniPc.GetComponent<PerCosmeticColorSyncComponent>();
            return sync != null ? sync.GetRemoteAnimSet : (System.Func<string, AnimSet>)(_ => default);
        }

        // Local RandomPreset mini: resolve under the rolled preset's colour context — its specs live in the PREVIEW maps, so GetPreviewAnimSet, NOT GetAnimSet (live store), matching how SetupColorsOverridePatches binds the live body.
        int slot = MiniSemibotSpawner.PresetSlotOf(miniPc);
        if (slot >= 0)
            return id =>
            {
                AnimSet r = default;
                PerCosmeticColors.RunWithPresetContext(slot, () =>
                    r = CustomizerStore.GetEffectiveColorAnimations(id) ? PerCosmeticColors.GetPreviewAnimSet(id) : default);
                return r;
            };

        // Local SameAsPlayer mini: the live store, per-cosmetic gated.
        return id => CustomizerStore.GetEffectiveColorAnimations(id)
            ? PerCosmeticColors.GetAnimSet(id) : default;
    }

    internal void Destroy()
    {
        _instance?.Destroy();
        _instance = null;
        if (_holder != null) { Object.Destroy(_holder); _holder = null; }
        _built = false;
        _failed = false;
    }
}
