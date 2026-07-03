// Reactive icon capture: MetaManager.CosmeticEquip fires once with _isPreview:true on hover — poll until equipLerp >= 1, then snapshot the menu RT as the icon PNG. Aborts cleanly if the hover ends early (retries next hover). Gated by AutoCaptureIcons.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MetaManager), "CosmeticEquip")]
internal static class CosmeticHoverPatch
{
    private static readonly HashSet<string> _scheduled = new();

    private static FieldInfo? _equipLerpField;
    private static bool _equipLerpWarned;

    private enum AnimState { StillRunning, Done, Gone }

    // cached = the live Cosmetic found on a previous poll; reused while alive and still ours to avoid a
    // per-frame FindObjectsOfType scan. A destroyed object Unity-compares == null, so the re-scan kicks in then.
    private static AnimState CheckEquipAnim(CosmeticAsset asset, ref Cosmetic? cached)
    {
        if (_equipLerpField == null && !_equipLerpWarned)
        {
            _equipLerpField = AccessTools.Field(typeof(Cosmetic), "equipLerp");
            if (_equipLerpField == null)
            {
                _equipLerpWarned = true;
                BceConsole.LogWarning("CosmeticHoverPatch: Cosmetic.equipLerp not found — will capture without waiting for animation");
            }
        }

        // If we can't read the field, skip the wait entirely.
        if (_equipLerpField == null) return AnimState.Done;

        // Re-acquire only when the cached Cosmetic is gone (destroyed → Unity null) or no longer ours.
        if (cached == null || cached.cosmeticAsset != asset)
        {
            cached = null;
            foreach (var cosmetic in UnityEngine.Object.FindObjectsOfType<Cosmetic>())
            {
                if (cosmetic != null && cosmetic.cosmeticAsset == asset) { cached = cosmetic; break; }
            }
        }

        // Asset not found — cosmetic was unpreview'd (user moved away).
        if (cached == null) return AnimState.Gone;

        return (float)(_equipLerpField.GetValue(cached) ?? 1f) >= 1f
            ? AnimState.Done
            : AnimState.StillRunning;
    }

    internal static void OnMenuClosed() { _scheduled.Clear(); _suppressedHoverId = null; }

    internal static void Invalidate(CosmeticAsset asset) => _scheduled.Remove(asset.assetId);

    // Set when the user deletes a single icon while the cursor still rests on its button — otherwise the hover auto-capture re-shoots the PNG next frame and the deletion looks like a no-op. Cleared once the hover moves off the asset.
    private static string? _suppressedHoverId;

    internal static void SuppressWhileHovered(CosmeticAsset asset)
    {
        if (asset != null) _suppressedHoverId = asset.assetId;
    }

    // The assetId currently under the cursor (or null), updated every frame by HoverTick — lets the capture coroutine tell "this cosmetic never spawns a Cosmetic" apart from "the user moved away".
    private static string? _currentHoverId;

    // Called every frame by CosmeticsMenuLateUpdatePatch with the currently hovered asset (or null).
    internal static void HoverTick(CosmeticAsset? hovered)
    {
        _currentHoverId = hovered?.assetId;
        if (_suppressedHoverId != null && hovered?.assetId != _suppressedHoverId)
            _suppressedHoverId = null;
    }

    // For EQUIPPED bridge cosmetics without an icon: vanilla skips CosmeticEquip(_isPreview:true) for already-equipped items, so the Postfix never fires — this path fills the gap (lerp is already 1, snapshot is immediate).
    internal static void TryScheduleCapture(CosmeticAsset asset, MonoBehaviour host)
    {
        if (!Plugin.AutoCaptureIcons.Value) return;
        if (asset == null) return;
        if (!BridgeIds.IsBridgeAsset(asset)) return;
        if (IconCapture.HasCache(asset)) return;
        if (asset.assetId == _suppressedHoverId) return;  // just deleted under the cursor
        if (!_scheduled.Add(asset.assetId)) return;  // already pending

        host.StartCoroutine(CaptureAfterAnimation(asset));
    }

    // Vanilla calls CosmeticEquip(asset, _isPreview: true) exactly once on hover-start — no per-frame overhead.
    [HarmonyPostfix]
    private static void Postfix(MetaManager __instance, CosmeticAsset _cosmeticAssetNew, bool _isPreview)
    {
        if (!_isPreview) return;                     // not a preview equip — ignore
        if (!Plugin.AutoCaptureIcons.Value) return;

        var asset = _cosmeticAssetNew;
        if (asset == null) return;
        if (!BridgeIds.IsBridgeAsset(asset)) return;
        if (IconCapture.HasCache(asset)) return;
        if (asset.assetId == _suppressedHoverId) return;  // just deleted under the cursor
        if (!_scheduled.Add(asset.assetId)) return;  // coroutine already running for this asset

        __instance.StartCoroutine(CaptureAfterAnimation(asset));
    }

