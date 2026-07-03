// Popup for a per-cosmetic colour animation ("A" button): Cycle Smooth = palette sequence + Direction/Speed; Rainbow = Direction/Speed only.

using System.Collections.Generic;
using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class ColorAnimationPopup
{
    private const float PopupX = -120f;
    private const float TitleGap = 15f;
    private const float BtnTopGap = 10f;
    private const float BtnTopPadding = 10f;
    private const float RowH = 30f;
    private const float PopupSpacing = 5f;

    private const float BtnAddX = -137f;

    private const float BtnCancelX = -137f;
    private const float BtnRemoveX = -40f;
    private const float BtnClearX = 58f;
    private const float BtnClearAnimX = 0f;
    
    private const float BtnSaveX = 58f;

    private const float PreviewX = 110f;     // preview swatch X within the colour-slider row (clear of the scrollbar)
    private const float PreviewY = -20f;      // preview swatch Y within the colour-slider row
    private const float SwatchSize = 24f;
    private const float SeqStartX = -118f;   // left edge of the sequence strip
    private const float SeqStep = 28f;       // spacing between sequence swatches

    private const string ModeCycle = "Cycle Smooth";
    private const string ModeRainbow = "Rainbow";
    private static readonly string[] ModeOptions = { ModeCycle, ModeRainbow };
    private static readonly string[] DirOptions = { "Loop", "Ping-Pong" };

    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(CosmeticAsset? asset)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(asset));

    private static void ShowNow(CosmeticAsset? asset)
    {
        if (asset == null) return;
        var palette = MetaManager.instance?.colors;
        int colorCount = palette?.Count ?? 0;
        if (colorCount == 0) return;

        string assetId = asset.assetId;

        // The slot the picker is on: -1 = ALL (whole asset), >= 0 = a specific material slot.
        int slot = PerCosmeticColors.ActiveSlot;

        // ── Working state ─────────────────────────────────────────────────────
        var existing =
            slot >= 0 ? (PerCosmeticColors.TryGetSlotAnimation(assetId, slot, out var ss) ? ss : null)
                      : (PerCosmeticColors.TryGetAnimation(assetId, out var s) ? s : null);
        string uiMode = existing != null && existing.Mode == ColorAnimMode.Rainbow ? ModeRainbow : ModeCycle;
        var dir = existing?.Dir ?? ColorAnimDir.Loop;
        // Separate speed per mode: Cycle allows fast steps (0.1s+); Rainbow is clamped to 3s+ because faster hue sweeps flicker uncomfortably.
        float cycleSpeed = existing != null && existing.Mode == ColorAnimMode.CycleSmooth
            ? Mathf.Max(1f, existing.SecondsPerStep) : 1f;
        float rainbowSpeed = existing != null && existing.Mode == ColorAnimMode.Rainbow
            ? Mathf.Max(3f, existing.SecondsPerStep) : 3f;
        var sequence = existing != null ? new List<int>(existing.Palette) : new List<int>();
        int candidate = 0;

        Color PaletteColor(int idx)
            => (palette != null && idx >= 0 && idx < palette.Count) ? palette[idx].color : Color.white;

        // ── Popup ─────────────────────────────────────────────────────────────
        string title = !string.IsNullOrEmpty(asset.assetName) ? asset.assetName : asset.name;
        string slotLabel = slot >= 0 ? $"  (slot {slot + 1})" : "";
        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: $"Animate Color{slotLabel}\n{title}",
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: PopupSpacing,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup); // inputGuard: true — blocks underlying colour-page chip clicks

        var builderEls = new List<REPOScrollViewElement>(); // Cycle only
        var rainbowEls = new List<REPOScrollViewElement>(); // Rainbow only
        RectTransform seqRow = null!;
        Image? previewImg = null;

        void UpdatePreview()
        {
            if (previewImg != null) previewImg.color = PaletteColor(candidate);
        }

        void RebuildSeq()
        {
            if (seqRow == null) return;
            for (int i = seqRow.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(seqRow.GetChild(i).gameObject);
            for (int i = 0; i < sequence.Count; i++)
                MakeSwatch(seqRow, new Vector2(SeqStartX + i * SeqStep, 0f), PaletteColor(sequence[i]));
        }

        void RefreshAllLive()
        {
            foreach (var pc in UnityEngine.Object.FindObjectsOfType<PlayerCosmetics>())
                ColorAnimatorRefresher.RefreshLiveAnimators(pc);

            // A per-cosmetic animate change must rebuild our minis' static death-head clones too — a mini already in death-head state keeps the pre-change bake otherwise (the live-body refresh never touches the cached clone). Same fix as RefreshColorAnimations.
            MiniSemibotSpawner.InvalidateLocalDeathHeads();
        }

        void ApplyVisibility()
        {
            bool cycle = uiMode == ModeCycle;
            foreach (var el in builderEls) if (el != null) el.visibility = cycle;
            foreach (var el in rainbowEls) if (el != null) el.visibility = !cycle;
            popup.scrollView.UpdateElements();
        }

        // ── Mode ──────────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var sl = MenuAPI.CreateREPOSlider("Mode", "", opt => { uiMode = opt; ApplyVisibility(); },
                scrollView, ModeOptions, uiMode);
            return (RectTransform)sl.transform;
        }, topPadding: TitleGap);

        // ── Direction (both modes) ────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var sl = MenuAPI.CreateREPOSlider("Direction", "", opt =>
                dir = opt == DirOptions[1] ? ColorAnimDir.PingPong : ColorAnimDir.Loop,
                scrollView, DirOptions, dir == ColorAnimDir.PingPong ? DirOptions[1] : DirOptions[0]);
            return (RectTransform)sl.transform;
        });

        // ── Speed — Cycle (0.1s+) ─────────────────────────────────────────────
        REPOSlider cycleSpeedSlider = null!;
        popup.AddElementToScrollView(scrollView =>
        {
            cycleSpeedSlider = MenuAPI.CreateREPOSlider("Speed", "seconds per step",
                v => cycleSpeed = v, scrollView, default, 1f, 8f, 1, cycleSpeed, "", "s");
            return (RectTransform)cycleSpeedSlider.transform;
        });
        builderEls.Add(cycleSpeedSlider.GetComponent<REPOScrollViewElement>());

        // ── Speed — Rainbow (3s+, faster flickers too much) ───────────────────
        REPOSlider rainbowSpeedSlider = null!;
        popup.AddElementToScrollView(scrollView =>
        {
            rainbowSpeedSlider = MenuAPI.CreateREPOSlider("Speed", "seconds per hue loop",
                v => rainbowSpeed = v, scrollView, default, 3f, 15f, 1, rainbowSpeed, "", "s");
            return (RectTransform)rainbowSpeedSlider.transform;
        });
        rainbowEls.Add(rainbowSpeedSlider.GetComponent<REPOScrollViewElement>());

        // ── Colour slider + live preview swatch (Cycle) ───────────────────────
        REPOSlider colourSlider = null!;
        popup.AddElementToScrollView(scrollView =>
        {
            colourSlider = MenuAPI.CreateREPOSlider("Color", "scrub the palette",
                v => { candidate = Mathf.Clamp(v - 1, 0, colorCount - 1); UpdatePreview(); },
                scrollView, default, 1, colorCount, 1, "#", "");

            previewImg = MakeSwatch((RectTransform)colourSlider.transform,
                new Vector2(PreviewX, PreviewY), PaletteColor(candidate));
            return (RectTransform)colourSlider.transform;
        }, topPadding: BtnTopGap);
        builderEls.Add(colourSlider.GetComponent<REPOScrollViewElement>());

        // ── Add / Remove / Clear (Cycle) ──────────────────────────────────────
        RectTransform editRow = null!;
        popup.AddElementToScrollView(scrollView =>
        {
            editRow = new GameObject("Edit Row", typeof(RectTransform)).GetComponent<RectTransform>();
            editRow.SetParent(scrollView, false);
            editRow.sizeDelta = new Vector2(0f, RowH);

            MenuAPI.CreateREPOButton("Add", () => { sequence.Add(candidate); RebuildSeq(); },
                editRow, new Vector2(BtnAddX, 0f));
            MenuAPI.CreateREPOButton("Remove", () =>
            {
                if (sequence.Count > 0) { sequence.RemoveAt(sequence.Count - 1); RebuildSeq(); }
            }, editRow, new Vector2(BtnRemoveX, 0f));
            MenuAPI.CreateREPOButton("Clear", () => { sequence.Clear(); RebuildSeq(); },
                editRow, new Vector2(BtnClearX, 0f));

            return editRow;
        }, topPadding: BtnTopGap);
        builderEls.Add(editRow.GetComponent<REPOScrollViewElement>());

        // ── Sequence label + strip (Cycle) ────────────────────────────────────
        REPOLabel seqLabel = null!;
        popup.AddElementToScrollView(scrollView =>
        {
            seqLabel = MenuAPI.CreateREPOLabel("Sequence:", scrollView);
            return (RectTransform)seqLabel.transform;
        }, topPadding: BtnTopGap);
        builderEls.Add(seqLabel.GetComponent<REPOScrollViewElement>());

        popup.AddElementToScrollView(scrollView =>
        {
            seqRow = new GameObject("Sequence Strip", typeof(RectTransform)).GetComponent<RectTransform>();
            seqRow.SetParent(scrollView, false);
            seqRow.sizeDelta = new Vector2(0f, RowH);
            RebuildSeq();
            return seqRow;
        });
        builderEls.Add(seqRow.GetComponent<REPOScrollViewElement>());

        // ── Bottom row 1: Cancel (left) | Save (right) ────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = new GameObject("Bottom Row 1", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(scrollView, false);
            row.sizeDelta = new Vector2(0f, RowH);

            MenuAPI.CreateREPOButton("Cancel", () => popup.ClosePage(false),
                row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Save", () =>
            {
                if (uiMode == ModeCycle && sequence.Count == 0)
                {
                    if (slot >= 0) PerCosmeticColors.ClearSlotAnimationForAsset(assetId, slot);
                    else PerCosmeticColors.ClearAnimationForAsset(assetId);
                }
                else
                {
                    var spec = new ColorAnimation
                    {
                        Mode = uiMode == ModeRainbow ? ColorAnimMode.Rainbow : ColorAnimMode.CycleSmooth,
                        Dir = dir,
                        SecondsPerStep = uiMode == ModeRainbow ? rainbowSpeed : cycleSpeed,
                        Palette = new List<int>(sequence),
                    };
                    if (slot >= 0) PerCosmeticColors.SetSlotAnimation(assetId, slot, spec);
                    else PerCosmeticColors.SetAnimation(assetId, spec);
                    // Animation now active → move the selection ring onto the "A" button.
                    BridgeAnimateButton.Active?.SelectRingNow();
                }
                RefreshAllLive();
                BridgeSlotSelectorRow.Active?.Refresh();   // re-tint slot buttons after the change
                popup.ClosePage(false);
            }, row, new Vector2(BtnSaveX, 0f));

            return row;
        }, topPadding: (BtnTopPadding * 2f));

        // ── Bottom row 2: Remove anim (right) ─────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = new GameObject("Bottom Row 2", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(scrollView, false);
            row.sizeDelta = new Vector2(0f, RowH);

            MenuAPI.CreateREPOButton("Remove anim", () =>
            {
                if (slot >= 0) PerCosmeticColors.ClearSlotAnimationForAsset(assetId, slot);
                else PerCosmeticColors.ClearAnimationForAsset(assetId);
                RefreshAllLive();
                BridgeSlotSelectorRow.Active?.Refresh();
                popup.ClosePage(false);
            }, row, new Vector2(BtnClearAnimX, 0f));

            return row;
        }, topPadding: BtnTopPadding);

        ApplyVisibility();
        popup.scrollView.UpdateElements();
        popup.OpenPage(openOnTop: true);

        // Block the colour page's buttons behind us so they don't react to hover/click.
        popup.gameObject.AddComponent<ColorPageInputBlocker>().Block();
    }

    // Creates a solid-colour UI Image swatch (centred-anchor) at the given local position.
    private static Image MakeSwatch(RectTransform parent, Vector2 pos, Color color)
    {
        var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(SwatchSize, SwatchSize);
        rt.anchoredPosition = pos;

        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}
