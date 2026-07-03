// Second-level popup from "Hide Conditions →": per-cosmetic hide-self rules — hide while a chosen TYPE is equipped, or while a shape/state condition is active. Edits mutate the shared config (committed on the main popup's Save); Done keeps, Cancel reverts to snapshot.

using MenuLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticHidePopup
{
    private const float PopupX = -120f;
    private const float TitleGap = 15f;
    private const float BtnTopGap = 10f;
    private const float BtnDoneX = 58f;
    private const float BtnCancelX = -137f;

    // Curated "hide when equipped" trigger types — the categories that commonly cover/clip. World is synthetic and overlays never clip, so both are excluded.
    private static readonly SemiFunc.CosmeticType[] HideWhenTypes =
    {
        SemiFunc.CosmeticType.Hat,
        SemiFunc.CosmeticType.HeadTopMesh,
        SemiFunc.CosmeticType.HeadBottom,
        SemiFunc.CosmeticType.HeadBottomMesh,
        SemiFunc.CosmeticType.Ears,
        SemiFunc.CosmeticType.Eyewear,
        SemiFunc.CosmeticType.FaceTop,
        SemiFunc.CosmeticType.FaceBottom,
        SemiFunc.CosmeticType.BodyTop,
        SemiFunc.CosmeticType.BodyTopMesh,
        SemiFunc.CosmeticType.BodyBottom,
        SemiFunc.CosmeticType.BodyBottomMesh,
        SemiFunc.CosmeticType.ArmRight,
        SemiFunc.CosmeticType.ArmLeft,
        SemiFunc.CosmeticType.LegRight,
        SemiFunc.CosmeticType.LegLeft,
        SemiFunc.CosmeticType.FootRight,
        SemiFunc.CosmeticType.FootLeft,
    };

    // Player poses, in enum order, with user-facing labels.
    private static readonly (PlayerAvatarVisuals.Pose Pose, string Label)[] HidePoses =
    {
        (PlayerAvatarVisuals.Pose.Stand, "Standing"),
        (PlayerAvatarVisuals.Pose.Crouch, "Crouching"),
        (PlayerAvatarVisuals.Pose.Crawl, "Crawling"),
        (PlayerAvatarVisuals.Pose.Tumble, "Tumbling"),
    };

    private static readonly Dictionary<SemiFunc.CosmeticType, string> TypeLabels = new()
    {
        [SemiFunc.CosmeticType.Hat] = "Hat",
        [SemiFunc.CosmeticType.HeadTopMesh] = "Head Top (Hair)",
        [SemiFunc.CosmeticType.HeadBottom] = "Head Bottom",
        [SemiFunc.CosmeticType.HeadBottomMesh] = "Head Bottom Mesh",
        [SemiFunc.CosmeticType.Ears] = "Ears",
        [SemiFunc.CosmeticType.Eyewear] = "Eyewear",
        [SemiFunc.CosmeticType.FaceTop] = "Face Top",
        [SemiFunc.CosmeticType.FaceBottom] = "Face Bottom",
        [SemiFunc.CosmeticType.BodyTop] = "Body Top",
        [SemiFunc.CosmeticType.BodyTopMesh] = "Body Top Mesh",
        [SemiFunc.CosmeticType.BodyBottom] = "Body Bottom",
        [SemiFunc.CosmeticType.BodyBottomMesh] = "Body Bottom Mesh",
        [SemiFunc.CosmeticType.ArmRight] = "Arm Right",
        [SemiFunc.CosmeticType.ArmLeft] = "Arm Left",
        [SemiFunc.CosmeticType.LegRight] = "Leg Right",
        [SemiFunc.CosmeticType.LegLeft] = "Leg Left",
        [SemiFunc.CosmeticType.FootRight] = "Foot Right",
        [SemiFunc.CosmeticType.FootLeft] = "Foot Left",
    };

    /// Opens the Hide Conditions page over the main popup; the config is mutated directly and persisted by the caller's Save.
    /// worldMode: body-shape conditions don't apply, and the cosmetic's own type is no obstacle to any trigger.
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(
        CosmeticHideConfig config,
        SemiFunc.CosmeticType cosmeticType,
        Action? onPreview = null,
        Transform? parentPopupTransform = null,
        bool worldMode = false)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(
            config, cosmeticType, onPreview, parentPopupTransform, worldMode));

    private static void ShowNow(
        CosmeticHideConfig config,
        SemiFunc.CosmeticType cosmeticType,
        Action? onPreview,
        Transform? parentPopupTransform,
        bool worldMode)
    {
        config.WhenTypes ??= new List<SemiFunc.CosmeticType>();
        config.WhenConditions ??= new List<CosmeticCustomCondition.Type>();
        config.WhenPoses ??= new List<PlayerAvatarVisuals.Pose>();

        // Snapshot for Cancel.
        var snapTypes = new List<SemiFunc.CosmeticType>(config.WhenTypes);
        var snapConds = new List<CosmeticCustomCondition.Type>(config.WhenConditions);
        var snapPoses = new List<PlayerAvatarVisuals.Pose>(config.WhenPoses);

        var conditions = worldMode
            ? Array.Empty<CosmeticCustomCondition.Type>()
            : CosmeticConditionsPopup.ValidCustomTypes(cosmeticType);

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: "Hide Conditions",
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        // ── Section: Hide when equipped ───────────────────────────────────────
        AddLabel(popup, "Hide when equipped:", TitleGap);
        bool firstType = true;
        foreach (var t in HideWhenTypes)
        {
            // Don't offer the cosmetic's own type as a trigger (it would always hide it). World cosmetics aren't really a Hat (synthetic mapping), so nothing is skipped.
            if (!worldMode && t == cosmeticType) continue;
            var captured = t;
            string label = TypeLabels.TryGetValue(captured, out var l) ? l : captured.ToString();
            bool isOn = config.WhenTypes.Contains(captured);
            bool isFirst = firstType; firstType = false;

            popup.AddElementToScrollView(scrollView =>
            {
                var toggle = MenuAPI.CreateREPOToggle(
                    text: label,
                    onToggle: on =>
                    {
                        if (on) { if (!config.WhenTypes!.Contains(captured)) config.WhenTypes!.Add(captured); }
                        else config.WhenTypes!.Remove(captured);
                        onPreview?.Invoke();
                    },
                    parent: scrollView,
                    defaultValue: isOn);
                return toggle.rectTransform;
            }, topPadding: isFirst ? 6f : 0f);
        }

        // ── Section: Hide when condition (only when the type has relevant conditions) ──
        if (conditions.Count > 0)
        {
            AddLabel(popup, "Hide when condition:", BtnTopGap);
            bool firstCond = true;
            foreach (var c in conditions)
            {
                var captured = c;
                string label = CosmeticConditionFormat.Label(captured);
                bool isOn = config.WhenConditions.Contains(captured);
                bool isFirst = firstCond; firstCond = false;

                popup.AddElementToScrollView(scrollView =>
                {
                    var toggle = MenuAPI.CreateREPOToggle(
                        text: label,
                        onToggle: on =>
                        {
                            if (on) { if (!config.WhenConditions!.Contains(captured)) config.WhenConditions!.Add(captured); }
                            else config.WhenConditions!.Remove(captured);
                            onPreview?.Invoke();
                        },
                        parent: scrollView,
                        defaultValue: isOn);
                    return toggle.rectTransform;
                }, topPadding: isFirst ? 6f : 0f);
            }
        }

        // ── Section: Hide when pose (player body state; meaningless for world displays) ──
        if (!worldMode)
        {
            AddLabel(popup, "Hide when pose:", BtnTopGap);
            bool firstPose = true;
            foreach (var (pose, label) in HidePoses)
            {
                var captured = pose;
                bool isOn = config.WhenPoses.Contains(captured);
                bool isFirst = firstPose; firstPose = false;

                popup.AddElementToScrollView(scrollView =>
                {
                    var toggle = MenuAPI.CreateREPOToggle(
                        text: label,
                        onToggle: on =>
                        {
                            if (on) { if (!config.WhenPoses!.Contains(captured)) config.WhenPoses!.Add(captured); }
                            else config.WhenPoses!.Remove(captured);
                            onPreview?.Invoke();
                        },
                        parent: scrollView,
                        defaultValue: isOn);
                    return toggle.rectTransform;
                }, topPadding: isFirst ? 6f : 0f);
            }
        }

        // ── Section: Hide with (read-only) ────────────────────────────────────
        // Specific-cosmetic rules absorbed from a modded cosmetic's cosmeticList (no picker to edit yet).
        if (config.WhenCosmetics is { Count: > 0 })
        {
            AddLabel(popup, "Hide with (from mod):", BtnTopGap);
            foreach (var name in config.WhenCosmetics)
                AddLabel(popup, "• " + Friendly(name), 0f);
        }

        // ── Buttons: [Cancel] [Done] ─────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                config.WhenTypes!.Clear(); config.WhenTypes.AddRange(snapTypes);
                config.WhenConditions!.Clear(); config.WhenConditions.AddRange(snapConds);
                config.WhenPoses!.Clear(); config.WhenPoses.AddRange(snapPoses);
                onPreview?.Invoke();
                popup.ClosePage(false);
            }, row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Done", () => popup.ClosePage(false),
                row, new Vector2(BtnDoneX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        popup.OpenPage(openOnTop: true);
    }

    // Tidies a CosmeticAsset.name for display (drops the authoring "Cosmetic - " prefix and any "(Clone)").
    private static string Friendly(string name)
    {
        string s = name.Replace("(Clone)", "").Trim();
        if (s.StartsWith("Cosmetic - ", StringComparison.OrdinalIgnoreCase)) s = s.Substring("Cosmetic - ".Length);
        return s;
    }

    private static void AddLabel(MenuLib.MonoBehaviors.REPOPopupPage popup, string text, float topPadding)
    {
        popup.AddElementToScrollView(scrollView =>
        {
            var label = MenuAPI.CreateREPOLabel(text, scrollView, Vector2.zero);
            return label.rectTransform;
        }, topPadding: topPadding);
    }
}
