// ============================================================================
// [MenuCapture] One-shot batch icon generator.
//
// When the player opens the cosmetics menu AND the [Icons] GenerateAllIcons
// flag is true, this coroutine cycles through every bridge cosmetic that has
// no cached icon yet. For each one it:
//   1. Preview-equips it via MetaManager.CosmeticEquip(asset, _isPreview:true)
//   2. Waits a couple frames for the avatar to render
//   3. Captures the menu's RT into a PNG via IconCapture.TryCapture
//   4. Preview-unequips it
//
// At the end it restores whatever the user was previewing/equipping, calls
// CosmeticPlayerUpdateLocal to refresh the avatar, then flips the config
// flag back to false so the batch is genuinely one-shot.
//
// The avatar visibly cycles through cosmetics during this — that's intentional
// progress feedback. We also log every N items so you can follow along in the
// console.
//
// To disable / remove: set GenerateAllIcons=false (default).
// ============================================================================

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

internal static class BatchIconGenerator
{
    // We don't want to retrigger if the user opens the menu twice in the same session.
    private static bool _ranThisSession;

    internal static void TryStart(MonoBehaviour host)
    {
        if (_ranThisSession) return;
        if (!Plugin.GenerateAllIcons.Value) return;

        host.StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        yield return new WaitForSeconds(0.5f);

        if (MetaManager.instance == null) yield break;
        _ranThisSession = true;

        var savedPreview = MetaManager.instance.cosmeticEquippedPreview?.ToList()
                           ?? new List<int>();

        var work = new List<CosmeticAsset>();
        foreach (var asset in MetaManager.instance.cosmeticAssets)
        {
            if (asset == null) continue;
            if (asset.assetId == null) continue;
            if (!BridgeIds.IsBridgeAsset(asset)) continue;
            if (IconCapture.HasCache(asset)) continue;
            work.Add(asset);
        }

        Plugin.Logger.LogInfo($"GenerateAllIcons: {work.Count} icon(s) to generate.");

        int done = 0;
        int failed = 0;
        const int LogEvery = 50;

        var savedColorsPreview = MetaManager.instance.colorsEquippedPreview != null
            ? (int[])MetaManager.instance.colorsEquippedPreview.Clone()
            : null;

        foreach (var asset in work)
        {
            MetaManager.instance.cosmeticEquippedPreview = MetaManager.instance.cosmeticEquipped.ToList();
            MetaManager.instance.colorsEquippedPreview = (int[])MetaManager.instance.colorsEquipped.Clone();
            MetaManager.instance.CosmeticEquip(asset, _isPreview: true);
            MetaManager.instance.CosmeticPreviewSet(_state: true);
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

            yield return null;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            if (IconCapture.TryCapture(asset, asset.type)) done++;
            else failed++;

            MetaManager.instance.CosmeticUnequip(asset, _isPreview: true, _save: false, _resetColor: false);

            if ((done + failed) % LogEvery == 0)
                Plugin.Logger.LogInfo($"Batch progress: {done + failed}/{work.Count} ({done} ok, {failed} failed)");
        }

        MetaManager.instance.cosmeticEquippedPreview = savedPreview;
        if (savedColorsPreview != null)
            MetaManager.instance.colorsEquippedPreview = savedColorsPreview;
        MetaManager.instance.CosmeticPreviewSet(_state: false);
        MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);

        Plugin.GenerateAllIcons.Value = false;
        Plugin.Instance.Config.Save();

        Plugin.Logger.LogInfo(
            $"GenerateAllIcons done — {done} captured, {failed} failed. Flag reset to false.");
    }
}

[HarmonyPatch(typeof(MenuPageCosmetics), "Start")]
internal static class BatchIconGeneratorTrigger
{
    [HarmonyPostfix]
    private static void Postfix(MenuPageCosmetics __instance)
    {
        BatchIconGenerator.TryStart(__instance);
    }
}
