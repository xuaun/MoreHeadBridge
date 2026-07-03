// Two-layer popup for conditional pos/rot/scale offsets: list view → per-entry configurator. Commits the working copy to pendingOffsets on Done.

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

// ── Layer 1: list of all offset entries ──────────────────────────────────────

internal static class CosmeticOffsetPopup
{
    private const float PopupX = -120f;
    private const float TitleGap = 15f;
    private const float BtnTopGap = 10f;
    private const float BtnRowH = 30f;
    private const float BtnDoneX = 58f;
    private const float BtnCancelX = -137f;

    // Entry-row positions: label on the left, Edit/Del buttons on the right.
    private const float EntryLabelX = -137f;
    private const float EntryEditX = 11f;
    private const float EntryDelX = 71f;

    // ── Offset trigger conditions per cosmetic type ───────────────────────────
    // Maps each cosmetic slot to the conditions it would usefully REACT TO (the matching region's mesh signals). Unlisted types fall back to all conditions.

    private static readonly CosmeticCustomCondition.Type[] s_headMesh = new[]
    {
        CosmeticCustomCondition.Type.HeadTopMesh_Shape_Big,
        CosmeticCustomCondition.Type.HeadTopMesh_Shape_Tiny,
        CosmeticCustomCondition.Type.HeadTopMesh_Shape_MissingRight,
        CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Big,
        CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Tiny,
        CosmeticCustomCondition.Type.EyeLeft_Disable,
        CosmeticCustomCondition.Type.EyeRight_Disable,
    };
    private static readonly CosmeticCustomCondition.Type[] s_bodyTopMesh = new[]
    {
        CosmeticCustomCondition.Type.BodyTopMesh_Shape_Big,
        CosmeticCustomCondition.Type.BodyTopMesh_Shape_Tiny,
    };
    private static readonly CosmeticCustomCondition.Type[] s_bodyBotMesh = new[]
    {
        CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Big,
        CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Wide,
        CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tall,
        CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tiny,
    };
    private static readonly CosmeticCustomCondition.Type[] s_armRMesh = new[]
    {
        CosmeticCustomCondition.Type.ArmRightMesh_Shape_Big,
        CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigUpper,
        CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigLower,
        CosmeticCustomCondition.Type.ArmRightMesh_Shape_Huge,
        CosmeticCustomCondition.Type.ArmRightMesh_Shape_Tiny,
    };
    private static readonly CosmeticCustomCondition.Type[] s_armLMesh = new[]
    {
        CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Big,
        CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigUpper,
        CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigLower,
        CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Huge,
        CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Tiny,
    };
    private static readonly CosmeticCustomCondition.Type[] s_legRMesh = new[]
    {
        CosmeticCustomCondition.Type.LegRightMesh_Shape_Big,
        CosmeticCustomCondition.Type.LegRightMesh_Shape_BigUpper,
        CosmeticCustomCondition.Type.LegRightMesh_Shape_BigLower,
        CosmeticCustomCondition.Type.LegRightMesh_Shape_Huge,
        CosmeticCustomCondition.Type.LegRightMesh_Shape_Tiny,
    };
    private static readonly CosmeticCustomCondition.Type[] s_legLMesh = new[]
    {
        CosmeticCustomCondition.Type.LegLeftMesh_Shape_Big,
        CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigUpper,
        CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigLower,
        CosmeticCustomCondition.Type.LegLeftMesh_Shape_Huge,
        CosmeticCustomCondition.Type.LegLeftMesh_Shape_Tiny,
    };

