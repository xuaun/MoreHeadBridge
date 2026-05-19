// ============================================================================
// In-game popup for per-cosmetic rarity / category overrides.
// Opened via Shift+click on a bridge cosmetic.
//
// UI structure (top → bottom):
//   • Main Category  — Head / Body / Arms / Legs / World
//   • Sub Category   — dynamically updated in-place when Main changes;
//                      hidden (no gap) when Main = World
//   • Modded Rarity  — Default / Yes / No
//   • Rarity         — Common / Uncommon / Rare / UltraRare
//   • [BACK]   [SAVE]        ← side-by-side row
//   •         [RESET]        ← right-aligned, only when override exists
//
// Changing Main Category swaps the Sub Category slider's options in-place via
// REPOSlider.stringOptions + REPOScrollViewElement.visibility — the popup never
// closes or reopens, so there is no animation artifact.
// ============================================================================

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticOverridePopup
{
    // ── Layout constants ──────────────────────────────────────────────────────
    // Horizontal position of the popup.
    private const float PopupX    = -120f;
    // Extra gap between the popup title and the first scroll element.
    private const float TitleGap  =   15f;
    // Height of each button row container (should match the button template height).
    private const float BtnRowH   =   30f;
    // Extra gap above the first button row (separates sliders from buttons).
    private const float BtnTopGap =   10f;

    // X positions for the three buttons (finalized via in-game tuning).
    private const float BtnBackX  = -137f;
    private const float BtnSaveX  =   58f;
    private const float BtnResetX =   51f;
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] RarityOptions =
        Enum.GetNames(typeof(SemiFunc.Rarity));       // Common, Uncommon, Rare, UltraRare

    private static readonly string[] MainOptions =
        Enum.GetNames(typeof(MainCosmeticCategory));  // Head, Body, Arms, Legs, World

    // Shown in the "Modded Rarity" slider; maps to bool? (null / true / false).
    private static readonly string[] ModdedOptions = { "Default", "Yes", "No" };

    // Sub-category types per main group, in display order (overlays excluded).
    private static readonly Dictionary<MainCosmeticCategory, OverrideCosmeticType[]> SubOptions = new()
    {
        [MainCosmeticCategory.Head]  = new[]
        {
            OverrideCosmeticType.Hat,
            OverrideCosmeticType.Eyewear,
            OverrideCosmeticType.FaceTop,
            OverrideCosmeticType.FaceBottom,
            OverrideCosmeticType.HeadBottom,
            OverrideCosmeticType.Ears,
        },
        [MainCosmeticCategory.Body]  = new[]
        {
            OverrideCosmeticType.BodyTop,
            OverrideCosmeticType.BodyBottom,
        },
        [MainCosmeticCategory.Arms]  = new[]
        {
            OverrideCosmeticType.ArmRight,
            OverrideCosmeticType.ArmLeft,
        },
        [MainCosmeticCategory.Legs]  = new[]
        {
            OverrideCosmeticType.LegRight,
            OverrideCosmeticType.LegLeft,
            OverrideCosmeticType.FootRight,
            OverrideCosmeticType.FootLeft,
        },
        [MainCosmeticCategory.World] = new[] { OverrideCosmeticType.World },
    };

    // Human-readable display label for each sub-category type.
    private static readonly Dictionary<OverrideCosmeticType, string> SubLabels = new()
    {
        [OverrideCosmeticType.Hat]          = "Hat",
        [OverrideCosmeticType.Eyewear]      = "Eyewear",
        [OverrideCosmeticType.FaceTop]      = "Face Upper",
        [OverrideCosmeticType.FaceBottom]   = "Face Middle",
        [OverrideCosmeticType.HeadBottom]   = "Face Lower",
        [OverrideCosmeticType.Ears]         = "Ears",
        [OverrideCosmeticType.BodyTop]      = "Bodywear Top",
        [OverrideCosmeticType.BodyBottom]   = "Bodywear Bottom",
        [OverrideCosmeticType.ArmRight]     = "Armwear Right",
        [OverrideCosmeticType.ArmLeft]      = "Armwear Left",
        [OverrideCosmeticType.LegRight]     = "Legwear Right",
        [OverrideCosmeticType.LegLeft]      = "Legwear Left",
        [OverrideCosmeticType.FootRight]    = "Footwear Right",
        [OverrideCosmeticType.FootLeft]     = "Footwear Left",
        [OverrideCosmeticType.World]        = "World",
    };

    // Reverse-lookup: display label → OverrideCosmeticType (used in sub-slider onChange).
    private static readonly Dictionary<string, OverrideCosmeticType> LabelToType =
        SubLabels.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    /// Opens the override popup for the given bridge cosmetic.
    internal static void Show(CosmeticAsset asset)
    {
        bool hasOverride  = PerCosmeticOverrides.HasOverride(asset);
        string displayName = asset.assetName ?? asset.name ?? asset.assetId;

        // ── Mutable pending state (captured by closures below) ────────────────
        PerCosmeticOverrides.TryGet(asset.assetId, out var existing);
        bool? pendingModded          = existing?.IsModded;
        SemiFunc.Rarity      pendingRarity = asset.rarity;
        MainCosmeticCategory pendingMain   = PerCosmeticOverrides.GetCurrentMain(asset);
        OverrideCosmeticType pendingType   = PerCosmeticOverrides.GetCurrentType(asset);

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText:           displayName,
            shouldCachePage:      false,
            pageDimmerVisibility: true,
            spacing:              5f,
            localPosition:        new Vector2(PopupX, 0f));

        // ── Sub Category slider — declared before Main so the Main onChange can
        //    reference it. AddElementToScrollView runs its callback synchronously,
        //    so subSlider is guaranteed non-null before any user interaction fires.
        REPOSlider? subSlider = null;

        // ── Main Category slider ──────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text:            "Main Category",
                description:     "",
                onOptionChanged: (string opt) =>
                {
                    if (!Enum.TryParse(opt, out MainCosmeticCategory newMain)) return;
                    if (newMain == pendingMain) return;

                    pendingMain = newMain;
                    pendingType = SubOptions[newMain][0];

                    if (subSlider == null) return;
                    var subElement = subSlider.GetComponent<REPOScrollViewElement>();

                    if (newMain == MainCosmeticCategory.World)
                    {
                        if (subElement != null) subElement.visibility = false;
                    }
                    else
                    {
                        subSlider.stringOptions = GetSubLabels(newMain);
                        subSlider.SetValue(0, invokeCallback: false);
                        if (subElement != null) subElement.visibility = true;
                    }
                },
                parent:         scrollView,
                stringOptions:  MainOptions,
                defaultOption:  pendingMain.ToString());
            return (RectTransform)slider.transform;
        }, topPadding: TitleGap);

        // ── Sub Category slider ───────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var initialGroup  = pendingMain == MainCosmeticCategory.World
                ? MainCosmeticCategory.Head : pendingMain;
            var initialLabels = GetSubLabels(initialGroup);

            // If the saved type is not in SubOptions for this group (e.g. overlay type
            // from an older save), fall back to the first option gracefully.
            bool typeInGroup = Array.IndexOf(SubOptions[initialGroup], pendingType) >= 0;
            string defaultSub = typeInGroup && SubLabels.ContainsKey(pendingType)
                ? SubLabels[pendingType] : initialLabels[0];

            subSlider = MenuAPI.CreateREPOSlider(
                text:            "Sub Category",
                description:     "",
                onOptionChanged: (string opt) =>
                {
                    if (LabelToType.TryGetValue(opt, out var t))
                        pendingType = t;
                },
                parent:         scrollView,
                stringOptions:  initialLabels,
                defaultOption:  defaultSub);

            return (RectTransform)subSlider.transform;
        });

        // Callback above ran synchronously — subSlider is populated. Hide if World.
        if (pendingMain == MainCosmeticCategory.World)
        {
            var el = subSlider?.GetComponent<REPOScrollViewElement>();
            if (el != null) el.visibility = false;
        }

        // ── Modded Rarity slider ──────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text:            "Modded Rarity",
                description:     "",
                onOptionChanged: (string opt) =>
                {
                    pendingModded = opt switch
                    {
                        "Yes" => true,
                        "No"  => false,
                        _     => (bool?)null,
                    };
                },
                parent:         scrollView,
                stringOptions:  ModdedOptions,
                defaultOption:  pendingModded switch { true => "Yes", false => "No", _ => "Default" });
            return (RectTransform)slider.transform;
        });

        // ── Rarity slider ─────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text:            "Rarity",
                description:     "",
                onOptionChanged: (string opt) =>
                {
                    if (Enum.TryParse(opt, out SemiFunc.Rarity r))
                        pendingRarity = r;
                },
                parent:         scrollView,
                stringOptions:  RarityOptions,
                defaultOption:  pendingRarity.ToString());
            return (RectTransform)slider.transform;
        });

        // ── Row: [BACK]  [SAVE] ──────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Back", () => popup.ClosePage(false),
                row, new Vector2(BtnBackX, 0f));

            MenuAPI.CreateREPOButton("Save", () =>
            {
                PerCosmeticOverrides.SetAndApply(asset, pendingModded, pendingRarity, pendingType);
                RefreshMenu();
                popup.ClosePage(false);
            }, row, new Vector2(BtnSaveX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        // ── Row: [RESET] (right-aligned, only when override exists) ──────────
        if (hasOverride)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = MakeRow(scrollView);

                MenuAPI.CreateREPOButton("Reset", () =>
                {
                    PerCosmeticOverrides.Reset(asset);
                    RefreshMenu();
                    popup.ClosePage(false);
                }, row, new Vector2(BtnResetX, 0f));

                return row;
            }, topPadding: 5f);
        }

        // openOnTop: false → sets the cosmetics menu to Inactive while the popup is open,
        // blocking all clicks behind it. MenuLib restores it when ClosePage is called.
        popup.OpenPage(openOnTop: false);
    }

    // Returns the display-label array for the sub-category slider for the given main group.
    private static string[] GetSubLabels(MainCosmeticCategory main)
        => Array.ConvertAll(SubOptions[main], t => SubLabels[t]);

    // Creates a bare RectTransform container sized for one button row, parented to the
    // scroll view. Buttons are added as children with manual localPosition offsets.
    private static RectTransform MakeRow(Transform scrollView)
    {
        var row = new GameObject("Button Row", typeof(RectTransform))
            .GetComponent<RectTransform>();
        row.SetParent(scrollView, false);
        row.sizeDelta = new Vector2(0f, BtnRowH);
        return row;
    }

    private static void RefreshMenu()
    {
        CosmeticsMenuState.ActivePage?.RefreshScrollContent();
    }
}
