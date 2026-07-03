// Mounts the player's supported cosmetics onto a spawned death-head model and owns their lifetime (Clear destroys them and restores hidden meshes).
// Decorative cosmetics are CLONED from the live instance (keeping their colours). Mesh cosmetics with a re-parent target are re-mounted from the PREFAB, replicating Cosmetic.Setup's mesh-swap � the live GO is empty because its meshes already live on the AVATAR's bones. Hide-only types (legs) stay as the death head's own legs: colour only, no swap.

using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class DeathHeadCosmeticMounter
{
    private readonly IReadOnlyDictionary<SemiFunc.CosmeticType, PlayerCosmetics.CosmeticParent> _parentByType;
    private readonly Transform? _fallbackAnchor;   // Hat anchor, used when a type has no entry
    private readonly PlayerCosmetics? _colorSource;

    private readonly List<GameObject> _mounted = new();          // destroyed on Clear
    private readonly List<GameObject> _hiddenBaseMeshes = new(); // re-shown on Clear

    // Decorative clone ? its asset id, captured at mount time (StripGameplay removes the clone's Cosmetic component, so it can't be resolved later). Drives ApplyDeathHeadOffsets.
    private readonly Dictionary<GameObject, string> _cloneAssetIds = new();

    // The mount of the cosmetic being edited � receives the live offset.
    internal GameObject? ConfiguredMount { get; private set; }

    // Optional resolver for decorative clones' animation specs (set by the Mini-Semibot death head). Non-null fully decides the AnimSet per assetId; null = global-on + local-store (popup preview).
    internal System.Func<string, AnimSet>? AnimOverride;

    internal DeathHeadCosmeticMounter(
        IReadOnlyDictionary<SemiFunc.CosmeticType, PlayerCosmetics.CosmeticParent> parentByType,
        Transform? fallbackAnchor, PlayerCosmetics? colorSource)
    {
        _parentByType = parentByType;
        _fallbackAnchor = fallbackAnchor;
        _colorSource = colorSource;
    }

    // <paramref name="configuredLiveGo"/> is the one being edited � its mount becomes ConfiguredMount.
    internal void Mount(List<(GameObject liveGo, CosmeticAsset asset)> cosmetics, GameObject? configuredLiveGo)
    {
        Clear();

        foreach (var (liveGo, asset) in cosmetics)
        {
            if (liveGo == null || asset == null) continue;
            bool isConfigured = liveGo == configuredLiveGo;

            // Owner-authoritative type: a REMOTE mini follows its owner's Type override, never the viewer-mutated shared asset.type, else a friend's mesh-switch leaks onto our view.
            var effType = OwnerType(asset);

            if (!_parentByType.TryGetValue(effType, out var cp) || cp?.parent == null)
            {
                if (_fallbackAnchor != null) MountDecorativeClone(liveGo, _fallbackAnchor, isConfigured);
                continue;
            }

            bool hasReparentTarget = cp.baseMeshParents != null && cp.baseMeshParents.Count > 0;
            bool hideOnly = !hasReparentTarget && cp.baseMeshes != null && cp.baseMeshes.Count > 0;

            if (hasReparentTarget)
            {
                if (BridgeIds.IsBridgeAsset(asset))
                    MountBridgeMeshCosmetic(liveGo, effType, cp, isConfigured); // bridge: clone live GO
                else
                    MountMeshCosmetic(asset, effType, cp, isConfigured);        // vanilla: swap onto bones
            }
            else if (hideOnly)
                continue;                                        // legs: keep the death head's own
                                                                 // legs (color only) � no mesh swap
            else
                MountDecorativeClone(liveGo, cp.parent, isConfigured); // decorative: clone live GO
        }
    }

    internal void Clear()
    {
        foreach (var c in _mounted)
            if (c != null) UnityEngine.Object.Destroy(c);
        _mounted.Clear();
        _cloneAssetIds.Clear();
        // Re-show any death-head base meshes a mesh-swap hid, so a re-mount starts clean.
        foreach (var bm in _hiddenBaseMeshes)
            if (bm != null) bm.SetActive(true);
        _hiddenBaseMeshes.Clear();
        ConfiguredMount = null;
    }

    // Clones the already-built live cosmetic GO onto the anchor (keeps its colors); visual-only.
    private void MountDecorativeClone(GameObject src, Transform anchor, bool isConfigured)
    {
        try
        {
            var clone = UnityEngine.Object.Instantiate(src, anchor, worldPositionStays: false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;

            var asset = src.GetComponentInChildren<Cosmetic>(true)?.cosmeticAsset;
            DeathHeadModelStripper.StripGameplay(clone);   // also removes the clone's BridgeTintMaterials
            if (asset != null) AttachPreviewAnimator(clone, asset);

            _mounted.Add(clone);
            if (asset?.assetId != null) _cloneAssetIds[clone] = asset.assetId;
            if (isConfigured) ConfiguredMount = clone;
        }
        catch (System.Exception ex) { BridgeLog.Debug($"DeathHead: skipped a cosmetic clone � {ex.Message}"); }
    }

    /// Applies each decorative clone's Player_DeathHead offset statically � the static preview
    /// pipeline has no live CosmeticOffsetCondition, so the trigger would never fire. Resolver:
    /// assetId ? its DeathHead offset entry (null = none). Same TRS application as ApplyOffset.
    /// Mesh-swapped cosmetics live on the head's own bones and are left alone.
    internal void ApplyDeathHeadOffsets(System.Func<string, CosmeticOffsetEntry?> resolver)
    {
        foreach (var kvp in _cloneAssetIds)
        {
            var go = kvp.Key;
            if (go == null) continue;
            var offset = resolver(kvp.Value);
            if (offset == null) continue;

            var t = go.transform;
            t.localPosition = new Vector3(offset.PosX, offset.PosY, offset.PosZ);
            t.localEulerAngles = new Vector3(offset.RotX, offset.RotY, offset.RotZ);
            t.localScale = new Vector3(offset.ScaleX, offset.ScaleY, offset.ScaleZ);
        }
    }

    // The strip destroyed the clone's BridgeTintMaterials (the baked static colour survives on the instance materials). Re-inject tint materials + an animator so the preview animates; no-op for static cosmetics.
    private void AttachPreviewAnimator(GameObject clone, CosmeticAsset asset)
    {
        try
        {
            AnimSet set;
            if (AnimOverride != null)
            {
                // Mini-Semibot death head: the resolver already encodes feature/animation gating and the right spec source (local / preset / synced-remote).
                set = AnimOverride(asset.assetId);
            }
            else
            {
                if (!PerCosmeticColors.FeatureEnabled || !Plugin.EnableBridgeColorAnimations.Value) return;
                set = PerCosmeticColors.GetAnimSet(asset.assetId);
            }
            if (!set.Any) return;

            BridgeTintHelper.InjectBridgeTintMaterials(clone, asset);   // strip removed the originals
            var anim = clone.AddComponent<BridgeColorAnimator>();
            anim.Init(clone, set);
            if (anim.IsEmpty) anim.Stop();
        }
        catch (System.Exception ex) { BridgeLog.Debug($"DeathHead: preview animation failed � {ex.Message}"); }
    }

    // A bridge mesh-switch cosmetic keeps its BUILT mesh + baked tint on the LIVE GO (CosmeticSetupPatch leaves its meshParents empty so vanilla Setup never re-parents/strips it), so clone the live GO like a decorative cosmetic � the raw prefab is unbuilt. Then hide the death head's own base meshes for mesh-switch types (by the OWNER's effType), mirroring the live avatar's swap.
    private void MountBridgeMeshCosmetic(GameObject liveGo, SemiFunc.CosmeticType effType, PlayerCosmetics.CosmeticParent cp, bool isConfigured)
    {
        MountDecorativeClone(liveGo, cp.parent, isConfigured);

        if (IsMeshSwitch(effType) && cp.baseMeshes != null)
            foreach (var bm in cp.baseMeshes)
                if (bm != null) { bm.gameObject.SetActive(false); _hiddenBaseMeshes.Add(bm.gameObject); }
    }

    // The OWNER's effective cosmetic type for the colour source (a remote mini follows its owner's broadcast Type override, never the viewer-mutated shared asset.type). Mirrors GatherMiniCosmetics.
    private SemiFunc.CosmeticType OwnerType(CosmeticAsset asset)
    {
        if (_colorSource != null && AvatarIdentity.TryGetRemoteActor(_colorSource, out int actor))
        {
            CustomizerSync.TryGetRemote(actor, asset.assetId, out var rd);
            return MoreHeadCosmeticMountPatch.GetRemoteEffectiveType(asset, rd).cosmeticType;
        }
        return asset.type;
    }

    // Re-mounts a mesh cosmetic from its prefab, replicating Cosmetic.Setup's mesh-swap onto the death head's own base meshes/bones. effType = the OWNER's resolved type (drives mesh-switch).
    private void MountMeshCosmetic(CosmeticAsset asset, SemiFunc.CosmeticType effType, PlayerCosmetics.CosmeticParent cp, bool isConfigured)
    {
        try
        {
            if (asset.prefab == null || !asset.prefab.IsValid()) return;
            var prefab = asset.prefab.Prefab;
            if (prefab == null) return;

            var go = UnityEngine.Object.Instantiate(prefab, cp.parent);
            var meshParents = go.GetComponent<Cosmetic>()?.meshParents;
            bool meshSwitch = IsMeshSwitch(effType);

            if (cp.resetTransform)
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            // Color the meshes BEFORE the mesh-swap re-parents them OUT of `go` — afterwards GetComponentsInChildren on `go` wouldn't find them and they'd stay default.
            DeathHeadColorizer.ApplyCosmeticColor(go, asset, _colorSource);

            // Setup(): meshSwitch hides the death head's base meshes (and, briefly, the cosmetic's).
            if (meshSwitch)
            {
                if (cp.baseMeshes != null)
                    foreach (var bm in cp.baseMeshes)
                        if (bm != null) { bm.gameObject.SetActive(false); _hiddenBaseMeshes.Add(bm.gameObject); }
                if (meshParents != null)
                    foreach (var mp in meshParents)
                        if (mp != null) mp.gameObject.SetActive(false);
            }

            // Setup(): re-parent the cosmetic's meshParents onto the death head's baseMeshParents.
            if (cp.baseMeshParents != null && meshParents != null)
            {
                int num = 0;
                foreach (var bmp in cp.baseMeshParents)
                {
                    if (num >= meshParents.Count) break;
                    var mp = meshParents[num];
                    if (mp != null && bmp != null)
                    {
                        mp.gameObject.SetActive(true);
                        mp.SetParent(bmp);
                        if (cp.resetTransform)
                        {
                            mp.localPosition = Vector3.zero;
                            mp.localRotation = Quaternion.identity;
                            mp.localScale = Vector3.one;
                        }
                    }
                    num++;
                }
            }

            // Safety net: reactivate any meshParents the swap deactivated but never re-parented (more meshParents than baseMeshParents) so they don't silently vanish.
            if (meshParents != null)
                foreach (var mp in meshParents)
                    if (mp != null && mp.IsChildOf(go.transform) && !mp.gameObject.activeSelf)
                        mp.gameObject.SetActive(true);

            DeathHeadModelStripper.StripGameplay(go);
            // The RAW prefab carries the cosmetic's icon-maker subtree (SemiIcon Maker camera + "Icon Light" spotlights, culling mask = all layers) that vanilla Cosmetic.Setup deactivates but our manual mount doesn't — those spotlights blow out the death-head mini. Strip them and other non-visual clutter. Decorative clones come from the live GO where vanilla already deactivated it, so they're unaffected.
            DeathHeadModelStripper.CleanVisualClutter(go);

            _mounted.Add(go);
            // meshSwitch re-parents meshParents OUT of go (onto the death head bones) — track them so Clear removes them on re-mount (Destroying go alone would leave them behind).
            if (meshParents != null)
                foreach (var mp in meshParents)
                    if (mp != null && !mp.IsChildOf(go.transform)) _mounted.Add(mp.gameObject);

            if (isConfigured) ConfiguredMount = go;
        }
        catch (System.Exception ex) { BridgeLog.Debug($"DeathHead: skipped a cosmetic mount — {ex.Message}"); }
    }

    private static bool IsMeshSwitch(SemiFunc.CosmeticType type)
    {
        var typeAssets = MetaManager.instance?.cosmeticTypeAssets;
        if (typeAssets == null) return false;
        int idx = (int)type;
        if (idx < 0 || idx >= typeAssets.Count) return false;
        return typeAssets[idx]?.meshSwitch ?? false;
    }
}