    private static readonly Dictionary<SemiFunc.CosmeticType, CosmeticCustomCondition.Type[]> OffsetTriggers
        = new()
        {
            // Head-region cosmetics react to head/chin mesh condition trigger settings.
            [SemiFunc.CosmeticType.Hat] = s_headMesh,
            [SemiFunc.CosmeticType.Eyewear] = s_headMesh,
            [SemiFunc.CosmeticType.FaceTop] = s_headMesh,
            [SemiFunc.CosmeticType.FaceBottom] = s_headMesh,
            [SemiFunc.CosmeticType.HeadBottom] = s_headMesh,
            [SemiFunc.CosmeticType.Ears] = s_headMesh,
            [SemiFunc.CosmeticType.EyeLidRightMesh] = s_headMesh,
            [SemiFunc.CosmeticType.EyeLidLeftMesh] = s_headMesh,
            // Body cosmetics react to the mesh of the same body region.
            [SemiFunc.CosmeticType.BodyTop] = s_bodyTopMesh,
            [SemiFunc.CosmeticType.BodyBottom] = s_bodyBotMesh,
            [SemiFunc.CosmeticType.ArmRight] = s_armRMesh,
            [SemiFunc.CosmeticType.ArmLeft] = s_armLMesh,
            [SemiFunc.CosmeticType.LegRight] = s_legRMesh,
            [SemiFunc.CosmeticType.LegLeft] = s_legLMesh,
            [SemiFunc.CosmeticType.FootRight] = s_legRMesh,
            [SemiFunc.CosmeticType.FootLeft] = s_legLMesh,
            // Overlay cosmetics react to the same mesh as their base counterpart.
            [SemiFunc.CosmeticType.BodyTopOverlay] = s_bodyTopMesh,
            [SemiFunc.CosmeticType.BodyBottomOverlay] = s_bodyBotMesh,
            [SemiFunc.CosmeticType.ArmRightOverlay] = s_armRMesh,
            [SemiFunc.CosmeticType.ArmLeftOverlay] = s_armLMesh,
            [SemiFunc.CosmeticType.LegRightOverlay] = s_legRMesh,
            [SemiFunc.CosmeticType.LegLeftOverlay] = s_legLMesh,
            // Mesh cosmetics react to their own signals for sub-component adjustments.
            [SemiFunc.CosmeticType.HeadTopMesh] = s_headMesh,
            [SemiFunc.CosmeticType.HeadBottomMesh] = s_headMesh,
            [SemiFunc.CosmeticType.BodyTopMesh] = s_bodyTopMesh,
            [SemiFunc.CosmeticType.BodyBottomMesh] = s_bodyBotMesh,
            [SemiFunc.CosmeticType.ArmRightMesh] = s_armRMesh,
            [SemiFunc.CosmeticType.ArmLeftMesh] = s_armLMesh,
            [SemiFunc.CosmeticType.LegRightMesh] = s_legRMesh,
            [SemiFunc.CosmeticType.LegLeftMesh] = s_legLMesh,
        };

    /// The offset trigger types valid for a cosmetic type (empty if none).
    /// Single source of truth, consulted via CosmeticTriggerCatalog by the prune-on-save check.
    internal static IReadOnlyList<CosmeticCustomCondition.Type> ValidOffsetTriggers(SemiFunc.CosmeticType type)
        => OffsetTriggers.TryGetValue(type, out var arr) ? arr : System.Array.Empty<CosmeticCustomCondition.Type>();

    // Curated trigger set for World cosmetics: every region's mesh shapes + Crown (not the raw enum, which has engine-internal signals). Player_DeathHead excluded — it has its own dedicated button.
    internal static readonly CosmeticCustomCondition.Type[] WorldOffsetTriggers = BuildWorldOffsetTriggers();
    private static CosmeticCustomCondition.Type[] BuildWorldOffsetTriggers()
    {
        var set = new List<CosmeticCustomCondition.Type>();
        foreach (var arr in new[] { s_headMesh, s_bodyTopMesh, s_bodyBotMesh, s_armRMesh, s_armLMesh, s_legRMesh, s_legLMesh })
            foreach (var t in arr)
                if (!set.Contains(t)) set.Add(t);
        if (!set.Contains(CosmeticCustomCondition.Type.Player_Crown))
            set.Add(CosmeticCustomCondition.Type.Player_Crown);
        return set.ToArray();
    }

