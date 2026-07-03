using MenuLib;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Custom-RGB popup ("C" button): R/G/B sliders drive a live swatch + the live avatar; Save commits, Remove clears. Modes: per-cosmetic (asset != null) colours one cosmetic; section (asset == null) colours every eligible cosmetic in the painted section.
internal static class CustomColorPopup
{
    // Sentinel used to distinguish "not a section call" from a real colorKey of 0.
    private const int NotSection = int.MinValue;

    private const float PopupX       = -120f;
    private const float TitleGap     = 15f;
    private const float BtnTopGap    = 10f;
    private const float PopupSpacing = 5f;

    private const float BtnCancelX = -137f;
    private const float BtnSaveX   =   58f;
    private const float BtnRemoveX =    0f;

    private const float SwatchSize = 28f;
    private const float SwatchX    = -10f;

    private static readonly string[] ByteOptions = BuildByteOptions();
    private static string[] BuildByteOptions()
    {
        var a = new string[256];
        for (int i = 0; i < 256; i++) a[i] = i.ToString();
        return a;
    }

    // Per-cosmetic overload (bridge, vanilla, modded).
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(CosmeticAsset? asset)
    {
        if (asset == null) return;
        PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowImpl(asset, NotSection, default));
    }

    // Section overload — applies to every eligible cosmetic in the painted section.
    internal static void Show(CosmeticAsset? asset, int sectionColorKey, MenuPageColor.ColorPageType sectionPageMode)
    {
        // asset should be null here (section mode); if it's non-null fall back to per-cosmetic.
        if (asset != null)
        {
            PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowImpl(asset, NotSection, default));
            return;
        }
        PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowImpl(null, sectionColorKey, sectionPageMode));
    }

    private static void ShowImpl(CosmeticAsset? asset, int sectionColorKey, MenuPageColor.ColorPageType sectionPageMode)
    {
        bool isSection = asset == null;
        string assetId = asset?.assetId ?? "";

        // Bridge cosmetics and modded cosmetics with a per-part slot layout (YoshiCarry) both colour through BridgeTintMaterials, so both take the per-slot BTM path; everything else uses the vanilla PlayerMaterial path.
        bool slotCapable = !isSection && (BridgeIds.IsBridgeAsset(asset!) || ModdedSlotLayout.Handles(asset!));

        // Slot is only relevant for slot-capable cosmetics.
        int slot = slotCapable ? PerCosmeticColors.ActiveSlot : -1;

        // Starting colour: per-cosmetic → existing custom if any; section → white.
        Color start = Color.white;
        if (!isSection)
        {
            if (slot >= 0 && PerCosmeticColors.TryGetCustomSlotColor(assetId, slot, out var sc)) start = sc;
            else if (PerCosmeticColors.TryGetCustomColor(assetId, out var wc)) start = wc;
        }
        int r = Mathf.Clamp(Mathf.RoundToInt(start.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(start.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(start.b * 255f), 0, 255);

        Image? swatch = null;
        Color Current() => new(r / 255f, g / 255f, b / 255f);

        void ApplyLive()
        {
            if (swatch != null) swatch.color = Current();
            if (isSection)
            {
                VanillaTintHelper.ApplyCustomRGBToSectionLive(sectionColorKey, sectionPageMode, Current());
            }
            else if (slotCapable)
            {
                if (slot >= 0) BridgeTintHelper.ApplySlotRGBToLiveInstances(asset!, slot, Current());
                else           BridgeTintHelper.ApplyWholeAssetRGBToLiveInstances(asset!, Current());
            }
            else
            {
                VanillaTintHelper.ApplyCustomRGBToLiveInstances(asset!, Current());
            }
        }

        string title = isSection ? "Section" :
            (!string.IsNullOrEmpty(asset!.assetName) ? asset.assetName : asset.name);
        string slotLabel = (!isSection && slot >= 0) ? $"  (slot {slot + 1})" : "";
        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: $"Custom Color{slotLabel}\n{title}",
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: PopupSpacing,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup); // inputGuard: true — blocks underlying colour-page chip clicks

        PopupUI.AddIntSlider(popup, "Red",   ByteOptions, r, v => { r = Mathf.RoundToInt(v); ApplyLive(); }, TitleGap);
        PopupUI.AddIntSlider(popup, "Green", ByteOptions, g, v => { g = Mathf.RoundToInt(v); ApplyLive(); });
        PopupUI.AddIntSlider(popup, "Blue",  ByteOptions, b, v => { b = Mathf.RoundToInt(v); ApplyLive(); });

        // Live preview swatch.
        popup.AddElementToScrollView(scrollView =>
        {
            var go = new GameObject("CustomColorPreview", typeof(RectTransform));
            go.transform.SetParent(scrollView, false);
            var img = go.AddComponent<Image>();
            img.color = Current();
            swatch = img;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(SwatchSize, SwatchSize);
            rt.anchoredPosition = new Vector2(SwatchX, 0f);
            return rt;
        }, topPadding: BtnTopGap);

        // Cancel / Save row.
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                popup.ClosePage(false);
                RuntimeConfigApplier.ReapplyLocalCosmeticColors();   // revert live preview
            }, row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Save", () =>
            {
                if (isSection)
                {
                    VanillaTintHelper.SaveCustomColorToSection(sectionColorKey, sectionPageMode, Current());
                }
                else if (slot >= 0)
                {
                    PerCosmeticColors.SetCustomSlotColor(assetId, slot, Current());
                    RuntimeConfigApplier.ReapplyLocalCosmeticColors();
                }
                else
                {
                    PerCosmeticColors.SetCustomColor(assetId, Current());
                    RuntimeConfigApplier.ReapplyLocalCosmeticColors();
                }
                BridgeCustomColorButton.Active?.SetDisplayColor(Current());
                BridgeCustomColorButton.Active?.SelectRingNow();
                BridgeSlotSelectorRow.Active?.Refresh();
                popup.ClosePage(false);
                if (!isSection && SemiFunc.IsMultiplayer()) PerCosmeticColorNetworkSync.BroadcastAll();
            }, row, new Vector2(BtnSaveX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Remove custom", () =>
            {
                if (isSection)
                {
                    VanillaTintHelper.RemoveCustomColorFromSection(sectionColorKey, sectionPageMode);
                }
                else if (slot >= 0)
                {
                    if (PerCosmeticColors.RemoveCustomSlotNoSave(assetId, slot)) PerCosmeticColors.SaveCustomSlots();
                    RuntimeConfigApplier.ReapplyLocalCosmeticColors();
                }
                else
                {
                    if (PerCosmeticColors.RemoveCustomColorNoSave(assetId)) PerCosmeticColors.SaveCustom();
                    RuntimeConfigApplier.ReapplyLocalCosmeticColors();
                    if (SemiFunc.IsMultiplayer()) PerCosmeticColorNetworkSync.BroadcastAll();
                }
                popup.ClosePage(false);
                BridgeSlotSelectorRow.Active?.Refresh();
            }, row, new Vector2(BtnRemoveX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        // Defer opening to mouse-release: the colour-page chip that opened this is still pressed, and the same press would land on a slider or the chips below.
        popup.OpenPage(openOnTop: true);
    }
}
