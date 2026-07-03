// Popup for the crown target on Hat/HeadTopMesh bridge cosmetics ("Crown Settings →"). Display values are RELATIVE to the vanilla bare-head default; stored values add a fixed offset (Y +0.35, Z +0.25).

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticCrownPopup
{
    // ── Display ↔ internal offset ─────────────────────────────────────────────
    // Internal pos = display pos + offset; (0,0,0) display maps to the vanilla bare-head default crown position.
    private const float PosYOffset = 0.35f;
    private const float PosZOffset = 0.25f;

    // ── Layout ────────────────────────────────────────────────────────────────
    private const float PopupX    = -120f;
    private const float TitleGap  = 15f;
    private const float BtnTopGap = 10f;
    private const float BtnRowH   = 30f;
    private const float BtnDoneX   =  58f;
    private const float BtnCancelX = -137f;
    private const float BtnClearX  = -137f;

    // ── Slider option arrays (built once) ─────────────────────────────────────

    // Position: −1.00 to +1.00 in 0.05 steps.
    private static readonly string[] PosOptions = BuildRange(-1.00f, 1.00f, 0.05f, "F2");

    // Rotation: −180 to +180 in 5° steps.
    private static readonly string[] RotOptions = BuildIntRange(-180, 180, 5);

    // Scale: 0.50 to 2.00 in 0.05 steps.
    private static readonly string[] ScaleOptions = BuildRange(0.50f, 2.00f, 0.05f, "F2");

    // Priority: −50 to +50 in 5 steps.
    private static readonly string[] PriorityOptions = BuildIntRange(-50, 50, 5);

    private static readonly string[] SpringOptions = { "No", "Yes" };

    // ── Entry point ───────────────────────────────────────────────────────────

    /// existing: config to edit, or null to create. cosmeticType: sets the priority default (Hat→0, HeadTopMesh→−50).
    /// onDone: fires with the config on Done. onClear: fires on Clear Crown (not on Cancel).
    /// onPreview: fires on every slider change with the uncommitted config; null on Cancel (revert).
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(
        CosmeticCrownConfig? existing,
        SemiFunc.CosmeticType cosmeticType,
        Action<CosmeticCrownConfig> onDone,
        Action? onClear = null,
        Action<CosmeticCrownConfig?>? onPreview = null,
        Transform? parentPopupTransform = null,
        Action? onCancel = null)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(
            existing, cosmeticType, onDone, onClear, onPreview, parentPopupTransform, onCancel));

    private static void ShowNow(
        CosmeticCrownConfig? existing,
        SemiFunc.CosmeticType cosmeticType,
        Action<CosmeticCrownConfig> onDone,
        Action? onClear,
        Action<CosmeticCrownConfig?>? onPreview,
        Transform? parentPopupTransform,
        Action? onCancel)
    {
        float displayX = existing?.PosX ?? 0f;
        float displayY = existing != null ? existing.PosY - PosYOffset : 0f;
        float displayZ = existing != null ? existing.PosZ - PosZOffset : 0f;
        float rotX        = existing?.RotX        ?? 0f;
        float rotY        = existing?.RotY        ?? 0f;
        float rotZ        = existing?.RotZ        ?? 0f;
        float scaleX      = existing?.ScaleX      ?? 1f;
        float scaleY      = existing?.ScaleY      ?? 1f;
        float scaleZ      = existing?.ScaleZ      ?? 1f;
        int   priority    = existing?.Priority    ??
            (cosmeticType == SemiFunc.CosmeticType.HeadTopMesh ? -50 : 0);
        bool  disableSpring = existing?.DisableSpring ?? false;

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: "Crown Settings",
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        // Helper: fires onPreview with current display values after every slider change.
        void Preview() => FirePreview(onPreview,
            displayX, displayY, displayZ,
            rotX, rotY, rotZ,
            scaleX, scaleY, scaleZ,
            priority, disableSpring);

        // ── Position sliders ──────────────────────────────────────────────────
        PopupUI.AddFloatSlider(popup, "Pos X", PosOptions, displayX, v => { displayX = v; Preview(); }, TitleGap);
        PopupUI.AddFloatSlider(popup, "Pos Y", PosOptions, displayY, v => { displayY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Pos Z", PosOptions, displayZ, v => { displayZ = v; Preview(); });

        // ── Rotation sliders ──────────────────────────────────────────────────
        PopupUI.AddIntSlider(popup, "Rot X", RotOptions, (int)rotX, v => { rotX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddIntSlider(popup, "Rot Y", RotOptions, (int)rotY, v => { rotY = v; Preview(); });
        PopupUI.AddIntSlider(popup, "Rot Z", RotOptions, (int)rotZ, v => { rotZ = v; Preview(); });

        // ── Scale sliders ─────────────────────────────────────────────────────
        PopupUI.AddFloatSlider(popup, "Scale X", ScaleOptions, scaleX, v => { scaleX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddFloatSlider(popup, "Scale Y", ScaleOptions, scaleY, v => { scaleY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Scale Z", ScaleOptions, scaleZ, v => { scaleZ = v; Preview(); });

        // ── Priority slider ───────────────────────────────────────────────────
        PopupUI.AddIntSlider(popup, "Priority", PriorityOptions, priority, v => { priority = (int)v; Preview(); }, PopupUI.BtnTopGap);

        // ── Disable Spring slider ─────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var s = MenuAPI.CreateREPOSlider("Disable Spring", "",
                (string opt) => { disableSpring = opt == "Yes"; Preview(); },
                scrollView, SpringOptions, disableSpring ? "Yes" : "No");
            return (RectTransform)s.transform;
        }, topPadding: PopupUI.TitleGap);

        // ── Clear Crown — only shown when editing an existing config ──────────
        if (existing != null && onClear != null)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Clear Crown", () =>
                {
                    popup.ClosePage(false);
                    onClear();
                }, row, new Vector2(BtnClearX, 0f));
                return row;
            }, topPadding: BtnTopGap);
        }

        // ── Cancel / Done ─────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            // Cancel: close without any callback (pendingCrown unchanged); revert preview to the state before this sub-popup opened.
            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                onPreview?.Invoke(existing); // null = no crown, non-null = prior config
                popup.ClosePage(false);
                onCancel?.Invoke();
            }, row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Done", () =>
            {
                popup.ClosePage(false);
                onDone(new CosmeticCrownConfig
                {
                    PosX         = displayX,
                    PosY         = displayY + PosYOffset,   // display → internal
                    PosZ         = displayZ + PosZOffset,
                    RotX         = rotX,
                    RotY         = rotY,
                    RotZ         = rotZ,
                    ScaleX       = scaleX,
                    ScaleY       = scaleY,
                    ScaleZ       = scaleZ,
                    Priority     = priority,
                    DisableSpring = disableSpring,
                });
            }, row, new Vector2(BtnDoneX, 0f));

            return row;
        }, topPadding: existing != null ? 0f : BtnTopGap);

        // Fire an initial preview so the crown is visible immediately on open — without it, the crown only appears after the first slider move.
        Preview();
        popup.OpenPage(openOnTop: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void FirePreview(
        Action<CosmeticCrownConfig?>? onPreview,
        float displayX, float displayY, float displayZ,
        float rotX, float rotY, float rotZ,
        float scaleX, float scaleY, float scaleZ,
        int priority, bool disableSpring)
    {
        onPreview?.Invoke(new CosmeticCrownConfig
        {
            PosX = displayX,
            PosY = displayY + PosYOffset,
            PosZ = displayZ + PosZOffset,
            RotX = rotX, RotY = rotY, RotZ = rotZ,
            ScaleX = scaleX, ScaleY = scaleY, ScaleZ = scaleZ,
            Priority = priority,
            DisableSpring = disableSpring,
        });
    }

    private static string[] BuildRange(float min, float max, float step, string fmt)
    {
        var list = new List<string>();
        for (float v = min; v <= max + step * 0.001f; v += step)
            list.Add(v.ToString(fmt, CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    private static string[] BuildIntRange(int min, int max, int step)
    {
        var list = new List<string>();
        for (int v = min; v <= max; v += step)
            list.Add(v.ToString(CultureInfo.InvariantCulture));
        return list.ToArray();
    }

}