    /// Opens the offset list popup. Edits go to a working copy; only Done commits to pendingOffsets. forType filters the trigger dropdown.
    /// worldMode: every condition offered regardless of forType (worlds aren't body-attached), minus the never-fired HeadTop_Covered_TopMiddle.
    // Build deferred to mouse-release: a fresh page is ACTIVE before OpenPage, so a REPOSlider landing under the still-held click gets scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(List<CosmeticOffsetEntry> pendingOffsets,
                              SemiFunc.CosmeticType forType = default,
                              Action<List<CosmeticOffsetEntry>>? onPreview = null,
                              Transform? parentPopupTransform = null,
                              bool worldMode = false,
                              Action<bool>? onDeathHeadTrigger = null)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(
            pendingOffsets, forType, onPreview, parentPopupTransform, worldMode, onDeathHeadTrigger));

    private static void ShowNow(List<CosmeticOffsetEntry> pendingOffsets,
                              SemiFunc.CosmeticType forType,
                              Action<List<CosmeticOffsetEntry>>? onPreview,
                              Transform? parentPopupTransform,
                              bool worldMode,
                              Action<bool>? onDeathHeadTrigger)
    {
        var working = pendingOffsets.Select(e => e.Clone()).ToList();
        CosmeticCustomCondition.Type[]? triggers = null;
        if (!worldMode) OffsetTriggers.TryGetValue(forType, out triggers);
        ShowInternal(pendingOffsets, working, triggers, worldMode, forType, onPreview, parentPopupTransform, onDeathHeadTrigger);
    }

    private static void ShowInternal(
        List<CosmeticOffsetEntry> original,
        List<CosmeticOffsetEntry> working,
        CosmeticCustomCondition.Type[]? triggers,
        bool worldMode,
        SemiFunc.CosmeticType forType,
        Action<List<CosmeticOffsetEntry>>? onPreview = null,
        Transform? parentPopupTransform = null,
        Action<bool>? onDeathHeadTrigger = null)
    {
        // Player_DeathHead offsets are configured via the dedicated "Death Head" button, not this list. Hold them aside so the list never shows/edits them, and re-attach on Done.
        var deathHeadAside = working
            .Where(e => e.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead).ToList();
        working.RemoveAll(e => e.TriggerType == CosmeticCustomCondition.Type.Player_DeathHead);

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: "Special Position Fixes",
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        // ── "No entries" placeholder ──────────────────────────────────────────
        // Always present; visibility toggled by Rebuild(). First fixed element so entry rows can be inserted before it by sibling index.
        REPOScrollViewElement? noEntriesEl = null;
        Transform? noEntriesTr = null;

        popup.AddElementToScrollView(scrollView =>
        {
            var label = MenuAPI.CreateREPOLabel("(no entries)", scrollView);
            noEntriesTr = label.transform;
            // AddElementToScrollView adds REPOScrollViewElement after Invoke returns; grabbed on the next line where it's guaranteed to exist.
            return (RectTransform)label.transform;
        }, topPadding: TitleGap);

        noEntriesEl = noEntriesTr!.GetComponent<REPOScrollViewElement>();

        // ── Tracked dynamic entry rows ────────────────────────────────────────
        var entryRows = new List<GameObject>();

        void SortWorking()
        {
            working.Sort(CosmeticConditionFormat.Compare);
        }

        // ── Rebuild: destroy old rows, create new rows before fixed elements ──
        // Shows the user's offsets PLUS this type's automatic vanilla fixes (those not yet taken over by a user entry) as "(auto)" rows — Edit pre-fills from the seed, the right button disables/restores them. So the auto fixes live inside this list, not behind a sub-popup.
        void Rebuild()
        {
            // Hide + destroy old rows. visibility = false before Destroy prevents a one-frame layout flash while Unity defers destruction.
            foreach (var go in entryRows)
            {
                if (go == null) continue;
                var el = go.GetComponent<REPOScrollViewElement>();
                if (el != null) el.visibility = false;
                UnityEngine.Object.Destroy(go);
            }
            entryRows.Clear();

            // Fit triggers for this type render in a STABLE seed order (auto/custom/off shown in place), then any free-form (non-fit) offsets, sorted — so toggling a fix never moves its row.
            IReadOnlyList<CosmeticCustomCondition.Type> seedTriggers = worldMode
                ? System.Array.Empty<CosmeticCustomCondition.Type>()
                : OffsetSeedDefaults.SeedTriggersFor(forType);
            var seedSet = new HashSet<CosmeticCustomCondition.Type>(seedTriggers);
            var extraUser = working.Where(o => !seedSet.Contains(o.TriggerType)).ToList();
            extraUser.Sort(CosmeticConditionFormat.Compare);

            int total = seedTriggers.Count + extraUser.Count;
            if (noEntriesEl != null) noEntriesEl.visibility = total == 0;
            if (total == 0)
            {
                popup.scrollView.UpdateElements();
                return;
            }

            // Insert entry rows before noEntriesTr — after destroying the old rows it is the first child (sibling index 0).
            int insertAt = noEntriesTr!.GetSiblingIndex();
            var scroller = popup.menuScrollBox.scroller;
            int sib = 0;

            void AddRow(CosmeticCustomCondition.Type trig, bool isAuto)
            {
                bool fit = OffsetSeedDefaults.IsFitTrigger(trig);
                // Opt-in seeds (e.g. MissingRight) are NOT auto-applied: no user entry means OFF, and
                // an identity entry is equivalent — either way "Use" materialises the seed as an entry.
                bool optIn = OffsetSeedDefaults.IsOptInTrigger(trig);
                var entry = isAuto ? null : working.FirstOrDefault(o => o.TriggerType == trig);
                bool off = (entry != null && IsIdentityFix(entry)) || (isAuto && optIn);
                string suffix = off ? "  (off)" : isAuto ? "  (auto)" : fit ? "  (custom)" : "";

                var row = CreateEntryRow(scroller, sib == 0 ? TitleGap : 0f);
                row.SetSiblingIndex(insertAt + sib);
                sib++;

                var label = MenuAPI.CreateREPOLabel(
                    CosmeticConditionFormat.Label(trig, headExplicit: worldMode) + suffix,
                    row, new Vector2(EntryLabelX, 0f));
                label.rectTransform.sizeDelta = new Vector2(155f, BtnRowH);
                label.labelTMP.rectTransform.sizeDelta = label.rectTransform.sizeDelta;
                label.labelTMP.fontSize = 20f;
                label.labelTMP.enableAutoSizing = true;
                label.labelTMP.fontSizeMin = 12f;
                label.labelTMP.fontSizeMax = 20f;

                MenuAPI.CreateREPOButton("Edit", () =>
                {
                    CosmeticOffsetEntry? existing = working.FirstOrDefault(o => o.TriggerType == trig);
                    if (existing == null) OffsetSeedDefaults.TryGetSeed(forType, trig, out existing);
                    // Fit triggers lock the trigger (a fix shouldn't change which shape it reacts to); a free-form offset keeps the dropdown so its trigger can still be changed.
                    CosmeticCustomCondition.Type? locked = fit ? trig : (CosmeticCustomCondition.Type?)null;
                    PopupUI.AfterMouseRelease(popup, () =>
                        CosmeticOffsetEntryPopup.Show(new OffsetEntryArgs
                        {
                            Existing = existing,
                            Triggers = triggers,
                            OnDone = result =>
                            {
                                if (result == null) { onPreview?.Invoke(working); return; }
                                working.RemoveAll(o => o.TriggerType == trig || o.TriggerType == result.TriggerType);
                                working.Add(result);
                                SortWorking();
                                Rebuild();
                            },
                            OnPreview = partial =>
                            {
                                if (partial == null) return;
                                var temp = working.Where(o => o.TriggerType != trig).Select(e => e.Clone()).ToList();
                                temp.Add(partial);
                                onPreview?.Invoke(temp);
                            },
                            ParentPopupTransform = parentPopupTransform ?? popup.transform,
                            WorldMode = worldMode,
                            OnDeathHeadTrigger = onDeathHeadTrigger,
                            LockedTrigger = locked,
                        }));
                }, row, new Vector2(EntryEditX, 0f));

                // auto → Skip (disable); off → Use (apply the seed); else → Del (remove the offset).
                MenuAPI.CreateREPOButton(off ? "Use" : isAuto ? "Skip" : "Del", () =>
                {
                    working.RemoveAll(o => o.TriggerType == trig);
                    if (off && optIn)   // opt into the seeded fix → materialise it as a user entry
                    {
                        if (OffsetSeedDefaults.TryGetSeed(forType, trig, out var s))
                            working.Add(s);
                    }
                    else if (isAuto)   // disable an auto fix → identity entry (re-bases to the default pose)
                    {
                        OffsetSeedDefaults.TryGetSeed(forType, trig, out var s);
                        working.Add(new CosmeticOffsetEntry { TriggerType = trig, LerpSpeed = s?.LerpSpeed ?? 3f });
                    }
                    SortWorking();
                    Rebuild();
                }, row, new Vector2(EntryDelX, 0f));

                entryRows.Add(row.gameObject);
            }

            foreach (var t in seedTriggers)
                AddRow(t, isAuto: working.All(o => o.TriggerType != t));
            foreach (var e in extraUser)
                AddRow(e.TriggerType, isAuto: false);

            // Explicit recalc: OnTransformChildrenChanged fired per SetParent/SetSiblingIndex above; this guarantees a clean final layout regardless of intermediate firing order.
            popup.scrollView.UpdateElements();

            // Notify the live preview so it shows the current working offsets.
            onPreview?.Invoke(working);
        }

        // ── Add Entry button ──────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("+ Add Entry", () =>
            {
                // Deferred to mouse-release like Edit: REPO buttons fire on mouse-DOWN and a MenuLib slider scrubs while held, silently dragging whichever slider spawns under the cursor.
                PopupUI.AfterMouseRelease(popup, () =>
                    CosmeticOffsetEntryPopup.Show(new OffsetEntryArgs
                    {
                        Triggers = triggers,
                        OnDone = result =>
                        {
                            if (result == null) { onPreview?.Invoke(working); return; }
                            working.Add(result);
                            SortWorking();
                            Rebuild();
                        },
                        OnPreview = partial =>
                        {
                            if (partial == null) return;
                            var temp = working.Select(e => e.Clone()).ToList();
                            temp.Add(partial);
                            onPreview?.Invoke(temp);
                        },
                        ParentPopupTransform = parentPopupTransform ?? popup.transform,
                        WorldMode = worldMode,
                        OnDeathHeadTrigger = onDeathHeadTrigger,
                    }));
            }, row, new Vector2(BtnCancelX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        // ── Cancel / Done ─────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                onPreview?.Invoke(original);
                popup.ClosePage(false);
            }, row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Done", () =>
            {
                original.Clear();
                original.AddRange(working);
                original.AddRange(deathHeadAside); // preserve the button-managed DeathHead offset
                popup.ClosePage(false);
            }, row, new Vector2(BtnDoneX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        // Initial population — runs before OpenPage so layout is correct on first frame.
        Rebuild();

        popup.OpenPage(openOnTop: true);
    }

    private static bool IsIdentityFix(CosmeticOffsetEntry e)
        => e.PosX == 0f && e.PosY == 0f && e.PosZ == 0f
        && e.RotX == 0f && e.RotY == 0f && e.RotZ == 0f
        && e.ScaleX == 1f && e.ScaleY == 1f && e.ScaleZ == 1f;

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Row parented to the scroller with REPOScrollViewElement attached (for dynamically inserted rows). Layout is driven by OnTransformChildrenChanged → UpdateElements, so no onSettingChanged needed.
    private static RectTransform CreateEntryRow(Transform scroller, float topPadding = 0f)
    {
        var row = new GameObject("Entry Row", typeof(RectTransform))
            .GetComponent<RectTransform>();
        row.sizeDelta = new Vector2(0f, BtnRowH);
        row.SetParent(scroller, false);

        var el = row.gameObject.AddComponent<REPOScrollViewElement>();
        el.topPadding = topPadding;
        return row;
    }
}
