// In-game per-cosmetic customizer popup (Shift+click a bridge cosmetic): category, highlight, sway, rarity and more. Changing the main category swaps the sub-slider options in-place, so the popup never closes/reopens.

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
    // ── Layout constants ──────────────────────────────────────────────────────
    // Horizontal position of the popup.
    private const float PopupX = -120f;
    // Extra gap between the popup title and the first scroll element.
    private const float TitleGap = 15f;
    // Height of each button row container (should match the button template height).
    private const float BtnRowH = 30f;
    // Extra gap above the first button row (separates sliders from buttons).
    private const float BtnTopGap = 10f;

    // X positions for the buttons (finalized via in-game tuning).
    private const float BtnBackX = -137f;
    private const float BtnSaveX = 58f;
    private const float BtnResetX = 51f;
    private const float BtnDeleteIconX = BtnBackX; // left column, aligned with Back
    private const float BtnExportX = -23f; // right-side export button
    private const float BtnConditionX = BtnBackX; // position for Condition Trigger button
    private const float BtnOffsetX = BtnBackX;    // position for Special Position Fixes button
    private const float BtnCrownX = BtnBackX;     // position for Crown Settings button
    // ─────────────────────────────────────────────────────────────────────────


    /// Opens the override popup for the given bridge cosmetic.
    // Build deferred to mouse-release: a fresh page is ACTIVE before OpenPage, so a REPOSlider under the held click gets scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(CosmeticAsset asset)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(asset));

    private static void ShowNow(CosmeticAsset asset)
    {
        // The synthetic Mini-Semibot isn't a mesh cosmetic — route it to its own purpose-built popup.
        if (asset.assetId == MiniSemibotCosmetic.AssetId)
        {
            MiniSemibotOverridePopup.Show(asset);
            return;
        }

        bool hasOverride = CustomizerStore.HasOverride(asset);
        bool hasCachedIcon = IconCapture.HasCache(asset);
        string displayName = asset.assetName ?? asset.name ?? asset.assetId;

        // ── Mutable pending state (captured by closures below) ────────────────
        CustomizerStore.TryGet(asset.assetId, out var existing);
        bool? pendingModded = existing?.IsModded;
        SemiFunc.Rarity? pendingRarity = existing?.Rarity;
        MainCosmeticCategory pendingMain = CustomizerStore.GetCurrentMain(asset);
        OverrideCosmeticType pendingType = CustomizerStore.GetEffectiveType(asset);
        bool? pendingFixCollider = existing?.FixCollider;
        bool? pendingFixAnimation = existing?.FixAnimation;
        bool? pendingFixCrown = existing?.FixCrown;
        VanillaEquipAnimationMode? pendingEquipAnim = existing?.VanillaEquipAnimationMode;
        bool? pendingTintable = existing?.Tintable;
        bool? pendingCustomColors = existing?.EnableCustomColors;
        bool? pendingColorAnimations = existing?.EnableColorAnimations;
        bool? pendingIsolatedIcon = existing?.UseIsolatedIcon;
        bool? pendingUseFit = existing?.UseFitOffsets;
        SwayMode? pendingEnableSway = existing?.EnableSway;
        // Shared by reference with CosmeticConditionsPopup — mutations are immediately visible here.
        var pendingCustomTypes = new HashSet<CosmeticCustomCondition.Type>(
            existing?.CustomTypes ?? System.Linq.Enumerable.Empty<CosmeticCustomCondition.Type>());
        // Non-bridge modded: surface the cosmetic's own native shape conditions as editable toggles (no-op for bridge).
        NativeCustomTypeImport.MergeIntoPending(asset, pendingCustomTypes);
        // Shared by reference with CosmeticOffsetPopup.
        var pendingOffsets = new List<CosmeticOffsetEntry>(
            existing?.Offsets ?? System.Linq.Enumerable.Empty<CosmeticOffsetEntry>());
        // Non-bridge modded: surface the cosmetic's own native offsets as editable entries (no-op for bridge).
        NativeOffsetImport.MergeIntoPending(asset, pendingOffsets);
        // Crown config — only relevant for Hat/HeadTopMesh types.
        CosmeticCrownConfig? pendingCrown = existing?.Crown?.Clone();
        // Non-bridge modded: surface the cosmetic's own native crown target as a starting config (no-op for bridge / if already overridden).
        NativeCrownImport.MergeIntoPending(asset, ref pendingCrown);
        // Death-head visibility — null (or true) = shown, false = hidden on the death head.
        bool? pendingShowOnDeathHead = existing?.ShowOnDeathHead;
        // Death-head floor pose (React to Floor + transform); cloned so edits persist only on Save.
        DeathHeadFloorPose? pendingFloorPose = existing?.FloorPose?.Clone();
        // Hide-self rules (mutated by reference inside CosmeticHidePopup; empty → null on save).
        CosmeticHideConfig pendingHide = existing?.HideConditions?.Clone() ?? new CosmeticHideConfig();
        // Non-bridge modded: surface the cosmetic's own native hide rules as editable toggles (no-op for bridge).
        NativeHideImport.MergeIntoPending(asset, pendingHide);
        // Blacklist (bridge + hidden only): committed on Save, load-time effect.
        BridgeFavoritesManager.EnsureLoaded();
        bool pendingBlacklist = BridgeBlacklist.Contains(asset.assetId);

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: displayName,
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        // ── Live preview avatar ───────────────────────────────────────────────
        // Created before the first slider so Init() can RefreshFull() with the saved state; floats left of the popup.
        var preview = popup.gameObject.AddComponent<CosmeticOverridePreview>();
        preview.Init(asset, popup);

        // ADD-OVERRIDE-FIELD: surface the new field in the popup UI and wire it into BuildPending (and DoSave) here.
        CosmeticOverrideData BuildPending() => new CosmeticOverrideData
        {
            Type = pendingType,
            Offsets = pendingOffsets.Count > 0
                                ? new List<CosmeticOffsetEntry>(pendingOffsets) : null,
            CustomTypes = pendingCustomTypes.Count > 0
                                ? new List<CosmeticCustomCondition.Type>(pendingCustomTypes) : null,
            Crown = pendingCrown,
            EnableSway = pendingEnableSway,
            FixCollider = pendingFixCollider,
            FixAnimation = pendingFixAnimation,
            FixCrown = pendingFixCrown,
            VanillaEquipAnimationMode = pendingEquipAnim,
            ShowOnDeathHead = pendingShowOnDeathHead,
            FloorPose = pendingFloorPose,
            HideConditions = pendingHide.HasAny ? pendingHide : null,
            EnableCustomColors = pendingCustomColors,
            EnableColorAnimations = pendingColorAnimations,
            UseIsolatedIcon = pendingIsolatedIcon,
            UseFitOffsets = pendingUseFit,
        };

        // Full re-instantiation preview (used for category, type, fix, and equip-anim changes).
        void PreviewFull() => preview.RefreshFull(BuildPending());

        // Stop the popup's scroll leaking into outside scroll views (disables outer ScrollRects, restored later).
        PopupUI.AttachGuards(popup);

        // Sub Category slider — declared before Main so its onChange can reference it (AddElementToScrollView runs synchronously, so subSlider is non-null first).
        REPOSlider? subSlider = null;

        // Shape Conditions row — hidden for World and for types with no relevant conditions; tracks Main/Sub live.
        REPOScrollViewElement? shapeCondEl = null;
        void UpdateShapeCondRow()
        {
            if (shapeCondEl == null) return;
            var (cosType, isWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
            shapeCondEl.visibility = !isWorld && CosmeticConditionsPopup.HasConditions(cosType);
        }

        // Type-dependent rows toggled live as Main/Sub change (built once) so no save+reopen: World opts, Crown (Hat/HeadTopMesh), Death Head, Impact Pose; all hidden for World.
        REPOScrollViewElement? worldLabelEl = null, worldShowSelfEl = null, worldAvoidEl = null,
                               worldHideKartEl = null, worldSpringEl = null, crownEl = null,
                               deathHeadEl = null, impactEl = null, fitEl = null;
        static void SetVis(REPOScrollViewElement? el, bool v) { if (el != null) el.visibility = v; }
        void UpdateConditionalRows()
        {
            UpdateShapeCondRow();
            var (cosType, isWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
            bool world = isWorld || pendingMain == MainCosmeticCategory.World;
            SetVis(worldLabelEl, world);
            SetVis(worldShowSelfEl, world);
            SetVis(worldAvoidEl, world);
            SetVis(worldHideKartEl, world);
            SetVis(worldSpringEl, world);
            SetVis(crownEl, !world && (pendingType == OverrideCosmeticType.Hat
                                       || pendingType == OverrideCosmeticType.HeadTopMesh));
            SetVis(deathHeadEl, !world && DeathHeadPrefabProvider.SupportedTypes.Contains(cosType));
            SetVis(impactEl, !world && ImpactPoseTypes.Contains(cosType));
            // "Vanilla Position Fixes" only matters where automatic fixes exist (bridge, non-world, seeded type).
            SetVis(fitEl, !world && BridgeIds.IsBridgeAsset(asset) && OffsetSeedDefaults.HasDefaults(cosType));
        }

        AddSectionLabel(popup, "Category", TitleGap);

        // ── Main Category slider ──────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Main Category",
                description: "",
                onOptionChanged: (string opt) =>
                {
                    if (!Enum.TryParse(opt, out MainCosmeticCategory newMain)) return;
                    if (newMain == pendingMain) return;

                    pendingMain = newMain;
                    pendingType = SubOptions[newMain][0];
                    UpdateConditionalRows();

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
                    PreviewFull();
                },
                parent: scrollView,
                stringOptions: MainOptions,
                defaultOption: pendingMain.ToString());
            return (RectTransform)slider.transform;
        }, topPadding: BtnTopGap);

        // ── Sub Category slider ───────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var initialGroup = pendingMain == MainCosmeticCategory.World
                ? MainCosmeticCategory.Head : pendingMain;
            var initialLabels = GetSubLabels(initialGroup);

            // If the saved type isn't in SubOptions for this group (e.g. overlay type from an older save), fall back to the first option gracefully.
            bool typeInGroup = Array.IndexOf(SubOptions[initialGroup], pendingType) >= 0;
            string defaultSub = typeInGroup && SubLabels.ContainsKey(pendingType)
                ? SubLabels[pendingType] : initialLabels[0];

            subSlider = MenuAPI.CreateREPOSlider(
                text: "Sub Category",
                description: "",
                onOptionChanged: (string opt) =>
                {
                    if (LabelToType.TryGetValue(opt, out var t))
                        pendingType = t;
                    UpdateConditionalRows();
                    PreviewFull();
                },
                parent: scrollView,
                stringOptions: initialLabels,
                defaultOption: defaultSub);

            return (RectTransform)subSlider.transform;
        });

        // Callback above ran synchronously — subSlider is populated. Hide if World.
        if (pendingMain == MainCosmeticCategory.World)
        {
            var el = subSlider?.GetComponent<REPOScrollViewElement>();
            if (el != null) el.visibility = false;
        }

        // ── World options ─────────────────────────────────────────────────────
        // Built always so switching to World shows them live; visibility toggled by UpdateConditionalRows. Prefs keyed by assetId.
        {
            worldLabelEl = AddSectionLabelRow(popup, "World", BtnTopGap)
                .GetComponent<REPOScrollViewElement>();

            // "Show To Self": whether YOU also see this world cosmetic in game. Default off (MoreHead parity); read live by the follower.
            RectTransform? showSelfRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Show To Self (In Game)",
                    description: "",
                    onOptionChanged: (string opt) =>
                    {
                        WorldFollowPrefs.SetShowToSelf(asset.assetId, opt == "On");
                    },
                    parent: scrollView,
                    stringOptions: new[] { "Off", "On" },
                    defaultOption: WorldFollowPrefs.GetShowToSelf(asset.assetId) ? "On" : "Off");
                return showSelfRow = (RectTransform)slider.transform;
            }, topPadding: BtnTopGap);
            worldShowSelfEl = showSelfRow != null ? showSelfRow.GetComponent<REPOScrollViewElement>() : null;

            // "Avoid Walls" (world cosmetics): owner-authoritative — YOUR choice shows on everyone's screen (rides the override sync payload).
            RectTransform? avoidRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Avoid Walls",
                    description: "",
                    onOptionChanged: (string opt) =>
                    {
                        WorldFollowPrefs.SetAvoidWalls(asset.assetId, opt == "On");
                        CustomizerSync.BroadcastAll();   // owner-authoritative → broadcast
                    },
                    parent: scrollView,
                    stringOptions: new[] { "Off", "On" },
                    defaultOption: WorldFollowPrefs.GetAvoidWalls(asset.assetId) ? "On" : "Off");
                return avoidRow = (RectTransform)slider.transform;
            }, topPadding: BtnTopGap);
            worldAvoidEl = avoidRow != null ? avoidRow.GetComponent<REPOScrollViewElement>() : null;

            // "Hide on Kart" (world): owner-authoritative like Avoid Walls — hides on everyone's screen while on a vehicle / the kart arena.
            RectTransform? hideKartRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Hide on Kart",
                    description: "",
                    onOptionChanged: (string opt) =>
                    {
                        WorldFollowPrefs.SetHideOnKart(asset.assetId, opt == "On");
                        CustomizerSync.BroadcastAll();   // owner-authoritative → broadcast
                    },
                    parent: scrollView,
                    stringOptions: new[] { "Off", "On" },
                    defaultOption: WorldFollowPrefs.GetHideOnKart(asset.assetId) ? "On" : "Off");
                return hideKartRow = (RectTransform)slider.transform;
            }, topPadding: BtnTopGap);
            worldHideKartEl = hideKartRow != null ? hideKartRow.GetComponent<REPOScrollViewElement>() : null;
        }

        // ── World follow spring ───────────────────────────────────────────────
        // Follow-spring feel (gated by EnableWorldFollowSpring). LOCAL, read live; defaults to Soft. Built always; shown only for World.
        if (Plugin.EnableWorldFollowSpring.Value)
        {
            RectTransform? springRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Follow Smoothing",
                    description: "",
                    onOptionChanged: (string opt) =>
                    {
                        WorldFollowPrefs.SetSpring(asset.assetId, opt switch
                        {
                            "Soft"   => FollowSpringMode.Soft,
                            "Bouncy" => FollowSpringMode.Springy,
                            _        => FollowSpringMode.Off,
                        });
                    },
                    parent: scrollView,
                    stringOptions: new[] { "Off", "Soft", "Bouncy" },
                    defaultOption: WorldFollowPrefs.GetSpring(asset.assetId) switch
                    {
                        FollowSpringMode.Soft    => "Soft",
                        FollowSpringMode.Springy => "Bouncy",
                        _                        => "Off",
                    });
                return springRow = (RectTransform)slider.transform;
            }, topPadding: BtnTopGap);
            worldSpringEl = springRow != null ? springRow.GetComponent<REPOScrollViewElement>() : null;
        }

        AddSectionLabel(popup, "Appearance", BtnTopGap);

        // ── Border Highlight slider ───────────────────────────────────────────
        // Bridge uses the orange "bridge" highlight; modded non-bridge uses the purple "modded" one — label accordingly.
        string borderHighlightLabel = BridgeIds.IsBridgeAsset(asset)
            ? "Bridge Border Highlight"
            : "Modded Border Highlight";
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: borderHighlightLabel,
                description: "",
                onOptionChanged: (string opt) =>
                {
                    pendingModded = TriStateToBool(opt);
                },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingModded));
            return (RectTransform)slider.transform;
        });

        // ── Rarity slider ─────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Rarity",
                description: "",
                onOptionChanged: (string opt) =>
                {
                    pendingRarity = opt == "Default"
                        ? null
                        : Enum.TryParse(opt, out SemiFunc.Rarity r) ? r : null;
                },
                parent: scrollView,
                stringOptions: RarityOptions,
                defaultOption: RarityToOption(pendingRarity));
            return (RectTransform)slider.transform;
        });

        // ── Tintable slider ───────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Allow Coloring",
                description: "",
                onOptionChanged: (string opt) => { pendingTintable = TriStateToBool(opt); },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingTintable));
            return (RectTransform)slider.transform;
        });

        // ── Allow Custom Color slider — per-cosmetic override for the RGB "C" button ──
        // Default = use the global EnableBridgeCustomColors / EnableModdedCustomColors setting.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Allow Custom Color",
                description: "",
                onOptionChanged: (string opt) => { pendingCustomColors = TriStateToBool(opt); },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingCustomColors));
            return (RectTransform)slider.transform;
        });

        // ── Allow Animated Color slider — per-cosmetic override for the animate "A" button ──
        // Bridge-only (animated colours are a bridge feature). Default = the global EnableBridgeColorAnimations setting; sits below "Allow Custom Color", mirroring it. Hidden for bridge MESH-switch cosmetics — they can't carry animated colour (cloned/baked on the death head), so it's forced off (see GetEffectiveColorAnimations).
        if (BridgeIds.IsBridgeAsset(asset) && !CustomizerStore.IsBridgeMeshSwitch(asset.assetId))
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Allow Animated Color",
                    description: "",
                    onOptionChanged: (string opt) => { pendingColorAnimations = TriStateToBool(opt); },
                    parent: scrollView,
                    stringOptions: TriStateOptions,
                    defaultOption: BoolToTriState(pendingColorAnimations));
                return (RectTransform)slider.transform;
            });
        }

        // ── Icon section — per-cosmetic isolated icon render override ──────────
        // Bridge-only: isolated render needs the self-contained .hhh prefab; non-bridge mods supply their own icons.
        if (BridgeIds.IsBridgeAsset(asset))
        {
            AddSectionLabel(popup, "Icon", BtnTopGap);
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Use Isolated Icon Render",
                    description: "",
                    onOptionChanged: (string opt) => { pendingIsolatedIcon = TriStateToBool(opt); },
                    parent: scrollView,
                    stringOptions: TriStateOptions,
                    defaultOption: BoolToTriState(pendingIsolatedIcon));
                return (RectTransform)slider.transform;
            });
        }

        // ── Blacklist toggle — bridge + already-hidden cosmetics only; commits on Save, load-time ──
        if (BridgeIds.IsBridgeAsset(asset) && BridgeFavoritesManager.IsHidden(asset))
        {
            AddSectionLabel(popup, "Blacklist", BtnTopGap);
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Add to Blacklist",
                    description: "",
                    onOptionChanged: (string opt) => { pendingBlacklist = opt == "On"; },
                    parent: scrollView,
                    stringOptions: new[] { "Off", "On" },
                    defaultOption: pendingBlacklist ? "On" : "Off");
                return (RectTransform)slider.transform;
            });
        }

        AddSectionLabel(popup, "Fixes", BtnTopGap);

        // ── Remove Physics slider ─────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Remove Physics",
                description: "",
                onOptionChanged: (string opt) => { pendingFixCollider = TriStateToBool(opt); PreviewFull(); },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingFixCollider));
            return (RectTransform)slider.transform;
        });

        // ── Loop Animation slider ─────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Loop Animation",
                description: "",
                onOptionChanged: (string opt) => { pendingFixAnimation = TriStateToBool(opt); PreviewFull(); },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingFixAnimation));
            return (RectTransform)slider.transform;
        });

        // ── Fix Crown Error slider (modded non-bridge only) ───────────────────
        // Injects an empty CosmeticPlayerCrown to silence vanilla's "has no CosmeticPlayerCrown" error on modded Hat/HeadTopMesh (bridge manage theirs via Crown Config; hidden here).
        if (!BridgeIds.IsBridgeAsset(asset))
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Fix Crown Error",
                    description: "",
                    onOptionChanged: (string opt) => { pendingFixCrown = TriStateToBool(opt); PreviewFull(); },
                    parent: scrollView,
                    stringOptions: TriStateOptions,
                    defaultOption: BoolToTriState(pendingFixCrown));
                return (RectTransform)slider.transform;
            });
        }

        AddSectionLabel(popup, "Behavior", BtnTopGap);

        // ── Jiggle Physics slider ─────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Jiggle Physics",
                description: "",
                onOptionChanged: (string opt) => { pendingEnableSway = OptionToSwayMode(opt); preview.RefreshSway(pendingEnableSway); },
                parent: scrollView,
                stringOptions: SwayOptions,
                defaultOption: SwayModeToOption(pendingEnableSway));
            return (RectTransform)slider.transform;
        });

        // ── Equip Animation slider ────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Equip Animation",
                description: "",
                onOptionChanged: (string opt) => { pendingEquipAnim = EquipAnimToValue(opt); PreviewFull(); },
                parent: scrollView,
                stringOptions: EquipAnimOptions,
                defaultOption: EquipAnimToOption(pendingEquipAnim));
            return (RectTransform)slider.transform;
        });

        AddSectionLabel(popup, "Advanced", BtnTopGap);

        // ── Vanilla Position Fixes slider — auto position/scale fixes for this cosmetic (Default/Yes/No) ─
        // Built always; shown only where fixes exist (UpdateConditionalRows). Default = global config.
        RectTransform? fitRow = null;
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Vanilla Position Fixes",
                description: "",
                onOptionChanged: (string opt) => { pendingUseFit = TriStateToBool(opt); PreviewFull(); },
                parent: scrollView,
                stringOptions: TriStateOptions,
                defaultOption: BoolToTriState(pendingUseFit));
            return fitRow = (RectTransform)slider.transform;
        }, topPadding: BtnTopGap);
        fitEl = fitRow != null ? fitRow.GetComponent<REPOScrollViewElement>() : null;

        // ── Shape Triggers button (only when the cosmetic's type has relevant conditions) ─
        // Resolves the current pendingType at click time, reflecting any Main/Sub change before clicking.
        RectTransform? shapeCondRow = null;
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Shape Conditions →", () =>
            {
                var cosType = CustomizerStore.MapOverrideToVanilla(pendingType).cosmeticType;
                if (CosmeticConditionsPopup.HasConditions(cosType))
                    CosmeticConditionsPopup.Show(
                        pendingCustomTypes, cosType,
                        onPreview: () => preview.RefreshCustomTypes(pendingCustomTypes, pendingOffsets),
                        parentPopupTransform: popup.transform);
            }, row, new Vector2(BtnConditionX, 0f));
            return shapeCondRow = row;
        }, topPadding: BtnTopGap);

        // REPOScrollViewElement is attached after the builder callback returns.
        shapeCondEl = shapeCondRow != null ? shapeCondRow.GetComponent<REPOScrollViewElement>() : null;
        UpdateShapeCondRow();

        // ── Special Position Fixes button — configure conditional position/rotation offsets ─
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Special Position Fixes →", () =>
            {
                var (cosmeticType, isWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
                CosmeticOffsetPopup.Show(
                    pendingOffsets,
                    cosmeticType,
                    onPreview: offsets => preview.RefreshOffsets(offsets),
                    parentPopupTransform: popup.transform,
                    worldMode: isWorld);
            },
                row, new Vector2(BtnOffsetX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        // ── Hide Conditions button — configure when this cosmetic auto-hides itself ──
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Hide Conditions →", () =>
            {
                var (cosType, isWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
                CosmeticHidePopup.Show(
                    pendingHide, cosType,
                    onPreview: PreviewFull,   // re-instantiate so the hide component reflects edits
                    parentPopupTransform: popup.transform,
                    worldMode: isWorld);
            }, row, new Vector2(BtnOffsetX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        // Impact Pose button — pose the cosmetic springs to on contact, with "React when alive"/"dead" toggles. Bridge-only.
        if (BridgeIds.IsBridgeAsset(asset))
        {
            // "React when dead" only → animate on the death-head preview, else live avatar; enter/exit death-head mode only on mode changes.
            bool impactDeathMode = false;

            CosmeticOffsetEntry? DeathHeadOffset() => pendingOffsets.FirstOrDefault(
                o => o.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead);

            void ShowImpactPreview(DeathHeadFloorPose fp)
            {
                bool wantDead = fp.ReactWhenDead && !fp.ReactWhenAlive;
                if (wantDead)
                {
                    if (!impactDeathMode)
                    {
                        preview.StopImpactPosePreview();   // clear any live-body base capture
                        preview.EnterDeathHeadMode(preview.FindPreviewCosmeticGo(),
                            pendingCrown != null, DeathHeadOffset());
                        impactDeathMode = true;
                    }
                    preview.SetDeathHeadFloorAnimation(fp);
                }
                else
                {
                    if (impactDeathMode)
                    {
                        preview.StopDeathHeadFloorAnimation();
                        preview.ExitDeathHeadMode();
                        impactDeathMode = false;
                    }
                    preview.PlayImpactPosePreview(fp);
                }
            }

            void EndImpactPreview()
            {
                if (impactDeathMode)
                {
                    preview.StopDeathHeadFloorAnimation();
                    preview.ExitDeathHeadMode();
                    impactDeathMode = false;
                }
                else preview.StopImpactPosePreview();
            }

            RectTransform? impactRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Impact Pose →", () =>
                {
                    DeathHeadFloorPosePopup.Show(
                        existing: pendingFloorPose,
                        // One-shot squish→hold→unsquish per edit (the real reaction only fires on contact in-game).
                        onPreview: ShowImpactPreview,
                        onDone: fp => { EndImpactPreview(); pendingFloorPose = fp; PreviewFull(); },
                        onClose: EndImpactPreview,
                        parentPopupTransform: popup.transform);
                }, row, new Vector2(BtnOffsetX, 0f));
                return impactRow = row;
            }, topPadding: BtnTopGap);
            impactEl = impactRow != null ? impactRow.GetComponent<REPOScrollViewElement>() : null;
        }

        // ── Crown Settings — gated to Hat / HeadTopMesh by UpdateConditionalRows (built always) ─
        {
            // "Crown Settings →" opens the crown configurator popup; Clear Crown and Cancel are handled inside CosmeticCrownPopup.
            RectTransform? crownRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Crown Settings →", () =>
                {
                    // Attach the guard that keeps the crown visible and positioned while Crown Settings is open; destroyed on any close path (Done/Clear/Cancel).
                    CrownForceVisibleGuard? crownGuard = null;
                    var cosmeticGo = preview.FindPreviewCosmeticGo();
                    if (cosmeticGo != null && preview.PreviewPc?.playerCrown != null)
                        crownGuard = CrownForceVisibleGuard.Attach(preview.PreviewPc.playerCrown, cosmeticGo);

                    void CleanupGuard()
                    {
                        if (crownGuard != null)
                        {
                            UnityEngine.Object.Destroy(crownGuard);
                            crownGuard = null;
                        }
                    }

                    CosmeticCrownPopup.Show(
                        existing: pendingCrown,
                        cosmeticType: asset.type,
                        onDone: result => { pendingCrown = result; CleanupGuard(); },
                        onClear: () => { pendingCrown = null; CleanupGuard(); preview.RefreshCrown(null); },
                        onPreview: config => preview.RefreshCrown(config),
                        onCancel: CleanupGuard,
                        parentPopupTransform: popup.transform);
                }, row, new Vector2(BtnCrownX, 0f));
                return crownRow = row;
            }, topPadding: BtnTopGap);
            crownEl = crownRow != null ? crownRow.GetComponent<REPOScrollViewElement>() : null;
        }

        // Death Head → — offset applied while the player is a death head. Gated to death-head types (built always; hidden for World).
        {
            RectTransform? deathHeadRow = null;
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Death Head →", () =>
                {
                    var existing = pendingOffsets.FirstOrDefault(
                        o => o.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead);
                    void RemoveDeathHeadOffset() => pendingOffsets.RemoveAll(
                        o => o.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead);
                    CosmeticOffsetEntryPopup.Show(new OffsetEntryArgs
                    {
                        Existing = existing,
                        Triggers = null,
                        OnDone = result =>
                        {
                            preview.ExitDeathHeadMode();
                            if (result != null)
                            {
                                RemoveDeathHeadOffset();
                                pendingOffsets.Add(result);
                                preview.RefreshOffsets(pendingOffsets);
                            }
                        },
                        OnPreview = partial => preview.UpdateDeathHeadOffset(partial),
                        ParentPopupTransform = popup.transform,
                        OnDeathHeadTrigger = enter =>
                        {
                            if (enter)
                            {
                                preview.EnterDeathHeadMode(
                                    preview.FindPreviewCosmeticGo(), pendingCrown != null, existing);
                                // Reflect the current Show-on-Death-Head toggle right away.
                                preview.SetDeathHeadConfiguredCosmeticVisible(pendingShowOnDeathHead != false);
                            }
                            else
                                preview.ExitDeathHeadMode();
                        },
                        LockedTrigger = CosmeticCustomCondition.Type.Player_DeathHead,
                        OnClear = () =>
                        {
                            preview.ExitDeathHeadMode();
                            RemoveDeathHeadOffset();
                            preview.RefreshOffsets(pendingOffsets);
                        },
                        // "Show on Death Head" toggle (this editor only): live changes preview, value commits on Done.
                        ShowOnDeathHead = pendingShowOnDeathHead != false,
                        OnShowOnDeathHeadPreview = show => preview.SetDeathHeadConfiguredCosmeticVisible(show),
                        OnShowOnDeathHeadCommit = show => pendingShowOnDeathHead = show ? (bool?)null : false,
                    });
                }, row, new Vector2(BtnCrownX, 0f));
                return deathHeadRow = row;
            }, topPadding: BtnTopGap);
            deathHeadEl = deathHeadRow != null ? deathHeadRow.GetComponent<REPOScrollViewElement>() : null;
        }

        // All conditional rows are built — set their initial visibility for the current type.
        UpdateConditionalRows();

        AddSectionLabel(popup, "Actions", BtnTopGap);

        // ── Row: [BACK]  [SAVE] ──────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Back", () => popup.ClosePage(false),
                row, new Vector2(BtnBackX, 0f));

            void DoSave()
            {
                // Type/world/physics changes need full re-instantiation; sway/offsets/customTypes apply in-place.
                var oldCosmeticType = asset.type;
                bool oldIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
                var (oldFixCollider, oldFixAnimation) = CustomizerStore.GetEffectiveFixes(asset.assetId);
                var oldEquipAnim = CustomizerStore.GetEffectiveEquipAnimationMode(asset.assetId);
                bool oldTintable = asset.tintable;
                // Capture crown BEFORE SetAndApply — it mutates the CosmeticOverrideData in-place, so comparing afterwards is always equal.
                var oldCrown = existing?.Crown;
                bool oldFixCrown = existing?.FixCrown == true;
                // List-visible state (sort/section/border) — RefreshMenu rebuilds the whole scroll, so only refresh when one changed.
                var oldRarity = asset.rarity;
                bool oldModdedMark = CustomizerStore.IsModdedForAsset(asset)
                                  || CustomizerStore.IsNonBridgeModdedForAsset(asset);

                CustomizerStore.SetAndApply(asset, new CosmeticOverrideData
                {
                    IsModded                  = pendingModded,
                    Rarity                    = pendingRarity,
                    Type                      = pendingType,
                    FixCollider               = pendingFixCollider,
                    FixAnimation              = pendingFixAnimation,
                    VanillaEquipAnimationMode = pendingEquipAnim,
                    Tintable                  = pendingTintable,
                    EnableSway                = pendingEnableSway,
                    CustomTypes               = pendingCustomTypes != null
                                                    ? new List<CosmeticCustomCondition.Type>(pendingCustomTypes)
                                                    : null,
                    Offsets                   = pendingOffsets,
                    Crown                     = pendingCrown,
                    ShowOnDeathHead           = pendingShowOnDeathHead,
                    FloorPose                 = pendingFloorPose,
                    FixCrown                  = pendingFixCrown,
                    HideConditions            = pendingHide,
                    EnableCustomColors        = pendingCustomColors,
                    EnableColorAnimations     = pendingColorAnimations,
                    UseIsolatedIcon           = pendingIsolatedIcon,
                    UseFitOffsets             = pendingUseFit,
                });

                // Blacklist commit (separate store, load-time — applies next launch).
                if (pendingBlacklist != BridgeBlacklist.Contains(asset.assetId))
                    BridgeBlacklist.SetBlacklisted(asset.assetId, asset.assetName ?? asset.name, pendingBlacklist);

                if (pendingTintable == false)
                {
                    PerCosmeticColors.ClearForAsset(asset.assetId);
                    PerCosmeticColorNetworkSync.BroadcastAll();
                }

                // A bridge cosmetic switched to a MESH type can't carry animated colour — clear any animation (whole + slots) so it stops everywhere (live, death head, mini, remote).
                if (CustomizerStore.IsBridgeMeshSwitch(asset.assetId)
                    && PerCosmeticColors.ClearAllAnimationForAsset(asset.assetId))
                    PerCosmeticColorNetworkSync.BroadcastAll();

                // After SetAndApply, asset.type and IsWorldAsset reflect the new values.
                var (newCosmeticType, newIsWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
                var (newFixCollider, newFixAnimation) = CustomizerStore.GetEffectiveFixes(asset.assetId);
                var newEquipAnim = CustomizerStore.GetEffectiveEquipAnimationMode(asset.assetId);
                bool tintableChanged = oldTintable != asset.tintable;

                bool needsReinstantiation =
                    oldCosmeticType != newCosmeticType ||
                    oldIsWorld != newIsWorld ||
                    oldFixCollider != newFixCollider ||
                    oldFixAnimation != newFixAnimation ||
                    oldEquipAnim != newEquipAnim ||
                    // Tintable changes require remount: true→false leaves stale BridgeTintMaterials; false→true needs a fresh InjectBridgeTintMaterials.
                    tintableChanged ||
                    !CosmeticCrownConfig.ValueEquals(oldCrown, pendingCrown, 0f) ||
                    // FixCrown adds/removes a CosmeticPlayerCrown at instantiation time.
                    oldFixCrown != (pendingFixCrown == true);

                if (needsReinstantiation)
                    MoreHeadCosmeticMountPatch.ReinstantiateCosmetic(asset);
                else if (pendingTintable == false)
                    // Reapply colors only for an explicit Tintable=false with no remount triggered — clears lingering per-cosmetic colors.
                    RuntimeConfigApplier.ReapplyLocalCosmeticColors();

                bool listChanged =
                    oldRarity != asset.rarity ||
                    oldCosmeticType != newCosmeticType ||
                    oldIsWorld != newIsWorld ||
                    oldModdedMark != (CustomizerStore.IsModdedForAsset(asset)
                                      || CustomizerStore.IsNonBridgeModdedForAsset(asset));
                if (listChanged)
                    RefreshMenu();
                popup.ClosePage(false);
            }

            MenuAPI.CreateREPOButton("Save", () =>
            {
                // Changing type can leave offsets/conditions/crown that no longer apply — warn + prune before saving.
                var (newType, isWorld) = CustomizerStore.MapOverrideToVanilla(pendingType);
                bool dropCrown = pendingCrown != null
                    && newType is not (SemiFunc.CosmeticType.Hat or SemiFunc.CosmeticType.HeadTopMesh);
                var validOffsets = CosmeticTriggerCatalog.ValidOffsetTriggers(newType);
                var validConds = CosmeticTriggerCatalog.ValidCustomTypes(newType);
                // Player_DeathHead is intentionally NOT in ValidOffsetTriggers (it has its own button); keep it while the new type can carry it.
                bool deathHeadSupported = DeathHeadPrefabProvider.SupportedTypes.Contains(newType);
                // World cosmetics accept every offset trigger, so nothing is ever pruned for them.
                var dropOffsets = isWorld
                    ? new List<CosmeticOffsetEntry>()
                    : pendingOffsets.Where(o =>
                        !validOffsets.Contains(o.TriggerType)
                        && !(o.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead
                             && deathHeadSupported)).ToList();
                var dropConds = pendingCustomTypes.Where(c => !validConds.Contains(c)).ToList();

                if (dropCrown || dropOffsets.Count > 0 || dropConds.Count > 0)
                {
                    ShowPruneConfirm(dropOffsets.Count, dropConds.Count, dropCrown, () =>
                    {
                        if (dropCrown) pendingCrown = null;
                        foreach (var o in dropOffsets) pendingOffsets.Remove(o);
                        foreach (var c in dropConds) pendingCustomTypes.Remove(c);
                        DoSave();
                    });
                    return;
                }
                DoSave();
            }, row, new Vector2(BtnSaveX, 0f));

            return row;
        }, topPadding: 0f);

        // ── Row: [DELETE ICON]  [EXPORT] — show either button when available ─
        if (hasCachedIcon || hasOverride)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);

                if (hasCachedIcon)
                    MenuAPI.CreateREPOButton("Delete Icon", () =>
                    {
                        IconCapture.DeleteCache(asset);
                        // The popup closes with the cursor over this cosmetic's button — suppress so hover auto-capture doesn't re-shoot the PNG the same frame.
                        CosmeticHoverPatch.SuppressWhileHovered(asset);
                        // Repaint just this asset's button (placeholder) — no full-list rebuild.
                        IconCapture.RefreshVisibleButtons(asset);
                        popup.ClosePage(false);
                    }, row, new Vector2(BtnDeleteIconX, 0f));

                if (hasOverride)
                    MenuAPI.CreateREPOButton("Export Settings", () =>
                    {
                        CustomizerIO.ExportSingle(asset.assetId);
                    }, row, new Vector2(BtnExportX, 0f));

                return row;
            }, topPadding: 10f);
        }

        // ── Row: [RESET] — shown when an override exists
        if (hasOverride)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Reset", () =>
                {
                    // Capture current effective state before reverting; same rebuild-vs-in-place split as DoSave.
                    var oldCosmeticType = asset.type;
                    bool oldIsWorld = HhhCosmeticLoader.IsWorldAsset(asset);
                    var (oldFixCollider, oldFixAnimation) = CustomizerStore.GetEffectiveFixes(asset.assetId);
                    var oldEquipAnim = CustomizerStore.GetEffectiveEquipAnimationMode(asset.assetId);
                    bool resetOldTintable = asset.tintable;   // capture before Reset restores it
                    var oldRarity = asset.rarity;
                    bool oldModdedMark = CustomizerStore.IsModdedForAsset(asset)
                                      || CustomizerStore.IsNonBridgeModdedForAsset(asset);

                    CustomizerStore.Reset(asset);

                    // After Reset, asset.type/tintable/etc. reflect loader defaults.
                    var (newFixCollider, newFixAnimation) = CustomizerStore.GetEffectiveFixes(asset.assetId);
                    var newEquipAnim = CustomizerStore.GetEffectiveEquipAnimationMode(asset.assetId);

                    bool resetTintableChanged = resetOldTintable != asset.tintable;
                    bool needsReinstantiation =
                        oldCosmeticType != asset.type ||
                        oldIsWorld != HhhCosmeticLoader.IsWorldAsset(asset) ||
                        oldFixCollider != newFixCollider ||
                        oldFixAnimation != newFixAnimation ||
                        oldEquipAnim != newEquipAnim ||
                        resetTintableChanged ||       // tintable restored by reset needs remount
                        existing?.Crown != null ||   // crown removed by reset
                        existing?.FixCrown == true;  // fix crown removed by reset

                    if (needsReinstantiation)
                        MoreHeadCosmeticMountPatch.ReinstantiateCosmetic(asset);

                    bool listChanged =
                        oldRarity != asset.rarity ||
                        oldCosmeticType != asset.type ||
                        oldIsWorld != HhhCosmeticLoader.IsWorldAsset(asset) ||
                        oldModdedMark != (CustomizerStore.IsModdedForAsset(asset)
                                          || CustomizerStore.IsNonBridgeModdedForAsset(asset));
                    if (listChanged)
                        RefreshMenu();
                    popup.ClosePage(false);
                }, row, new Vector2(BtnResetX, 0f));
                return row;
            }, topPadding: 10f);
        }

        // openOnTop: false → sets the cosmetics menu Inactive while open, blocking clicks behind it. MenuLib restores it on ClosePage.
        popup.OpenPage(openOnTop: false);
    }

}
