// Live-preview avatar inside CosmeticOverridePopup: shows pending (unsaved) overrides in real time.
// Init() makes it the active PlayerAvatarMenu (NOT restored until OnDestroy) and freezes the real cosmetics-menu avatar (iconMakerAvatar=true).

using MenuLib.MonoBehaviors;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class CosmeticOverridePreview : MonoBehaviour
{
    // ── Layout ────────────────────────────────────────────────────────────────
    // Local position relative to the popup transform; pivot = Vector2.right → avatar's bottom-right corner.
    private const float PreviewX = 0f;
    private const float PreviewY = 0f;

    // Must match MenuAPI.CreateREPOAvatarPreview defaults.
    private const float PreviewWidth = 184f;
    private const float PreviewHeight = 345f;

    // "Preview" label: positioned above the avatar, centred on its width.
    private const float PreviewLabelHeight = 30f;
    private const float PreviewLabelGapY = 6f;   // gap between top of avatar and bottom of label

    // ── State ─────────────────────────────────────────────────────────────────
    private CosmeticAsset? _asset;
    private PlayerAvatarMenu? _avatarMenu;
    private PlayerAvatarVisuals? _visuals;

    // Death-head sub-preview, shown while editing a Player_DeathHead offset.
    private readonly DeathHeadPreviewInstance _deathHead = new();
    private bool _deathHeadActive;
    private bool _normalCrownWasActive;
    private bool _normalCrownComponentWasEnabled;
    private Coroutine? _spawnAnim;

    // The active PlayerAvatarMenu.instance when the popup opened; paused (iconMakerAvatar=true) and restored on destroy.
    private PlayerAvatarMenu? _cosmeticsMenuAvatar;
    private bool _cosmeticsMenuAvatarWasIconMaker;

    internal PlayerCosmetics? PreviewPc { get; private set; }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// Creates and wires the preview.  Must be called once after this component
    /// is added to the popup GO (before the popup is opened).
    internal void Init(CosmeticAsset asset, REPOPopupPage popup)
    {
        _asset = asset;

        // ── Pause the cosmetics-menu avatar ──────────────────────────────────
        // iconMakerAvatar=true stops the menu avatar's Update() (rotation/lookat/self-destruct) but NOT FixedUpdate — the Rigidbody stays pinned at startPosition.
        _cosmeticsMenuAvatar = PlayerAvatarMenu.instance;
        if (_cosmeticsMenuAvatar != null)
        {
            _cosmeticsMenuAvatarWasIconMaker = _cosmeticsMenuAvatar.iconMakerAvatar;
            _cosmeticsMenuAvatar.iconMakerAvatar = true;
        }

        // ── Create the preview avatar ─────────────────────────────────────────
        // Awake set PlayerAvatarMenu.instance = preview. NOT restored here: while the popup is open, lookat/TryMount/etc. must use the preview.
        REPOAvatarPreview avatarPreview;
        try
        {
            avatarPreview = MenuLib.MenuAPI.CreateREPOAvatarPreview(
                popup.transform,
                new Vector2(PreviewX, PreviewY));
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"CosmeticOverridePreview: failed to create — {ex.Message}");
            if (_cosmeticsMenuAvatar != null)
                _cosmeticsMenuAvatar.iconMakerAvatar = _cosmeticsMenuAvatarWasIconMaker;
            return;
        }

        // playerAvatarVisuals is a child of PlayerAvatarMenu; Start() hasn't run yet so the hierarchy is still intact.
        _avatarMenu = avatarPreview.playerAvatarVisuals.GetComponentInParent<PlayerAvatarMenu>();
        if (_avatarMenu == null)
        {
            BceConsole.LogWarning("CosmeticOverridePreview: PlayerAvatarMenu not found");
            return;
        }
        _visuals = avatarPreview.playerAvatarVisuals;

        // iconMakerAvatar stays FALSE on the preview: Start() re-parents body+camera to world space, Update() keeps rotation/lookat working, FixedUpdate() pins it at startPosition.
        PreviewPc = _avatarMenu.GetComponentInChildren<PlayerCosmetics>();
        if (PreviewPc == null)
        {
            BceConsole.LogWarning("CosmeticOverridePreview: PlayerCosmetics not found");
            return;
        }

        // ── "Preview" label above the avatar ─────────────────────────────────
        // Parented to the REPOAvatarPreview (follows its SetAsLastSibling); per-axis inverted scale cancels the template's mirroring.
        AddPreviewLabel(avatarPreview, popup.headerTMP);

        // Initial pass: show saved state (pending == saved at popup open).
        RefreshFull(null);
    }

    // ── Death-head sub-preview ──────────────────────────────────────────────────

    /// Enters death-head mode: hides the normal avatar body and shows the death-head model
    /// with the player's cosmetics mounted on it.
    /// <paramref name="crownConfigured"/> mirrors the avatar's crown visibility rule.
    internal void EnterDeathHeadMode(GameObject? configuredCosmetic, bool crownConfigured,
                                     CosmeticOffsetEntry? offset)
    {
        if (_visuals == null) return;
        if (!_deathHead.TryEnsure(_visuals.transform, PreviewPc)) return;

        // Mount every supported cosmetic (multi-equip aware); the configured one gets the offset.
        var cosmetics = GetDeathHeadCosmeticGos();
        if (configuredCosmetic != null && _asset != null)
        {
            bool present = false;
            foreach (var c in cosmetics)
                if (c.go == configuredCosmetic) { present = true; break; }
            if (!present)
                cosmetics.Add((configuredCosmetic, _asset));
        }

        _deathHead.MountCosmetics(cosmetics, configuredCosmetic);
        _deathHead.ApplyOffset(offset);
        // Show the death-head's own crown when this cosmetic configures one.
        _deathHead.SetCrownVisible(crownConfigured);
        SetNormalCrownVisible(false);
        _deathHead.Show(true);
        SetAvatarBodyVisible(false);
        _deathHeadActive = true;

        if (_spawnAnim != null) StopCoroutine(_spawnAnim);
        _spawnAnim = StartCoroutine(SpawnAnimation());
    }

    /// Live-updates the offset applied to the death-head cosmetic clone (from slider changes).
    internal void UpdateDeathHeadOffset(CosmeticOffsetEntry? offset)
    {
        if (_deathHeadActive)
            _deathHead.ApplyOffset(offset);
    }

    /// Shows/hides the configured cosmetic on the death-head preview, mirroring the
    /// "Show on Death Head" toggle while the user edits it.
    internal void SetDeathHeadConfiguredCosmeticVisible(bool visible)
    {
        if (_deathHeadActive)
            _deathHead.SetConfiguredCosmeticVisible(visible);
    }

    // ── Floor-pose preview animation ──────────────────────────────────────────
    // Each slider change restarts a one-shot squish/unsquish from the SAME captured base pose, so the target is always the current slider values.
    private Coroutine? _floorAnim;
    private bool _floorBaseCaptured;
    private DeathHeadFloorPose _floorTarget = new();
    private Vector3 _floorBasePos, _floorBaseEuler, _floorBaseScale;

    // How long to hold at the squished pose before returning — long enough to read clearly.
    private const float FloorSquishHoldTime = 1.0f;

    /// Fires a one-shot squish preview. Captures the base on the first call (popup open or
    /// Off→On toggle) so every animation starts from the same reference offset pose.
    internal void SetDeathHeadFloorAnimation(DeathHeadFloorPose pose)
    {
        var t = _deathHead.ConfiguredMountTransform;
        if (!_deathHeadActive || t == null) return;

        _floorTarget = pose;

        if (!_floorBaseCaptured)
        {
            // Capture the current offset pose as the base. Fires on popup open with Enabled=true and on Off→On toggle (StopDeathHeadFloorAnimation clears _floorBaseCaptured).
            _floorBasePos = t.localPosition;
            _floorBaseEuler = t.localEulerAngles;
            _floorBaseScale = t.localScale;
            _floorBaseCaptured = true;
        }

        // Every call restarts the one-shot from the captured base (including the first).
        if (_floorAnim != null) StopCoroutine(_floorAnim);
        t.localPosition = _floorBasePos;
        t.localEulerAngles = _floorBaseEuler;
        t.localScale = _floorBaseScale;
        _floorAnim = StartCoroutine(FloorAnimOneShot(t));
    }

    /// Fires a one-shot impact-pose preview on the LIVE override cosmetic (not the death head), so
    /// edits in the Impact Pose popup are visible while alive. Reuses the floor-anim machinery,
    /// targeting the configured cosmetic's root instead of the death-head mount.
    internal void PlayImpactPosePreview(DeathHeadFloorPose pose)
    {
        var go = FindPreviewCosmeticGo();
        var t = go != null ? go.transform : null;
        if (t == null) return;

        _floorTarget = pose;

        if (!_floorBaseCaptured)
        {
            _floorBasePos = t.localPosition;
            _floorBaseEuler = t.localEulerAngles;
            _floorBaseScale = t.localScale;
            _floorBaseCaptured = true;
        }

        if (_floorAnim != null) StopCoroutine(_floorAnim);
        t.localPosition = _floorBasePos;
        t.localEulerAngles = _floorBaseEuler;
        t.localScale = _floorBaseScale;
        _floorAnim = StartCoroutine(FloorAnimOneShot(t));
    }

    /// Stops the impact-pose preview and restores the live cosmetic to its captured base pose.
    internal void StopImpactPosePreview()
    {
        if (_floorAnim != null) { StopCoroutine(_floorAnim); _floorAnim = null; }

        var go = FindPreviewCosmeticGo();
        var t = go != null ? go.transform : null;
        if (t != null && _floorBaseCaptured)
        {
            t.localPosition = _floorBasePos;
            t.localEulerAngles = _floorBaseEuler;
            t.localScale = _floorBaseScale;
        }
        _floorBaseCaptured = false;
    }

    /// Stops any running animation and restores the cosmetic to its captured base pose.
    internal void StopDeathHeadFloorAnimation()
    {
        if (_floorAnim != null) { StopCoroutine(_floorAnim); _floorAnim = null; }

        var t = _deathHead.ConfiguredMountTransform;
        if (t != null && _floorBaseCaptured)
        {
            t.localPosition = _floorBasePos;
            t.localEulerAngles = _floorBaseEuler;
            t.localScale = _floorBaseScale;
        }
        _floorBaseCaptured = false;
    }

    // One-shot: base → floor pose → hold → base.  Restartable mid-flight (always from base).
    private IEnumerator FloorAnimOneShot(Transform t)
    {
        float phase = 0f;

        while (phase < 1f)
        {
            if (t == null) yield break;
            phase = Mathf.MoveTowards(phase, 1f, Time.deltaTime * Mathf.Max(0.1f, _floorTarget.LerpSpeed));
            ApplyFloorPhase(t, phase);
            yield return null;
        }

        float hold = Time.time + FloorSquishHoldTime;
        while (Time.time < hold) yield return null;

        while (phase > 0f)
        {
            if (t == null) yield break;
            phase = Mathf.MoveTowards(phase, 0f, Time.deltaTime * Mathf.Max(0.1f, _floorTarget.LerpSpeed));
            ApplyFloorPhase(t, phase);
            yield return null;
        }

        if (t != null)
        {
            t.localPosition = _floorBasePos;
            t.localEulerAngles = _floorBaseEuler;
            t.localScale = _floorBaseScale;
        }
        _floorAnim = null;
    }

    private void ApplyFloorPhase(Transform t, float phase)
    {
        t.localPosition = Vector3.Lerp(_floorBasePos,
            new Vector3(_floorTarget.PosX, _floorTarget.PosY, _floorTarget.PosZ), phase);
        t.localRotation = Quaternion.Slerp(
            Quaternion.Euler(_floorBaseEuler),
            Quaternion.Euler(_floorTarget.RotX, _floorTarget.RotY, _floorTarget.RotZ), phase);
        t.localScale = Vector3.Lerp(_floorBaseScale,
            new Vector3(_floorTarget.ScaleX, _floorTarget.ScaleY, _floorTarget.ScaleZ), phase);
    }

    /// Exits death-head mode: hides the death-head model and restores the normal avatar body.
    internal void ExitDeathHeadMode()
    {
        if (!_deathHeadActive) return;
        StopDeathHeadFloorAnimation();
        if (_spawnAnim != null) { StopCoroutine(_spawnAnim); _spawnAnim = null; }
        _deathHead.Show(false);
        SetNormalCrownVisible(true);
        SetAvatarBodyVisible(true);
        _deathHeadActive = false;
    }

    private void SetAvatarBodyVisible(bool visible)
    {
        if (_visuals?.meshParent != null && _visuals.meshParent.activeSelf != visible)
            _visuals.meshParent.SetActive(visible);
    }

    // The floating crown isn't under meshParent — hide it explicitly and disable PlayerCrown so it can't re-activate its mesh every frame.
    private void SetNormalCrownVisible(bool visible)
    {
        var crown = PreviewPc?.playerCrown;
        if (crown == null) return;
        if (!visible)
        {
            _normalCrownComponentWasEnabled = crown.enabled;
            crown.enabled = false;
            if (crown.crownMesh != null)
            {
                _normalCrownWasActive = crown.crownMesh.gameObject.activeSelf;
                crown.crownMesh.gameObject.SetActive(false);
            }
        }
        else
        {
            if (crown.crownMesh != null)
                crown.crownMesh.gameObject.SetActive(_normalCrownWasActive);
            crown.enabled = _normalCrownComponentWasEnabled;
        }
    }

    // Multi-equip extras live in cosmeticEquipped, so the single equipped loop already covers them.
    private List<(GameObject go, CosmeticAsset asset)> GetDeathHeadCosmeticGos()
    {
        var list = new List<(GameObject, CosmeticAsset)>();
        if (PreviewPc == null) return list;

        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(PreviewPc);
        if (equipped != null)
        {
            foreach (var c in equipped)
            {
                if (c == null) continue;
                var asset = MoreHeadCosmeticMountPatch.GetCosmeticAsset(c);
                if (IsSupported(asset)) list.Add((c.gameObject, asset!));
            }
        }

        return list;
    }

    private static bool IsSupported(CosmeticAsset? asset)
        => asset != null && DeathHeadPrefabProvider.SupportedTypes.Contains(asset.type);

    // Scale pop-in for the death-head model, using the game's pop-out curve when available.
    private IEnumerator SpawnAnimation()
    {
        var curve = AssetManager.instance != null ? AssetManager.instance.animationCurvePopOut : null;
        const float dur = 0.35f;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float e = curve != null ? curve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
            _deathHead.SetScaleFactor(e);
            yield return null;
        }
        _deathHead.SetScaleFactor(1f);
        _spawnAnim = null;
    }

    // "Preview" label above the avatar; its localScale cancels the parent's per-axis mirroring.
    private static void AddPreviewLabel(REPOAvatarPreview avatarPreview, TextMeshProUGUI src)
    {
        var go = new GameObject("Preview Label", typeof(RectTransform));
        go.transform.SetParent(avatarPreview.transform, false);

        // The template bakes localEulerAngles (0,180,0), mirroring children — apply the inverse so the label isn't flipped.
        go.transform.localRotation = Quaternion.Inverse(avatarPreview.transform.localRotation);

        // Pivot is (1,0): (0,0) = bottom-right. Centre X = −PreviewWidth/2; label Y = PreviewHeight + gap.
        var rt = (RectTransform)go.transform;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.sizeDelta = new Vector2(PreviewWidth, PreviewLabelHeight);
        rt.localPosition = new Vector3(-(PreviewWidth * 0.5f), PreviewHeight + PreviewLabelGapY, 0f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = src.font;
        tmp.fontSize = src.fontSize;
        tmp.fontStyle = src.fontStyle;
        tmp.color = src.color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = "Preview";
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        _deathHead.Destroy();

        PlayerAvatarMenu.instance = _cosmeticsMenuAvatar;
        if (_cosmeticsMenuAvatar != null)
        {
            _cosmeticsMenuAvatar.iconMakerAvatar = _cosmeticsMenuAvatarWasIconMaker;

            // Forced preview setups can tear down the menu avatar's world cosmetics (Mini-Semibot included) without re-spawning — without this the mini only returns on next hover. Same restore the variant popup uses on close.
            var meta = MetaManager.instance;
            if (meta != null)
            {
                meta.CosmeticPreviewSet(_state: false);
                meta.CosmeticPlayerUpdateLocal(_synced: false);
            }
        }
    }

    // ── Refresh methods ───────────────────────────────────────────────────────

    /// Full re-instantiation with <paramref name="pendingData"/>.
    /// Call when sub-category, type, Fix*, or EquipAnim changes.
    internal void RefreshFull(CosmeticOverrideData? pendingData)
    {
        if (PreviewPc == null || _asset == null) return;
        var meta = MetaManager.instance;
        if (meta == null) return;

        // Temporarily apply the pending type to the shared CosmeticAsset so TryMount in the InstantiateCosmetic postfix uses the correct bone anchor.
        var savedType = _asset.type;
        if (pendingData?.Type != null)
        {
            var (ct, _) = CustomizerStore.MapOverrideToVanilla(pendingData.Type.Value);
            _asset.type = ct;
        }

        try
        {
            OverridePreviewContext.Set(PreviewPc, _asset.assetId, pendingData);
            PreviewPc.SetupCosmetics(_synced: false, _forced: true, meta.cosmeticEquipped);
            PreviewPc.SetupColors(_synced: false);
        }
        finally
        {
            _asset.type = savedType;
            OverridePreviewContext.Clear();
        }
    }

    /// In-place crown update — no re-instantiation needed.
    internal void RefreshCrown(CosmeticCrownConfig? crown)
    {
        foreach (var go in FindAllPreviewCosmeticGos())
            MoreHeadCosmeticMountPatch.ApplyCrownConfig(go, crown);

        if (PreviewPc?.playerCrown != null)
        {
            // Re-evaluate the floating PlayerCrown's target so the pos/rot/scale change is immediately visible.
            PreviewPc.playerCrown.UpdateTarget();

            // Force crown mesh visibility: the preview avatar has no real player-session link, so FetchLogic never activates it on its own.
            if (PreviewPc.playerCrown.crownMesh != null)
                PreviewPc.playerCrown.crownMesh.gameObject.SetActive(crown != null);
        }
    }

    /// In-place offset update — tears down and re-injects CosmeticOffsetCondition.
    internal void RefreshOffsets(List<CosmeticOffsetEntry> offsets)
    {
        if (PreviewPc == null || _asset == null) return;
        bool updated = false;
        foreach (var go in FindAllPreviewCosmeticGos())
        {
            MoreHeadCosmeticMountPatch.ResetAndDestroyAll(
                go.transform, go.GetComponents<CosmeticOffsetCondition>());

            MoreHeadCosmeticMountPatch.InjectOffsetConditions(
                go, _asset, PreviewPc,
                offsets.Count > 0 ? offsets : null,
                customTypes: null);
            updated = true;
        }
        if (updated) MoreHeadCosmeticMountPatch.InvokeConditionsSetup(PreviewPc);
    }

    /// In-place custom-type update — tears down and re-injects the broadcaster AND the offset
    /// conditions: InjectOffsetConditions re-seeds fit offsets, so stale CosmeticOffsetConditions
    /// must go first or they stack (each duplicate captures an already-offset baseline and the
    /// transform ratchets per toggle). <paramref name="offsets"/> = pending user offsets to keep.
    internal void RefreshCustomTypes(IEnumerable<CosmeticCustomCondition.Type> types,
                                     List<CosmeticOffsetEntry>? offsets)
    {
        if (PreviewPc == null || _asset == null) return;
        var list = new List<CosmeticCustomCondition.Type>(types);
        bool updated = false;
        foreach (var go in FindAllPreviewCosmeticGos())
        {
            // DestroyImmediate: ConditionsSetup runs right below — a deferred-doomed broadcaster would double-announce.
            foreach (var old in go.GetComponents<BridgeCustomTypesBroadcaster>())
                DestroyImmediate(old);
            MoreHeadCosmeticMountPatch.ResetAndDestroyAll(
                go.transform, go.GetComponents<CosmeticOffsetCondition>());

            MoreHeadCosmeticMountPatch.InjectOffsetConditions(
                go, _asset, PreviewPc,
                offsets is { Count: > 0 } ? offsets : null,
                customTypes: list.Count > 0 ? list : null,
                // Suppress native so unchecking previews as OFF.
                suppressNativeCustomTypes: NativeCustomTypeImport.HasNativeAnnounceList(_asset));
            updated = true;
        }
        if (updated) MoreHeadCosmeticMountPatch.InvokeConditionsSetup(PreviewPc);
    }

    /// In-place sway update — tears down and re-adds BridgeSwaySpring.
    /// Mirrors the logic in RefreshLiveSway: explicit override (even None) suppresses
    /// native CosmeticSprings; Default restores them.
    internal void RefreshSway(SwayMode? sway)
    {
        if (PreviewPc == null) return;
        bool hasExplicitSway = sway.HasValue;
        bool hasBridgeSway = sway is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong;
        float factor = CosmeticSwayHelper.SwayModeToFactor(sway);

        foreach (var go in FindAllPreviewCosmeticGos())
        {
            var nativeSprings = go.GetComponentsInChildren<CosmeticSprings>(true);

            // Sync native-spring state: suppress when override is active, restore on Default.
            foreach (var cs in nativeSprings)
                cs.enabled = !hasExplicitSway;

            // DestroyImmediate: OnDestroy restores the base rotations BEFORE the new spring captures them.
            foreach (var old in go.GetComponents<BridgeSwaySpring>())
                DestroyImmediate(old);

            // Add BridgeSwaySpring only for a bridge cosmetic (no native springs), OR when an explicit sway override is set (native springs suppressed above).
            bool canHaveBridgeSpring = hasExplicitSway || nativeSprings.Length == 0;
            if (!hasBridgeSway || !canHaveBridgeSpring) continue;

            var cosmetic = go.GetComponent<Cosmetic>();
            if (cosmetic != null)
            {
                var spring = go.AddComponent<BridgeSwaySpring>();
                spring.Init(cosmetic, factor);
            }
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    internal GameObject? FindPreviewCosmeticGo()
    {
        if (PreviewPc == null || _asset == null) return null;
        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(PreviewPc);
        if (equipped == null) return null;
        foreach (var c in equipped)
        {
            if (c == null) continue;
            if (MoreHeadCosmeticMountPatch.GetCosmeticAsset(c) == _asset)
                return c.gameObject;
        }
        return null;
    }

    /// Returns ALL live preview GOs for _asset (multi-equip aware).
    /// Multi-equip extras live in cosmeticEquipped, so the single equipped loop covers them.
    private List<GameObject> FindAllPreviewCosmeticGos()
    {
        var result = new List<GameObject>();
        if (PreviewPc == null || _asset == null) return result;

        var equipped = MoreHeadCosmeticMountPatch.GetEquippedCosmetics(PreviewPc);
        if (equipped != null)
        {
            foreach (var c in equipped)
            {
                if (c == null) continue;
                if (MoreHeadCosmeticMountPatch.GetCosmeticAsset(c) == _asset)
                    result.Add(c.gameObject);
            }
        }

        return result;
    }
}
