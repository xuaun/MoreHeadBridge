// ============================================================================
// A managed, visual-only death-head model for the CosmeticOverridePopup preview, shown while the user edits a Player_DeathHead offset.
// Owns the spawned model's lifecycle plus the crown/offset on it; colouring → DeathHeadColorizer, gameplay strip → DeathHeadModelStripper, mounting → DeathHeadCosmeticMounter.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class DeathHeadPreviewInstance
{
    // Cosmetic anchors / crown mesh names on the death-head prefab (from the Phase-0 dump).
    private const string HeadAnchorName = "Cosmetic Parent - Head Top";
    private const string FallbackAnchorName = "Cosmetics";
    private const string CrownMeshName = "Crown Mesh";

    // Local offset of the spawned model from the avatar visuals transform — lifts it into frame once the normal avatar is hidden (at zero it lands near the avatar's feet).
    private static readonly Vector3 SpawnLocalOffset = new(0f, 0.8f, 0f);

    private GameObject? _container;   // inactive = hidden, active = shown
    private GameObject? _instance;    // the stripped death-head model
    private Transform? _headAnchor;   // fallback mount anchor (Hat)
    private Transform? _crownMesh;
    private DeathHeadCosmeticMounter? _mounter;

    internal bool IsSpawned => _instance != null;

    // Lazily spawns the model under anchor (the preview's playerAvatarVisuals transform), tinted with the player's colors, stripped to visual-only, starting hidden.
    internal bool TryEnsure(Transform anchor, PlayerCosmetics? colorSource)
    {
        if (_instance != null) return true;

        var prefab = DeathHeadPrefabProvider.Prefab;
        if (prefab == null)
            return false;

        try
        {
            _container = new GameObject("MHB_DeathHeadPreview");
            _container.SetActive(false); // keep inactive so stripped components never Awake

            _instance = UnityEngine.Object.Instantiate(prefab, _container.transform);

            // Color + read the cosmetic anchors BEFORE stripping the PlayerCosmetics/PlayerMaterial that hold them (baked colors and cached anchors survive the strip).
            DeathHeadColorizer.ApplyBodyColors(_instance, colorSource);
            var parents = ReadCosmeticParents(_instance);
            DeathHeadModelStripper.StripGameplay(_instance);
            DeathHeadModelStripper.CleanVisualClutter(_instance);

            _headAnchor = DeathHeadModelStripper.FindDeep(_instance.transform, HeadAnchorName)
                          ?? DeathHeadModelStripper.FindDeep(_instance.transform, FallbackAnchorName);
            _crownMesh = DeathHeadModelStripper.FindDeep(_instance.transform, CrownMeshName);

            _container.transform.SetParent(anchor, worldPositionStays: false);
            _container.transform.localPosition = SpawnLocalOffset;
            _container.transform.localRotation = Quaternion.identity;
            _container.transform.localScale = Vector3.one;

            _mounter = new DeathHeadCosmeticMounter(parents, _headAnchor, colorSource);
            return true;
        }
        catch
        {
            Destroy();
            return false;
        }
    }

    // Reads the death head's real CosmeticParents (type → anchor + base meshes/parents) from its PlayerCosmetics.cosmeticParents, BEFORE StripGameplay destroys that component.
    private static Dictionary<SemiFunc.CosmeticType, PlayerCosmetics.CosmeticParent> ReadCosmeticParents(GameObject instance)
    {
        var map = new Dictionary<SemiFunc.CosmeticType, PlayerCosmetics.CosmeticParent>();
        var pc = instance.GetComponentInChildren<PlayerCosmetics>(true);
        if (pc?.cosmeticParents == null) return map;
        foreach (var cp in pc.cosmeticParents)
        {
            if (cp?.parent == null) continue;
            map[cp.cosmeticType] = cp;
        }
        return map;
    }

    internal void MountCosmetics(
        List<(GameObject liveGo, CosmeticAsset asset)> cosmetics, GameObject? configuredLiveGo)
        => _mounter?.Mount(cosmetics, configuredLiveGo);

    // Overrides the mounter's animation-spec resolver for decorative clones (used by the Mini-Semibot death head). null (popup preview) keeps the default global-on + local-store path.
    internal void SetAnimOverride(System.Func<string, AnimSet>? resolver)
    {
        if (_mounter != null) _mounter.AnimOverride = resolver;
    }

    // Statically applies each decorative clone's Player_DeathHead offset (Mini-Semibot death head); the popup preview drives the configured cosmetic through ApplyOffset instead.
    internal void ApplyDeathHeadOffsets(System.Func<string, CosmeticOffsetEntry?> resolver)
        => _mounter?.ApplyDeathHeadOffsets(resolver);

    // Applies the DeathHead offset's transform to the configured cosmetic mount, or resets to identity when null. Mirrors how CosmeticOffsetCondition positions it.
    internal void ApplyOffset(CosmeticOffsetEntry? offset)
    {
        var mount = _mounter?.ConfiguredMount;
        if (mount == null) return;
        var t = mount.transform;
        if (offset == null)
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return;
        }
        t.localPosition = new Vector3(offset.PosX, offset.PosY, offset.PosZ);
        t.localEulerAngles = new Vector3(offset.RotX, offset.RotY, offset.RotZ);
        t.localScale = new Vector3(offset.ScaleX, offset.ScaleY, offset.ScaleZ);
    }

    internal void SetCrownVisible(bool visible)
    {
        if (_crownMesh != null)
            _crownMesh.gameObject.SetActive(visible);
    }

    // Transform of the cosmetic currently being configured, used by the floor-pose preview animation. Null when nothing is mounted/configured.
    internal Transform? ConfiguredMountTransform
        => _mounter?.ConfiguredMount != null ? _mounter.ConfiguredMount.transform : null;

    // Shows/hides the cosmetic currently being configured (the one carrying the death-head offset), so the preview reflects the "Show on Death Head" toggle.
    internal void SetConfiguredCosmeticVisible(bool visible)
    {
        var mount = _mounter?.ConfiguredMount;
        if (mount != null && mount.activeSelf != visible)
            mount.SetActive(visible);
    }

    internal void Show(bool show)
    {
        if (_container != null)
            _container.SetActive(show);
    }

    // Uniform scale of the whole model — used for the spawn pop-in animation.
    internal void SetScaleFactor(float factor)
    {
        if (_container != null)
            _container.transform.localScale = Vector3.one * factor;
    }

    internal void Destroy()
    {
        _mounter?.Clear();
        _mounter = null;
        if (_container != null)
        {
            UnityEngine.Object.Destroy(_container);
            _container = null;
        }
        _instance = null;
        _headAnchor = null;
        _crownMesh = null;
    }
}
