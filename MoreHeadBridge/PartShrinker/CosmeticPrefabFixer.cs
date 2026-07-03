// Prefab-level physics/animation fixes + MoreHead post-processing. Batch usage: BeginBatch(); Fix() per file (logs Debug); EndBatch() emits one Info summary. Standalone Fix() logs Info.

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticPrefabFixer
{
    // Resolved once on first use; null means MoreHeadUtilities is not loaded.
    private static Type? _partShrinkerType;
    private static bool _partShrinkerResolved;

    // ── Batch state ────────────────────────────────────────────────────────────
    private static bool _inBatch;
    private static int _batchFixed;
    private static int _batchCols;
    private static int _batchRbs;
    private static int _batchAnimLoops;
    private static int _batchAnimrLoops;
    private static int _batchMissingScriptPrefabs;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// Start accumulating fix stats; Fix() logs at Debug until EndBatch().
    internal static void BeginBatch()
    {
        _inBatch = true;
        _batchFixed = _batchCols = _batchRbs
            = _batchAnimLoops = _batchAnimrLoops
            = _batchMissingScriptPrefabs = 0;
    }

    /// Emit one Info summary of all fixes since BeginBatch(), then clear batch state.
    internal static void EndBatch()
    {
        _inBatch = false;

        if (_batchFixed > 0)
            BceConsole.LogInfo(
                $"CosmeticPrefabFixer: fixed {_batchFixed} prefab(s) — " +
                $"removed {_batchCols} collider(s), {_batchRbs} rigidbody(s); " +
                $"looped {_batchAnimLoops} Animation clip(s), {_batchAnimrLoops} Animator(s).",
                ConsoleColor.Gray);

        if (_batchMissingScriptPrefabs > 0)
            BceConsole.LogWarning(
                $"CosmeticPrefabFixer: {_batchMissingScriptPrefabs} prefab(s) have missing " +
                $"MonoBehaviour script(s) — install MoreHeadUtilities if body-part hiding is needed.");
    }

    /// Fix physics/animation on the prefab. Per-cosmetic overrides (assetId) beat global config; null assetId = non-bridge prefab. Debug log in a batch, Info otherwise.
    internal static void Fix(GameObject prefab, string label = "", string? assetId = null)
        => FixCore(prefab, label, assetId, verbose: !_inBatch);

    /// Fix physics/animation on a live INSTANCE (deferred Destroy; never touches shared assets). Runs at equip time so overrides apply on re-equip.
    /// remoteFixAnimation: the remote wearer's broadcasted FixAnimation (only read when isRemote).
    /// isRemote: the instance belongs to another player — the viewer's per-cosmetic FixAnimation override must not apply to it.
    internal static void FixInstance(GameObject instance, string? assetId, bool? remoteFixAnimation = null, bool isRemote = false)
    {
        bool fixCollider, fixAnimation;
        if (OverridePreviewContext.IsPreviewActiveForAsset(assetId) && OverridePreviewContext.Data is { } previewData)
        {
            // Live-preview path: use pending values so the preview reflects unsaved settings.
            var (savedFc, savedFa) = CustomizerStore.GetEffectiveFixes(assetId);
            fixCollider  = previewData.FixCollider  ?? savedFc;
            fixAnimation = previewData.FixAnimation ?? savedFa;
        }
        else if (isRemote)
        {
            // Remote-player path: their broadcasted FixAnimation, or the global default when they sent none — never the viewer's per-cosmetic override. fixCollider stays the local preference (physics removal doesn't affect appearance).
            var (savedFc, _) = CustomizerStore.GetEffectiveFixes(assetId);
            fixCollider  = savedFc;
            fixAnimation = remoteFixAnimation ?? Plugin.LoopBridgeAnimation.Value;
        }
        else
        {
            (fixCollider, fixAnimation) = CustomizerStore.GetEffectiveFixes(assetId);
        }

        int cols = 0, rbs = 0, animLoops = 0, animrLoops = 0;

        if (fixCollider)
        {
            foreach (var col in instance.GetComponentsInChildren<Collider>(includeInactive: true))
            { cols++; UnityEngine.Object.Destroy(col); }
            foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            { rbs++; UnityEngine.Object.Destroy(rb); }
        }

        if (fixAnimation)
        {
            var animations = instance.GetComponentsInChildren<Animation>(includeInactive: true);
            var animators = instance.GetComponentsInChildren<Animator>(includeInactive: true);

            foreach (var anim in animations)
            {
                if (anim.wrapMode != WrapMode.Loop) { anim.wrapMode = WrapMode.Loop; animLoops++; }

                // Also fix any already-playing AnimationStates — wrapMode on the component only affects NEW plays; active states keep their original wrap mode.
                foreach (AnimationState state in anim)
                {
                    if (state == null) continue;
                    if (state.wrapMode != WrapMode.Loop)
                        state.wrapMode = WrapMode.Loop;
                }
            }

            foreach (var animator in animators)
            {
                if (animator.runtimeAnimatorController == null) continue;
                bool needsLooper = false;
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && !clip.isLooping) { needsLooper = true; break; }
                }
                if (needsLooper && animator.GetComponent<AnimatorLooper>() == null)
                {
                    animator.gameObject.AddComponent<AnimatorLooper>();
                    animrLoops++;
                }
            }
        }
        else
        {
            // fixAnimation explicitly false: remove any load-time AnimatorLooper so a per-cosmetic "Loop = No" takes effect on re-equip. Animation.wrapMode is NOT reset — FixCore mutates the SHARED clip asset (can't undo per-instance); AnimatorLooper is the component-level mechanism we control.
            foreach (var looper in instance.GetComponentsInChildren<AnimatorLooper>(includeInactive: true))
                UnityEngine.Object.Destroy(looper);
        }

        if (cols > 0 || rbs > 0 || animLoops > 0 || animrLoops > 0)
            BridgeLog.Trace(
                $"FixInstance '{instance.name}': removed {cols} Collider(s), {rbs} Rigidbody(s); " +
                $"looped {animLoops} Animation(s), {animrLoops} Animator(s).");
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    /// Fix all MoreHead-registered prefabs (no-op without MoreHead). Yields every batchSize prefabs to spread the pass across frames; one Info summary at the end.
    internal static IEnumerator TryFixMoreHeadPrefabsAsync(int batchSize = 25)
    {
        System.Collections.IList? list = null;
        PropertyInfo? prefabProp = null;

        try
        {
            var mhType = Type.GetType("MoreHead.HeadDecorationManager, MoreHead");
            if (mhType == null) yield break; // MoreHead not installed

            var decorationsProp = mhType.GetProperty(
                "Decorations", BindingFlags.Public | BindingFlags.Static);
            if (decorationsProp == null) yield break;

            list = decorationsProp.GetValue(null) as System.Collections.IList;
            if (list == null || list.Count == 0) yield break;

            prefabProp = list[0]!.GetType().GetProperty("Prefab");
            if (prefabProp == null) yield break;
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"MoreHead prefab fix pass skipped: {ex.Message}");
            yield break;
        }

        int count = 0;
        int processedThisFrame = 0;
        BeginBatch();

        try
        {
            foreach (var item in list)
            {
                try
                {
                    if (prefabProp.GetValue(item) is GameObject prefab)
                    {
                        Fix(prefab);
                        count++;
                        processedThisFrame++;
                    }
                }
                catch (Exception ex)
                {
                    BridgeLog.Trace($"MoreHead prefab fix item skipped: {ex.Message}");
                }

                if (processedThisFrame >= batchSize)
                {
                    processedThisFrame = 0;
                    yield return null;
                }
            }
        }
        finally
        {
            EndBatch();
            BceConsole.LogInfo($"CosmeticPrefabFixer: scanned {count} MoreHead prefab(s).", ConsoleColor.Gray);
        }
    }

    private static Type? GetPartShrinkerType()
    {
        if (!_partShrinkerResolved)
        {
            _partShrinkerResolved = true;
            _partShrinkerType = MoreHeadUtilitiesTypes.FindType("MoreHeadUtilities.PartShrinker");
        }
        return _partShrinkerType;
    }

    private static void FixCore(GameObject prefab, string label, string? assetId, bool verbose)
    {
        var (fixCollider, fixAnimation) = CustomizerStore.GetEffectiveFixes(assetId);

        int cols = 0, rbs = 0, animLoops = 0, animrLoops = 0;

        if (fixCollider)
        {
            foreach (var col in prefab.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                cols++;
                try { UnityEngine.Object.DestroyImmediate(col); }
                catch { try { col.enabled = false; } catch { /* best effort */ } }
            }

            foreach (var rb in prefab.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rbs++;
                try { UnityEngine.Object.DestroyImmediate(rb); }
                catch { try { rb.isKinematic = true; rb.useGravity = false; } catch { /* best effort */ } }
            }
        }

        if (fixAnimation)
        {
            foreach (var anim in prefab.GetComponentsInChildren<Animation>(includeInactive: true))
            {
                if (anim.clip != null && anim.clip.wrapMode != WrapMode.Loop)
                {
                    anim.clip.wrapMode = WrapMode.Loop;
                    animLoops++;
                }
                anim.wrapMode = WrapMode.Loop;
            }

            foreach (var animator in prefab.GetComponentsInChildren<Animator>(includeInactive: true))
            {
                if (animator.runtimeAnimatorController == null) continue;

                bool needsLooper = false;
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && !clip.isLooping) { needsLooper = true; break; }
                }

                if (needsLooper && animator.GetComponent<AnimatorLooper>() == null)
                {
                    animator.gameObject.AddComponent<AnimatorLooper>();
                    animrLoops++;
                }
            }
        }

        string displayLabel = string.IsNullOrEmpty(label) ? prefab.name : label;

        bool anyFix = cols > 0 || rbs > 0 || animLoops > 0 || animrLoops > 0;
        if (anyFix)
        {
            string msg = $"'{displayLabel}': removed {cols} Collider(s), {rbs} Rigidbody(s); " +
                         $"looped {animLoops} Animation clip(s), {animrLoops} Animator(s).";
            if (verbose) BceConsole.LogInfo(msg, ConsoleColor.Gray);
            else BridgeLog.Trace(msg);

            if (_inBatch)
            {
                _batchFixed++;
                _batchCols += cols;
                _batchRbs += rbs;
                _batchAnimLoops += animLoops;
                _batchAnimrLoops += animrLoops;
            }
        }

        // ── PartShrinker detection ─────────────────────────────────────────────
        var shrinkerType = GetPartShrinkerType();
        if (shrinkerType != null)
        {
            var shrinkers = prefab.GetComponentsInChildren(shrinkerType, includeInactive: true);
            if (shrinkers.Length > 0)
            {
                string msg = $"'{displayLabel}': {shrinkers.Length} PartShrinker component(s) — body-part hiding active.";
                if (verbose) BceConsole.LogInfo(msg, ConsoleColor.Gray);
                else BridgeLog.Trace(msg);
            }
        }
        else
        {
            // MoreHeadUtilities not loaded — count missing MonoBehaviour slots (serialized references to unresolved types show as null MonoBehaviour).
            int missing = 0;
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
                if (mb == null) missing++;

            if (missing > 0)
            {
                if (!_inBatch)
                {
                    BceConsole.LogWarning(
                        $"'{displayLabel}': {missing} missing script(s) — " +
                        $"install MoreHeadUtilities if body-part hiding is needed.");
                }
                else
                {
                    BridgeLog.Trace($"'{displayLabel}': {missing} missing script(s)");
                    _batchMissingScriptPrefabs++;
                }
            }
        }
    }
}
