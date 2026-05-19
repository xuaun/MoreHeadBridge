// ============================================================================
// [MenuCapture] One-shot batch icon generator.
//
// When the player opens the cosmetics menu AND the [Icons] GenerateAllIcons
// flag is true, this coroutine cycles through every bridge cosmetic that has
// no cached icon yet. For each one it:
//   1. Sets cosmeticEquippedPreview to contain only the target asset index
//   2. Enables preview mode (CosmeticPreviewSet) → avatar shows the asset alone
//   3. Force-completes the equip animation (iconCreationAvatar → equipLerp = 1f)
//   4. Waits one frame + WaitForEndOfFrame for the mesh to render at full scale
//   5. Captures the menu's RT into a PNG via IconCapture.TryCapture
//   6. Disables preview mode → avatar reverts to the real equipped loadout
//
// cosmeticEquipped is NEVER modified — all work goes through the preview list.
// If the coroutine is interrupted (user closes the menu), MenuPageCosmetics.OnDestroy
// calls NotifyMenuClosed(), which resets _isRunning and logs the warning.
// Vanilla's own OnDestroy → CosmeticPreviewSet(false) restores the avatar.
//
// GenerateAllIcons stays true on interruption so reopening the menu resumes
// the batch (HasCache skips already-generated icons).
//
// To disable / remove: set GenerateAllIcons=false (default).
// ============================================================================

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class BatchIconGenerator
{
    // Prevents concurrent runs (menu opened/closed rapidly while batch is active).
    private static bool _isRunning;

    // True once a batch has started at least once this session — used to detect
    // re-opens after an interrupted run and emit the "resuming" warning.
    private static bool _didStartOnce;

    private static int _progressDone;
    private static int _progressFailed;
    private static int _progressTotal;

    internal static void TryStart(MonoBehaviour host)
    {
        if (_isRunning) return;
        if (!Plugin.GenerateAllIcons.Value) return;

        if (_didStartOnce)
            Plugin.Logger.LogWarning(
                "GenerateAllIcons: previous batch was interrupted. " +
                "Resuming — only icons still missing will be generated.");

        _isRunning = true;
        host.StartCoroutine(Run());
    }

    // Called by BatchIconGeneratorMenuClosePatch when MenuPageCosmetics is destroyed.
    internal static void NotifyMenuClosed()
    {
        if (!_isRunning) return;
        _isRunning = false;

        int remaining = _progressTotal - _progressDone - _progressFailed;
        Plugin.Logger.LogWarning(
            $"GenerateAllIcons: batch interrupted at " +
            $"{_progressDone + _progressFailed}/{_progressTotal} " +
            $"({remaining} still to go). " +
            "Reopen the menu to continue. " +
            "(Your equipped cosmetics were not modified.)");
    }

    private static IEnumerator Run()
    {
        _didStartOnce = true;

        yield return new WaitForSecondsRealtime(0.5f);

        if (MetaManager.instance == null)
        {
            _isRunning = false;
            yield break;
        }

        var work = new List<CosmeticAsset>();
        foreach (var asset in MetaManager.instance.cosmeticAssets)
        {
            if (asset == null || asset.assetId == null) continue;
            if (!BridgeIds.IsBridgeAsset(asset)) continue;
            if (IconCapture.HasCache(asset)) continue;
            work.Add(asset);
        }

        Plugin.Logger.LogInfo($"GenerateAllIcons: {work.Count} icon(s) to generate.");
        if (work.Count == 0)
        {
            _isRunning = false;
            Plugin.GenerateAllIcons.Value = false;
            Plugin.Instance.Config.Save();
            yield break;
        }

        _progressDone   = 0;
        _progressFailed = 0;
        _progressTotal  = work.Count;

        bool interrupted = true; // flipped to false only when the loop exits normally
        const int LogEvery = 50;

        // try/finally is a best-effort safety net for scenarios where Unity does
        // call Dispose on the stopped coroutine. The reliable path is NotifyMenuClosed.
        try
        {
            foreach (var asset in work)
            {
                if (MetaManager.instance == null) break;

                int idx = MetaManager.instance.cosmeticAssets.IndexOf(asset);
                if (idx < 0) { _progressFailed++; continue; }

                // Point the preview list at this single asset so the avatar renders
                // it in isolation. cosmeticEquipped is never touched.
                MetaManager.instance.cosmeticEquippedPreview.Clear();
                MetaManager.instance.cosmeticEquippedPreview.Add(idx);
                if (MetaManager.instance.colorsEquipped != null)
                    MetaManager.instance.colorsEquippedPreview =
                        (int[])MetaManager.instance.colorsEquipped.Clone();
                MetaManager.instance.CosmeticPreviewSet(_state: true);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

                WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(false);
                if (HhhCosmeticLoader.IsWorldAsset(asset))
                    WorldCosmeticsSetupPatch.SetWorldAssetActive(asset, true);

                SkipEquipAnimationFor(asset);

                yield return null;                    // Update() → EquipAnimation snaps to 1f

                for (int guard = 0; guard < 3 && !IsAnimComplete(asset); guard++)
                    yield return null;

                yield return new WaitForEndOfFrame(); // frame renders at full scale

                if (MetaManager.instance == null) break;

                if (IconCapture.TryCapture(asset)) _progressDone++;
                else _progressFailed++;

                // Disable preview so the avatar reverts before the next iteration.
                MetaManager.instance.CosmeticPreviewSet(_state: false);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

                WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(true);

                int total = _progressDone + _progressFailed;
                if (total % LogEvery == 0)
                    Plugin.Logger.LogInfo(
                        $"Batch progress: {total}/{work.Count} " +
                        $"({_progressDone} ok, {_progressFailed} failed)");
            }

            interrupted = false;
        }
        finally
        {
            // Only act if NotifyMenuClosed hasn't already reset _isRunning.
            if (_isRunning)
            {
                _isRunning = false;
                if (interrupted)
                {
                    int remaining = _progressTotal - _progressDone - _progressFailed;
                    Plugin.Logger.LogWarning(
                        $"GenerateAllIcons: batch interrupted at " +
                        $"{_progressDone + _progressFailed}/{_progressTotal} " +
                        $"({remaining} still to go). " +
                        "Reopen the menu to continue.");
                }
            }

            if (MetaManager.instance != null)
            {
                MetaManager.instance.CosmeticPreviewSet(_state: false);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
            }

            WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(true);
        }

        // Only reached on normal completion (interrupted == false).
        Plugin.GenerateAllIcons.Value = false;
        Plugin.Instance.Config.Save();

        Plugin.Logger.LogInfo(
            $"GenerateAllIcons done — {_progressDone} captured, " +
            $"{_progressFailed} failed. Flag reset to false.");
    }

    // Sets iconCreationAvatar = true on every Cosmetic component whose cosmeticAsset
    // matches the target. On the next Update() EquipAnimation() will snap equipLerp to 1f
    // and apply the final mesh scale before the frame is rendered.
    private static FieldInfo? _iconCreationAvatarField;
    private static void SkipEquipAnimationFor(CosmeticAsset asset)
    {
        _iconCreationAvatarField ??= AccessTools.Field(typeof(Cosmetic), "iconCreationAvatar");
        if (_iconCreationAvatarField == null) return;

        foreach (var cosmetic in UnityEngine.Object.FindObjectsOfType<Cosmetic>())
        {
            if (cosmetic != null && cosmetic.cosmeticAsset == asset)
                _iconCreationAvatarField.SetValue(cosmetic, true);
        }
    }

    private static FieldInfo? _equipLerpField;
    private static bool IsAnimComplete(CosmeticAsset asset)
    {
        _equipLerpField ??= AccessTools.Field(typeof(Cosmetic), "equipLerp");
        if (_equipLerpField == null) return true;

        foreach (var c in UnityEngine.Object.FindObjectsOfType<Cosmetic>())
        {
            if (c != null && c.cosmeticAsset == asset &&
                (float)(_equipLerpField.GetValue(c) ?? 1f) < 1f)
                return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class BatchIconGeneratorStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        BatchIconGenerator.TryStart(__instance);
    }
}

// Hooks MenuPageCosmetics.OnDestroy to notify BatchIconGenerator when the menu
// closes mid-batch, and clears hover-capture state so the next open starts clean.
[HarmonyPatch(typeof(MenuPageCosmetics), "OnDestroy")]
internal static class BatchIconGeneratorMenuClosePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        BatchIconGenerator.NotifyMenuClosed();
        CosmeticHoverPatch.OnMenuClosed();
        CosmeticsMenuState.OnMenuClosed();
        CosmeticsMenuLateUpdatePatch.OnMenuClosed(); // E3: reset idle-hint timer
    }
}