    private static IEnumerator CaptureAfterAnimation(CosmeticAsset asset)
    {
        // One frame so vanilla's hover-equip can spawn the Cosmetic GO.
        yield return null;

        // Poll until the animation completes or the user moves away. Timeout (3 s) guards a cosmetic whose equipLerp never reaches 1f.
        const float Timeout = 3f;
        // Min poll before concluding a still-hovered cosmetic has NO mesh: a real mesh cosmetic's Cosmetic GO appears within a frame or two of the equip (only equipLerp is slow), so this window only elapses for genuinely mesh-less ones.
        const float MeshlessGrace = 0.4f;
        float elapsed = 0f;
        bool everSeen = false;   // did a live Cosmetic for this asset ever appear?
        bool meshless = false;
        Cosmetic? animCosmetic = null;   // cached across polls by CheckEquipAnim

        while (elapsed < Timeout)
        {
            var state = CheckEquipAnim(asset, ref animCosmetic);

            if (state == AnimState.Done)
                break; // cosmetic is fully scaled — proceed to capture

            if (state == AnimState.StillRunning)
            {
                everSeen = true;
            }
            else // Gone — no live Cosmetic for this asset
            {
                // Cursor moved off the button → genuine abort; retry on the next hover (silent).
                if (asset.assetId != _currentHoverId)
                {
                    _scheduled.Remove(asset.assetId);
                    yield break;
                }
                // Still hovered, no Cosmetic within the grace window → this cosmetic spawns NO mesh of its own (a hide-pupil-style effect, or one that failed to mount). The look lives on the avatar, so capture the avatar crop like the batch generator instead of bailing.
                if (!everSeen && elapsed >= MeshlessGrace)
                {
                    meshless = true;
                    WarnMeshlessOnce(asset);
                    break;
                }
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (meshless)
        {
            // Give the avatar a couple of frames to apply the effect (hide conditions settle) before snapping, re-checking the cursor each time.
            yield return null;
            yield return null;
            if (asset.assetId != _currentHoverId) { _scheduled.Remove(asset.assetId); yield break; }
        }
        else
        {
            // Guard: user may have un-hovered during the final animation-poll frame (between "Done" detected and WaitForEndOfFrame executing).
            if (CheckEquipAnim(asset, ref animCosmetic) == AnimState.Gone)
            {
                _scheduled.Remove(asset.assetId);
                yield break;
            }
        }

        // Snapshot at the end of this frame (ReadPixels requires WaitForEndOfFrame).
        yield return new WaitForEndOfFrame();

        // Guard against the cosmetic disappearing during WaitForEndOfFrame: mesh cosmetics via the Cosmetic scan, mesh-less ones via the cursor.
        bool gone = meshless ? asset.assetId != _currentHoverId
                             : CheckEquipAnim(asset, ref animCosmetic) == AnimState.Gone;
        if (gone)
        {
            _scheduled.Remove(asset.assetId);
            yield break;
        }

        bool ok = false;
        try
        {
            ok = IconCapture.TryCapture(asset);
        }
        finally
        {
            // Failure: remove so the next hover retries. Success: keep in _scheduled — HasCache() gates the next hover anyway.
            if (!ok)
            {
                _scheduled.Remove(asset.assetId);
                WarnCaptureFailedOnce();
            }
        }
    }

    // One visible note per asset (this session) when a hovered cosmetic spawns no Cosmetic and we fall back to the avatar crop. Surfaces both legit mesh-less effects (hide-pupil) and cosmetics that silently failed to mount — for the latter the captured icon shows the bare avatar, which is the tell.
    private static readonly HashSet<string> _meshlessNoted = new();
    private static void WarnMeshlessOnce(CosmeticAsset asset)
    {
        if (!_meshlessNoted.Add(asset.assetId)) return;
        BceConsole.LogInfo(
            $"Hover icon: '{asset.name}' spawned no mesh of its own (hide-only effect or failed " +
            "to mount) — capturing from the avatar.");
    }

    // One visible BCE warning per game session when a hover icon-capture fails — makes a silent never-generates noticeable without spamming each re-hover. Detailed reason still goes to LogDebug (in IconCapture).
    private static bool _captureFailWarned;
    private static void WarnCaptureFailedOnce()
    {
        if (_captureFailWarned) return;
        _captureFailWarned = true;
        BceConsole.LogWarning("Could not generate an icon on hover for a Bridge cosmetic.");
    }
}
