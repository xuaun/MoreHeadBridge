// One-shot batch icon generator: cycles every bridge cosmetic without a cached PNG — preview-equip, snap the avatar RT, save PNG. cosmeticEquipped is never touched; menu close interrupts cleanly, reopening resumes.
// MenuLib is a SOFT dep: all popup UI lives in BatchIconPopup, reached only via the no-inline wrappers at the bottom behind Plugin.MenuLibAvailable — this class must stay MenuLib-free (a MenuLib-typed field here broke class load in the always-applied menu patches without MenuLib).

using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class BatchIconGenerator
{
    // Prevents concurrent runs (menu opened/closed rapidly while batch is active).
    private static bool _isRunning;

    // True once a batch has started this session — detects re-opens after an interrupted run to emit the "resuming" warning.
    private static bool _didStartOnce;

    private static int _progressDone;
    private static int _progressFailed;
    private static int _progressTotal;

    // Cached avatar preview RawImage, hidden during generation; cleared (and alpha restored) in Run()'s finally.
    private static RawImage? _avatarRawImage;

    // Exposed for CosmeticsMenuLateUpdatePatch so the status label/overlay show generation progress instead of the hovered cosmetic name.
    internal static bool   IsGenerating    => _isRunning;
    internal static string ProgressText    { get; private set; } = "";

    // Called once on successful (non-interrupted) completion; cleared right after invocation so it fires at most once per registration.
    internal static System.Action? OnBatchCompleted;
    internal static int    ProgressDone    => _progressDone;
    internal static int    ProgressFailed  => _progressFailed;
    internal static int    ProgressTotal   => _progressTotal;

    internal static void TryStart(MonoBehaviour host)
    {
        if (_isRunning) return;
        if (!Plugin.GenerateAllIcons.Value) return;

        if (_didStartOnce)
            BceConsole.LogWarning(
                "GenerateAllIcons: previous batch was interrupted. " +
                "Resuming — only icons still missing will be generated");

        _isRunning = true;
        var menuPage = host.GetComponent<MenuPage>();
        host.StartCoroutine(Run(menuPage));
    }

    // Called when MenuPageCosmetics is destroyed: Unity stops the coroutine, so its finally never runs — clean up preview state and the popup here.
    internal static void NotifyMenuClosed()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (Plugin.MenuLibAvailable) PopupDestroy();

        // Restore avatar and preview — finally won't run since the host is being destroyed.
        if (_avatarRawImage != null)
        {
            _avatarRawImage.enabled = true;
            _avatarRawImage = null;
        }
        if (MetaManager.instance != null)
        {
            MetaManager.instance.CosmeticPreviewSet(_state: false);
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
        }
        WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(true);
        ProgressText = "";

        int remaining = _progressTotal - _progressDone - _progressFailed;
        BceConsole.LogWarning(
            $"GenerateAllIcons: batch interrupted at " +
            $"{_progressDone + _progressFailed}/{_progressTotal} " +
            $"({remaining} still to go). " +
            "Reopen the menu to continue. " +
            "(Your equipped cosmetics were not modified.)");
    }

    private static IEnumerator Run(MenuPage? cosmeticsMenuPage)
    {
        _didStartOnce = true;

        // Wait for the page's opening slide to finish before showing the popup, else the two
        // overlap. currentPageState is internal → reflection; falls back to a fixed delay.
        _menuPageStateField ??= AccessTools.Field(typeof(MenuPage), "currentPageState");

        if (cosmeticsMenuPage != null && _menuPageStateField != null)
        {
            const float AnimTimeout = 3f;
            float elapsed = 0f;
            while (elapsed < AnimTimeout)
            {
                var state = (MenuPage.PageState)(_menuPageStateField.GetValue(cosmeticsMenuPage)
                            ?? MenuPage.PageState.Active);
                if (state == MenuPage.PageState.Active) break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(0.5f);
        }

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
            // Mini-Semibot has its own dedicated icon and isn't a mesh cosmetic — never batch-generate one for it.
            if (asset.assetId == MiniSemibotCosmetic.AssetId) continue;
            if (IconCapture.HasCache(asset)) continue;
            work.Add(asset);
        }

        BceConsole.LogInfo($"GenerateAllIcons: {work.Count} icon(s) to generate.", ConsoleColor.DarkGreen);
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

        // Hide the avatar preview so rapid cosmetic-cycling isn't visible (photosensitivity); restored unconditionally in the finally block below.
        _avatarRawImage = FindAvatarRawImage();
        if (_avatarRawImage != null && Plugin.HideAvatarWhileGenerating.Value)
            _avatarRawImage.enabled = false;

        // The popup sets the cosmetics page Inactive (blocks interaction; ESC → OnPopupEscape). Without MenuLib the batch still runs, just without the blocking popup — progress shows in the menu status label.
        if (Plugin.MenuLibAvailable) PopupOpen();

        bool interrupted = true; // flipped to false only when the loop exits normally
        const int LogEvery = 50;

        // Best-effort safety net if Unity disposes the stopped coroutine; NotifyMenuClosed is the reliable cleanup path.
        try
        {
            foreach (var asset in work)
            {
                if (!_isRunning) break;          // ESC pressed — exit so finally restores state
                if (MetaManager.instance == null) break;

                int idx = MetaManager.instance.cosmeticAssets.IndexOf(asset);
                if (idx < 0) { _progressFailed++; continue; }

                // Build the preview list (never touches cosmeticEquipped). HideClothesWhileGenerating: true → only the target (clean icons); false → full loadout + target.
                MetaManager.instance.cosmeticEquippedPreview.Clear();
                if (!Plugin.HideClothesWhileGenerating.Value &&
                    MetaManager.instance.cosmeticEquipped != null)
                {
                    foreach (int equippedIdx in MetaManager.instance.cosmeticEquipped)
                        MetaManager.instance.cosmeticEquippedPreview.Add(equippedIdx);
                }
                if (!MetaManager.instance.cosmeticEquippedPreview.Contains(idx))
                    MetaManager.instance.cosmeticEquippedPreview.Add(idx);
                if (MetaManager.instance.colorsEquipped != null)
                {
                    // true → all-zero array (neutral avatar); false → clone the player's colors.
                    MetaManager.instance.colorsEquippedPreview = Plugin.ResetBodyColorWhileGenerating.Value
                        ? new int[MetaManager.instance.colorsEquipped.Length]
                        : (int[])MetaManager.instance.colorsEquipped.Clone();
                }
                MetaManager.instance.CosmeticPreviewSet(_state: true);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

                WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(false);
                if (HhhCosmeticLoader.IsWorldAsset(asset))
                    WorldCosmeticsSetupPatch.SetWorldAssetActive(asset, true);

                // Scan the scene's Cosmetics once per iteration (FindObjectsOfType is expensive; sharing it across helpers cuts up to 4 scans down to 1).
                var sceneCosmetics = UnityEngine.Object.FindObjectsOfType<Cosmetic>();

                SkipEquipAnimationFor(asset, sceneCosmetics);

                yield return null;                    // Update() → EquipAnimation snaps to 1f

                // Re-scan: the yield may have spawned new components.
                for (int guard = 0; guard < 3 && !IsAnimComplete(asset); guard++)
                {
                    sceneCosmetics = UnityEngine.Object.FindObjectsOfType<Cosmetic>();
                    if (IsAnimComplete(asset, sceneCosmetics)) break;
                    yield return null;
                }

                // Two fallbacks: playerAvatarMenu can be null on some frames via the hover alone.
                var iconHover   = UnityEngine.Object.FindObjectOfType<PlayerAvatarMenuHover>();
                var iconVisuals = iconHover?.playerAvatarMenu?.playerVisuals
                               ?? PlayerAvatarMenu.instance?.playerVisuals;

                // The frame must show ONLY the target. Parts hide two ways that bleed into other icons — PartShrinker (renderer.enabled) and Eye*_Disable conditions (scale-to-zero) — so rebuild + freeze HiddenParts and mirror face-mesh state to ALL avatars (the capture cam may render a different one).
                PartShrinkerBridge.ResyncFromMountedCosmetics(iconVisuals);
                PartShrinkerBridge.SetAllHiddenPartsEnabled(false);
                MirrorFaceMeshesToAllAvatars(iconVisuals);

                // Clear stale conditions so a previous cosmetic's Eye*_Disable doesn't bleed in; the target's broadcaster re-adds its own next frame.
                ResetCustomConditionsForCapture();

                yield return null;                      // broadcaster re-adds target conditions
                MirrorFaceMeshesToAllAvatars(iconVisuals);
                SnapHideConditions();                   // AnimateInstant — no half-shrunk part in the icon
                yield return new WaitForEndOfFrame();    // frame renders settled

                // Re-assert after the frame's Updates in case a condition re-drove a hide.
                MirrorFaceMeshesToAllAvatars(iconVisuals);
                SnapHideConditions();
                yield return new WaitForEndOfFrame();

                if (MetaManager.instance == null) { PartShrinkerBridge.SetAllHiddenPartsEnabled(true); break; }

                bool captured = IconCapture.TryCapture(asset);

                PartShrinkerBridge.SetAllHiddenPartsEnabled(true);

                if (captured) _progressDone++;
                else _progressFailed++;

                {
                    int done  = _progressDone + _progressFailed;
                    int pct   = _progressTotal > 0 ? done * 100 / _progressTotal : 0;
                    ProgressText = _progressFailed > 0
                        ? $"Generating icons: {done}/{_progressTotal} ({pct}%)  |  {_progressFailed} failed"
                        : $"Generating icons: {done}/{_progressTotal} ({pct}%)";
                    if (Plugin.MenuLibAvailable) PopupUpdate();
                }

                // Disable preview so the avatar reverts before the next iteration.
                MetaManager.instance.CosmeticPreviewSet(_state: false);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

                WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(true);

                int total = _progressDone + _progressFailed;
                if (total % LogEvery == 0)
                    BceConsole.LogInfo(
                        $"Batch progress: {total}/{work.Count} " +
                        $"({_progressDone} ok, {_progressFailed} failed)",
                        ConsoleColor.DarkGreen);
            }

            // Completed only if _isRunning is still true: ESC on the last item exits the foreach naturally, but that is NOT a normal completion.
            if (_isRunning) interrupted = false;
        }
        finally
        {
            // ClosePage restores the cosmetics page (no-op if ESC already cleared the popup).
            if (Plugin.MenuLibAvailable) PopupClose();

            // Only act if NotifyMenuClosed hasn't already reset _isRunning.
            if (_isRunning)
            {
                _isRunning = false;
                if (interrupted)
                {
                    int remaining = _progressTotal - _progressDone - _progressFailed;
                    BceConsole.LogWarning(
                        $"GenerateAllIcons: batch interrupted at " +
                        $"{_progressDone + _progressFailed}/{_progressTotal} " +
                        $"({remaining} still to go). " +
                        "Reopen the menu to continue");
                }
            }

            if (MetaManager.instance != null)
            {
                MetaManager.instance.CosmeticPreviewSet(_state: false);
                MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
            }

            WorldCosmeticsSetupPatch.SetAllWorldInstancesActive(true);

            if (_avatarRawImage != null)
            {
                _avatarRawImage.enabled = true;
                _avatarRawImage = null;
            }

            ProgressText = "";
        }

        // Only clear the flag on normal completion; if interrupted, it stays true to resume.
        if (!interrupted)
        {
            Plugin.GenerateAllIcons.Value = false;
            Plugin.Instance.Config.Save();
            _didStartOnce = false; // allow re-click without "interrupted" warning

            BceConsole.LogInfo(
                $"GenerateAllIcons done — {_progressDone} captured, " +
                $"{_progressFailed} failed.",
                ConsoleColor.DarkGreen);

            var cb = OnBatchCompleted;
            OnBatchCompleted = null;
            cb?.Invoke();
        }
    }

    // ── Popup bridge (MenuLib soft dep) ────────────────────────────────────────
    // BatchIconPopup is MenuLib-typed; these wrappers are the ONLY way in. NoInlining keeps MenuLib type resolution out of this class — the wrappers JIT only when invoked, which only happens behind Plugin.MenuLibAvailable checks.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopupOpen() => BatchIconPopup.Open();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopupUpdate() => BatchIconPopup.UpdateProgress();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopupClose() => BatchIconPopup.Close();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopupDestroy() => BatchIconPopup.Destroy();

    // ESC on the popup: stop the batch (coroutine breaks next iteration, its finally cleans up) and restore the avatar immediately. Returns true so the popup closes. MenuLib-free so BatchIconPopup can call back safely.
    internal static bool OnPopupEscape()
    {
        if (_avatarRawImage != null)
        {
            _avatarRawImage.enabled = true;
            _avatarRawImage = null;
        }

        _isRunning = false;

        int remaining = _progressTotal - _progressDone - _progressFailed;
        BceConsole.LogWarning(
            $"GenerateAllIcons: batch interrupted at " +
            $"{_progressDone + _progressFailed}/{_progressTotal} " +
            $"({remaining} still to go). " +
            "Reopen the menu to continue. " +
            "(Your equipped cosmetics were not modified.)");

        return true;
    }

    // PlayerAvatarMenuHover.RawImage is the same component IconCapture reads the RenderTexture from — if capture works, hiding this image works too.
    private static RawImage? FindAvatarRawImage()
    {
        var avatar = UnityEngine.Object.FindObjectOfType<PlayerAvatarMenuHover>();
        return avatar != null ? avatar.GetComponent<RawImage>() : null;
    }

    // iconCreationAvatar = true on every matching Cosmetic → next Update()'s EquipAnimation() snaps equipLerp to 1 before the frame renders. Takes a pre-fetched scene snapshot (no redundant FindObjectsOfType).
    private static FieldInfo? _menuPageStateField;

    private static FieldInfo? _iconCreationAvatarField;
    private static void SkipEquipAnimationFor(CosmeticAsset asset, Cosmetic[]? snapshot = null)
    {
        _iconCreationAvatarField ??= AccessTools.Field(typeof(Cosmetic), "iconCreationAvatar");
        if (_iconCreationAvatarField == null) return;

        var cosmetics = snapshot ?? UnityEngine.Object.FindObjectsOfType<Cosmetic>();
        foreach (var cosmetic in cosmetics)
        {
            if (cosmetic != null && cosmetic.cosmeticAsset == asset)
                _iconCreationAvatarField.SetValue(cosmetic, true);
        }
    }

    // Clears every avatar's custom hide conditions so a previous cosmetic's Eye*_Disable doesn't persist; the target's broadcaster re-adds its own next Update.
    private static void ResetCustomConditionsForCapture()
    {
        foreach (var pc in UnityEngine.Object.FindObjectsOfType<PlayerCosmetics>(true))
        {
            try
            {
                pc.conditionsCustom.Clear();
                pc.ConditionUpdateAll();
            }
            catch (System.Exception ex) { BridgeLog.Debug($"BatchIconGenerator: condition reset failed — {ex.Message}"); }
        }
    }

    // Snaps every CosmeticHideCondition to its target scale (0 or 1) so the captured frame never shows an eye mid-shrink/grow. Mirrors what vanilla does for iconMakerAvatar.
    private static void SnapHideConditions()
    {
        foreach (var hc in UnityEngine.Object.FindObjectsOfType<CosmeticHideCondition>(true))
        {
            try { hc.AnimateInstant(); } catch { }
        }
    }

    // Face meshes whose visibility must match the per-target preview state across all avatars.
    private static readonly string[] FaceMeshNames =
        { "mesh_eye_l", "mesh_eye_r", "mesh_pupil_l", "mesh_pupil_r", "mesh_head_top" };

    // Mirrors the reference avatar's eye/pupil/head renderer states onto EVERY PlayerAvatarVisuals — with HiddenParts frozen, whatever avatar the capture cam renders shows the per-target face state.
    private static void MirrorFaceMeshesToAllAvatars(PlayerAvatarVisuals? reference)
    {
        if (reference == null) return;

        var want = new Dictionary<string, bool>();
        foreach (var r in reference.GetComponentsInChildren<MeshRenderer>(true))
        {
            string nm = r.gameObject.name;
            if (Array.IndexOf(FaceMeshNames, nm) >= 0 && !want.ContainsKey(nm))
                want[nm] = r.enabled;
        }
        if (want.Count == 0) return;

        foreach (var v in UnityEngine.Object.FindObjectsOfType<PlayerAvatarVisuals>(true))
        {
            foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true))
                if (want.TryGetValue(r.gameObject.name, out bool en) && r.enabled != en)
                    r.enabled = en;
        }
    }

    private static FieldInfo? _equipLerpField;
    private static bool IsAnimComplete(CosmeticAsset asset, Cosmetic[]? snapshot = null)
    {
        _equipLerpField ??= AccessTools.Field(typeof(Cosmetic), "equipLerp");
        if (_equipLerpField == null) return true;

        var cosmetics = snapshot ?? UnityEngine.Object.FindObjectsOfType<Cosmetic>();
        foreach (var c in cosmetics)
        {
            if (c != null && c.cosmeticAsset == asset &&
                (float)(_equipLerpField.GetValue(c) ?? 1f) < 1f)
                return false;
        }
        return true;
    }
}
