// Sub-editor for the death-head "floor pose" (from "Impact Pose →"). Done returns the edited pose, Cancel leaves it; onPreview fires live. Scale range is wider (0.05–2.00) so "flatten" poses can go below the offset editor's 0.50 floor.

using MenuLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MoreHeadBridge;

internal static class DeathHeadFloorPosePopup
{
    // Two-state options for the react toggles.
    private static readonly string[] OnOffOptions = { "On", "Off" };

    // Scale: 0.05 to 2.00 in 0.05 steps — lower than the offset editor so cosmetics can flatten.
    private static readonly string[] ScaleOptions = BuildScaleOptions();
    private static string[] BuildScaleOptions()
    {
        var list = new List<string>();
        for (float v = 0.05f; v <= 2.0001f; v += 0.05f)
            list.Add(v.ToString("F2", CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    /// existing — the current pose (cloned for editing); null builds a fresh default pose.
    /// onPreview(pose) fires on every change; onDone(pose) commits; onClose runs on Cancel/Done
    /// so the caller can restore its own (offset) preview.
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(DeathHeadFloorPose? existing,
                              Action<DeathHeadFloorPose> onPreview,
                              Action<DeathHeadFloorPose> onDone,
                              Action onClose,
                              Transform? parentPopupTransform)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(
            existing, onPreview, onDone, onClose, parentPopupTransform));

    private static void ShowNow(DeathHeadFloorPose? existing,
                              Action<DeathHeadFloorPose> onPreview,
                              Action<DeathHeadFloorPose> onDone,
                              Action onClose,
                              Transform? parentPopupTransform)
    {
        var pose = existing?.Clone() ?? new DeathHeadFloorPose();
        pose.MigrateLegacy(); // fold legacy "React to Floor" into ReactWhenDead

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: "Impact Pose",
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupUI.PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        void Preview() => onPreview(pose);

        // ── React toggles (top) ────────────────────────────────────────────
        // Independent: the cosmetic can react while alive (pushes into a low ceiling / wall on the live body), while dead (contacts ground on the death head), or both.
        popup.AddElementToScrollView(scrollView =>
        {
            var s = MenuAPI.CreateREPOSlider(
                "React when alive", "",
                (string opt) => { pose.ReactWhenAlive = opt == "On"; Preview(); },
                scrollView, OnOffOptions, pose.ReactWhenAlive ? "On" : "Off");
            return (RectTransform)s.transform;
        }, topPadding: PopupUI.TitleGap);

        popup.AddElementToScrollView(scrollView =>
        {
            var s = MenuAPI.CreateREPOSlider(
                "React when dead", "",
                (string opt) => { pose.ReactWhenDead = opt == "On"; Preview(); },
                scrollView, OnOffOptions, pose.ReactWhenDead ? "On" : "Off");
            return (RectTransform)s.transform;
        }, topPadding: 0f);

        // Pose sliders — a gap above the first separates them from the toggle.
        PopupUI.AddFloatSlider(popup, "Pos X", CosmeticOffsetEntryPopup.PosOptions, pose.PosX, v => { pose.PosX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddFloatSlider(popup, "Pos Y", CosmeticOffsetEntryPopup.PosOptions, pose.PosY, v => { pose.PosY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Pos Z", CosmeticOffsetEntryPopup.PosOptions, pose.PosZ, v => { pose.PosZ = v; Preview(); });

        PopupUI.AddIntSlider(popup, "Rot X", CosmeticOffsetEntryPopup.RotOptions, (int)pose.RotX, v => { pose.RotX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddIntSlider(popup, "Rot Y", CosmeticOffsetEntryPopup.RotOptions, (int)pose.RotY, v => { pose.RotY = v; Preview(); });
        PopupUI.AddIntSlider(popup, "Rot Z", CosmeticOffsetEntryPopup.RotOptions, (int)pose.RotZ, v => { pose.RotZ = v; Preview(); });

        PopupUI.AddFloatSlider(popup, "Scale X", ScaleOptions, pose.ScaleX, v => { pose.ScaleX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddFloatSlider(popup, "Scale Y", ScaleOptions, pose.ScaleY, v => { pose.ScaleY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Scale Z", ScaleOptions, pose.ScaleZ, v => { pose.ScaleZ = v; Preview(); });

        PopupUI.AddIntSlider(popup, "Lerp Speed", CosmeticOffsetEntryPopup.SpeedOptions,
            Mathf.Clamp((int)pose.LerpSpeed, 1, 10), v => { pose.LerpSpeed = v; Preview(); }, PopupUI.BtnTopGap);

        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                popup.ClosePage(false);
                onClose();
            }, row, new Vector2(PopupUI.ButtonLeftX, 0f));

            MenuAPI.CreateREPOButton("Done", () =>
            {
                popup.ClosePage(false);
                onDone(pose);
                onClose();
            }, row, new Vector2(PopupUI.ButtonRightX, 0f));

            return row;
        }, topPadding: PopupUI.BtnTopGap);

        // Show the pose immediately so the preview reflects the starting values.
        Preview();
        popup.OpenPage(openOnTop: true);
    }
}
