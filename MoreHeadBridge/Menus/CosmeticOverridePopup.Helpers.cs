// Small formatters, section-label builders, and confirm/equality helpers for CosmeticOverridePopup (partial — see CosmeticOverridePopup.cs for the popup builder).

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static partial class CosmeticOverridePopup
{
    // Converts a TriStateOptions string to bool? (null=Default, true=Yes, false=No).
    private static bool? TriStateToBool(string opt) => opt switch
    {
        "Yes" => true,
        "No" => false,
        _ => (bool?)null,
    };

    private static string BoolToTriState(bool? v) => v switch
    {
        true => "Yes",
        false => "No",
        _ => "Default",
    };

    private static string SwayModeToOption(SwayMode? mode) => mode switch
    {
        SwayMode.None => "No",
        SwayMode.Light => "Light",
        SwayMode.Moderate => "Moderate",
        SwayMode.Strong => "Strong",
        _ => "Default",
    };

    private static SwayMode? OptionToSwayMode(string opt) => opt switch
    {
        "No" => SwayMode.None,
        "Light" => SwayMode.Light,
        "Moderate" => SwayMode.Moderate,
        "Strong" => SwayMode.Strong,
        _ => (SwayMode?)null,
    };

    private static string RarityToOption(SemiFunc.Rarity? rarity)
        => rarity?.ToString() ?? "Default";

    private static string EquipAnimToOption(VanillaEquipAnimationMode? mode)
        => mode switch
        {
            VanillaEquipAnimationMode.Fixed => "Fixed",
            VanillaEquipAnimationMode.Normal => "Normal",
            VanillaEquipAnimationMode.Disabled => "Disabled",
            _ => "Default",
        };

    private static VanillaEquipAnimationMode? EquipAnimToValue(string opt)
        => opt switch
        {
            "Fixed" => VanillaEquipAnimationMode.Fixed,
            "Normal" => VanillaEquipAnimationMode.Normal,
            "Disabled" => VanillaEquipAnimationMode.Disabled,
            _ => (VanillaEquipAnimationMode?)null,
        };

    private static string[] GetSubLabels(MainCosmeticCategory main)
        => Array.ConvertAll(SubOptions[main], t => SubLabels[t]);

    private static void AddSectionLabel(REPOPopupPage popup, string text, float topPadding)
        => AddSectionLabelRow(popup, text, topPadding);

    // Same as AddSectionLabel but returns the row transform so its REPOScrollViewElement can be captured and toggled (used for the type-dependent "World" section header).
    private static RectTransform AddSectionLabelRow(REPOPopupPage popup, string text, float topPadding)
    {
        RectTransform? rt = null;
        popup.AddElementToScrollView(scrollView =>
        {
            var label = MenuAPI.CreateREPOLabel(text, scrollView);
            label.labelTMP.fontSize = 18f;
            label.labelTMP.alpha = 0.85f;
            label.labelTMP.alignment = TMPro.TextAlignmentOptions.Left;
            label.rectTransform.sizeDelta = new Vector2(200f, 24f);
            return rt = (RectTransform)label.transform;
        }, topPadding: topPadding);
        return rt!;
    }

    // Cosmetic types that author an Impact Pose (CosmeticBlocked) in vanilla — the 7 regions where the reaction occurs (protruding gear). The Impact Pose button is shown only for these.
    private static readonly HashSet<SemiFunc.CosmeticType> ImpactPoseTypes = new()
    {
        SemiFunc.CosmeticType.Hat,
        SemiFunc.CosmeticType.Ears,
        SemiFunc.CosmeticType.BodyTop,
        SemiFunc.CosmeticType.FaceBottom,
        SemiFunc.CosmeticType.HeadBottom,
        SemiFunc.CosmeticType.Eyewear,
        SemiFunc.CosmeticType.BodyBottomMesh,
    };

    private static void RefreshMenu()
    {
        CosmeticsMenuState.ActivePage?.RefreshScrollContent();
    }

    // Confirmation before saving a type change that would drop offsets/conditions/crown no longer valid for the new type. onConfirm runs the prune + actual save; Cancel aborts.
    private static void ShowPruneConfirm(int offsets, int conds, bool crown, Action onConfirm)
    {
        var parts = new List<string>();
        if (offsets > 0) parts.Add($"{offsets} offset(s)");
        if (conds > 0) parts.Add($"{conds} condition(s)");
        if (crown) parts.Add("the crown");
        string msg = "Changing type removes:\n" + string.Join(", ", parts);

        var popup = MenuAPI.CreateREPOPopupPage("Change type?", shouldCachePage: false,
            pageDimmerVisibility: true, spacing: 5f, localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, inputGuard: false);

        popup.AddElementToScrollView(scrollView =>
            (RectTransform)MenuAPI.CreateREPOLabel(msg, scrollView).transform, topPadding: TitleGap);

        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Cancel", () => popup.ClosePage(false),
                row, new Vector2(BtnBackX, 0f));
            MenuAPI.CreateREPOButton("Confirm", () => { popup.ClosePage(false); onConfirm(); },
                row, new Vector2(BtnSaveX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        popup.OpenPage(openOnTop: true);
    }
}
