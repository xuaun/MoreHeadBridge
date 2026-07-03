// Layer 2 of the offset configurator: trigger condition + Pos/Rot/Scale/Lerp sliders. Done returns the entry, Cancel returns null; onPreview fires live.

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace MoreHeadBridge;

// Request bundle for CosmeticOffsetEntryPopup.Show — set only the fields the caller needs (the rest keep their defaults).
internal sealed class OffsetEntryArgs
{
    internal CosmeticOffsetEntry?                  Existing;
    internal CosmeticCustomCondition.Type[]?       Triggers;
    internal Action<CosmeticOffsetEntry?>          OnDone = _ => { };
    internal Action<CosmeticOffsetEntry?>?         OnPreview;
    internal Transform?                            ParentPopupTransform;
    internal bool                                  WorldMode;
    internal Action<bool>?                         OnDeathHeadTrigger;
    internal CosmeticCustomCondition.Type?         LockedTrigger;
    internal Action?                               OnClear;
    internal bool                                  ShowOnDeathHead = true;
    internal Action<bool>?                         OnShowOnDeathHeadPreview;
    internal Action<bool>?                         OnShowOnDeathHeadCommit;
    // Floor-pose plumbing: reserved (no current caller wires it — the floor-pose editor is DeathHeadFloorPosePopup). Explicit defaults keep this set warning-free.
    internal DeathHeadFloorPose?                   FloorPose = null;
    internal Action<DeathHeadFloorPose?>?          OnFloorPoseCommit = null;
    internal bool                                  FloorPoseSupported = false;
    internal Action<DeathHeadFloorPose>?           OnFloorPosePreview = null;
    internal Action?                               OnFloorPosePreviewEnd = null;
}

internal static class CosmeticOffsetEntryPopup
{
    // ── Slider option arrays (built once) ────────────────────────────────────

    // "None" + every CosmeticCustomCondition.Type, ordered by family/variant/label.
    private static readonly CosmeticConditionFormat.ConditionOption[] AllConditionOptions = BuildAllConditionOptions();
    private static CosmeticConditionFormat.ConditionOption[] BuildAllConditionOptions()
    {
        return Enum.GetValues(typeof(CosmeticCustomCondition.Type))
            .Cast<CosmeticCustomCondition.Type>()
            .OrderBy(CosmeticConditionFormat.FamilyRank)
            .ThenBy(CosmeticConditionFormat.VariantRank)
            .ThenBy(t => CosmeticConditionFormat.Label(t))
            .Select(t => new CosmeticConditionFormat.ConditionOption(t, CosmeticConditionFormat.Label(t)))
            .ToArray();
    }

    // World cosmetics use the curated catalog (CosmeticOffsetPopup.WorldOffsetTriggers) with region-disambiguated labels; engine-internal signals are excluded.
    private static readonly CosmeticConditionFormat.ConditionOption[] WorldConditionOptions = BuildWorldConditionOptions();
    private static CosmeticConditionFormat.ConditionOption[] BuildWorldConditionOptions()
    {
        return CosmeticOffsetPopup.WorldOffsetTriggers
            .OrderBy(CosmeticConditionFormat.FamilyRank)
            .ThenBy(CosmeticConditionFormat.VariantRank)
            .ThenBy(t => CosmeticConditionFormat.Label(t, headExplicit: true))
            .Select(t => new CosmeticConditionFormat.ConditionOption(t, CosmeticConditionFormat.Label(t, headExplicit: true)))
            .ToArray();
    }

    private static CosmeticConditionFormat.ConditionOption[] BuildFilteredOptions(CosmeticCustomCondition.Type[] types)
    {
        var result = new CosmeticConditionFormat.ConditionOption[types.Length];
        for (int i = 0; i < types.Length; i++)
            result[i] = new CosmeticConditionFormat.ConditionOption(types[i], CosmeticConditionFormat.Label(types[i]));
        return result;
    }

    // Position: -1.00 to +1.00 in 0.05 steps.
    internal static readonly string[] PosOptions = BuildPosOptions();
    private static string[] BuildPosOptions()
    {
        var list = new List<string>();
        for (float v = -1.00f; v <= 1.0001f; v += 0.05f)
            list.Add(v.ToString("F2", CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    // Rotation: -180 to +180 in 5-degree steps.
    internal static readonly string[] RotOptions = BuildRotOptions();
    private static string[] BuildRotOptions()
    {
        var list = new List<string>();
        for (int v = -180; v <= 180; v += 5)
            list.Add(v.ToString(CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    // Scale: 0.50 to 2.00 in 0.05 steps.
    internal static readonly string[] ScaleOptions = BuildScaleOptions();
    private static string[] BuildScaleOptions()
    {
        var list = new List<string>();
        for (float v = 0.50f; v <= 2.0001f; v += 0.05f)
            list.Add(v.ToString("F2", CultureInfo.InvariantCulture));
        return list.ToArray();
    }

    // Lerp speed: 1 – 10.
    internal static readonly string[] SpeedOptions =
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

    // Two-state options for the death-head "Show on Death Head" toggle.
    private static readonly string[] ShowHideOptions = { "Show", "Hide" };

    // ── Entry point ───────────────────────────────────────────────────────────

    /// existing: null = Add mode, non-null = Edit. triggers: restricts the Trigger dropdown when non-null.
    /// onDone: fires with the entry, or null on cancel. onPreview: fires per slider change; null when the trigger is "None".
    /// onDeathHeadTrigger: true when the trigger becomes Player_DeathHead, false when it leaves (or the popup closes) — gates the death-head sub-preview.
    /// lockedTrigger: fixes the trigger and hides its slider (the dedicated Death Head button).
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(OffsetEntryArgs args)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(args));

    private static void ShowNow(OffsetEntryArgs args)
    {
        // Unpack into locals so the builder body below reads unchanged.
        var existing                 = args.Existing;
        var triggers                 = args.Triggers;
        var onDone                   = args.OnDone;
        var onPreview                = args.OnPreview;
        var parentPopupTransform     = args.ParentPopupTransform;
        var worldMode                = args.WorldMode;
        var onDeathHeadTrigger       = args.OnDeathHeadTrigger;
        var lockedTrigger            = args.LockedTrigger;
        var onClear                  = args.OnClear;
        var showOnDeathHead          = args.ShowOnDeathHead;
        var onShowOnDeathHeadPreview = args.OnShowOnDeathHeadPreview;
        var onShowOnDeathHeadCommit  = args.OnShowOnDeathHeadCommit;
        var floorPose                = args.FloorPose;
        var onFloorPoseCommit        = args.OnFloorPoseCommit;
        var floorPoseSupported       = args.FloorPoseSupported;
        var onFloorPosePreview       = args.OnFloorPosePreview;
        var onFloorPosePreviewEnd    = args.OnFloorPosePreviewEnd;

        // "Show on Death Head" toggle only applies in the dedicated death-head editor; tracked locally, committed on Done so Cancel discards.
        bool deathHeadEditor = lockedTrigger == CosmeticCustomCondition.Type.Player_DeathHead
                               && onShowOnDeathHeadCommit != null;
        bool showOnDeath = showOnDeathHead;

        // Death-head "floor pose" — edited in the Configure Floor Pose sub-editor; cloned so Cancel discards, committed on Done.
        DeathHeadFloorPose? floorLocal = floorPose?.Clone();
        CosmeticCustomCondition.Type? selCondition = lockedTrigger ?? existing?.TriggerType;
        float posX = existing?.PosX ?? 0f;
        float posY = existing?.PosY ?? 0f;
        float posZ = existing?.PosZ ?? 0f;
        float rotX = existing?.RotX ?? 0f;
        float rotY = existing?.RotY ?? 0f;
        float rotZ = existing?.RotZ ?? 0f;
        float scaleX = existing?.ScaleX ?? 1f;
        float scaleY = existing?.ScaleY ?? 1f;
        float scaleZ = existing?.ScaleZ ?? 1f;
        float speed = existing?.LerpSpeed ?? 3f;

        // Builds the current partial entry from live slider state; null until a trigger is selected.
        CosmeticOffsetEntry? BuildCurrentEntry() => selCondition.HasValue
            ? new CosmeticOffsetEntry
            {
                TriggerType = selCondition.Value,
                PosX = posX, PosY = posY, PosZ = posZ,
                RotX = rotX, RotY = rotY, RotZ = rotZ,
                ScaleX = scaleX, ScaleY = scaleY, ScaleZ = scaleZ,
                LerpSpeed = speed,
            }
            : null;

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: lockedTrigger.HasValue
                ? CosmeticConditionFormat.Label(lockedTrigger.Value)
                : (existing == null ? "Add Offset" : "Edit Offset"),
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupUI.PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        // Edge-triggered: notifies the caller when the trigger enters/leaves Player_DeathHead to toggle its sub-preview.
        bool deathShown = false;
        void UpdateDeathHead()
        {
            bool isDeath = selCondition == CosmeticCustomCondition.Type.Player_DeathHead;
            if (isDeath == deathShown) return;
            deathShown = isDeath;
            onDeathHeadTrigger?.Invoke(isDeath);
        }

        // ── Trigger condition slider ───────────────────────────────────────
        // Hidden when the trigger is locked (Death Head button). World gets the full disambiguated list; otherwise the type-specific list, falling back to all conditions.
        var conditionOptions = worldMode
            ? WorldConditionOptions
            : (triggers != null ? BuildFilteredOptions(triggers) : AllConditionOptions);
        var conditionLookup = conditionOptions.ToDictionary(o => o.Label, o => o.Type);

        if (!lockedTrigger.HasValue)
        popup.AddElementToScrollView(scrollView =>
        {
            string defCond = selCondition.HasValue
                ? CosmeticConditionFormat.Label(selCondition.Value, headExplicit: worldMode)
                : "None";
            var optionLabels = new string[conditionOptions.Length + 1];
            optionLabels[0] = "None";
            for (int i = 0; i < conditionOptions.Length; i++)
                optionLabels[i + 1] = conditionOptions[i].Label;
            var s = MenuAPI.CreateREPOSlider(
                "Trigger", "",
                (string opt) =>
                {
                    if (opt == "None")
                    {
                        selCondition = null;
                        UpdateDeathHead();
                        onPreview?.Invoke(null);
                        return;
                    }

                    if (conditionLookup.TryGetValue(opt, out var value))
                    {
                        selCondition = value;
                        UpdateDeathHead();
                        onPreview?.Invoke(BuildCurrentEntry());
                    }
                },
                scrollView, optionLabels, defCond);
            return (RectTransform)s.transform;
        }, topPadding: PopupUI.TitleGap);

        // Edit mode may open already on Player_DeathHead — reflect that immediately.
        UpdateDeathHead();

        void Preview() => onPreview?.Invoke(BuildCurrentEntry());

        // Commits the death-head extras (Show toggle + floor pose) — called from Done AND Remove Offset so neither choice is lost.
        void CommitDeathHeadExtras()
        {
            if (!deathHeadEditor) return;
            onShowOnDeathHeadCommit?.Invoke(showOnDeath);
            onFloorPoseCommit?.Invoke(floorLocal);
        }

        // ── Show on Death Head toggle (death-head editor only) ─────────────
        // Hides this cosmetic on the in-game death head while keeping it on the live avatar; live preview, committed on Done.
        if (deathHeadEditor)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var s = MenuAPI.CreateREPOSlider(
                    "Show on Death Head", "",
                    (string opt) =>
                    {
                        showOnDeath = opt != "Hide";
                        onShowOnDeathHeadPreview?.Invoke(showOnDeath);
                    },
                    scrollView, ShowHideOptions, showOnDeath ? "Show" : "Hide");
                return (RectTransform)s.transform;
            }, topPadding: PopupUI.TitleGap);
        }

        // ── Position sliders ───────────────────────────────────────────────
        PopupUI.AddFloatSlider(popup, "Pos X", PosOptions, posX, v => { posX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddFloatSlider(popup, "Pos Y", PosOptions, posY, v => { posY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Pos Z", PosOptions, posZ, v => { posZ = v; Preview(); });

        // ── Rotation sliders ───────────────────────────────────────────────
        PopupUI.AddIntSlider(popup, "Rot X", RotOptions, (int)rotX, v => { rotX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddIntSlider(popup, "Rot Y", RotOptions, (int)rotY, v => { rotY = v; Preview(); });
        PopupUI.AddIntSlider(popup, "Rot Z", RotOptions, (int)rotZ, v => { rotZ = v; Preview(); });

        // ── Scale sliders ──────────────────────────────────────────────────
        PopupUI.AddFloatSlider(popup, "Scale X", ScaleOptions, scaleX, v => { scaleX = v; Preview(); }, PopupUI.BtnTopGap);
        PopupUI.AddFloatSlider(popup, "Scale Y", ScaleOptions, scaleY, v => { scaleY = v; Preview(); });
        PopupUI.AddFloatSlider(popup, "Scale Z", ScaleOptions, scaleZ, v => { scaleZ = v; Preview(); });

        // ── Lerp Speed slider ──────────────────────────────────────────────
        PopupUI.AddIntSlider(popup, "Lerp Speed", SpeedOptions, Mathf.Clamp((int)speed, 1, 10),
            v => { speed = v; Preview(); }, PopupUI.BtnTopGap);

        // ── Impact Pose → (death-head editor, bridge cosmetics only) ──
        if (deathHeadEditor && floorPoseSupported)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Impact Pose →", () =>
                {
                    DeathHeadFloorPosePopup.Show(
                        existing: floorLocal,
                        onPreview: p => onFloorPosePreview?.Invoke(p),
                        onDone: p => floorLocal = p,
                        onClose: () =>
                        {
                            onFloorPosePreviewEnd?.Invoke();
                            onPreview?.Invoke(BuildCurrentEntry()); // restore the offset preview
                        },
                        // Use the MAIN popup's transform, not this popup's: the avatar preview lives in the main popup, and LocalPopupOverlay.SetAsLastSibling only works when dimmer and avatar share a parent.
                        parentPopupTransform: parentPopupTransform);
                }, row, new Vector2(PopupUI.ButtonLeftX, 0f));
                return row;
            }, topPadding: PopupUI.BtnTopGap);
        }

        // ── Remove Offset (locked mode only — e.g. the Death Head button) ──
        if (onClear != null)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                MenuAPI.CreateREPOButton("Remove Offset", () =>
                {
                    popup.ClosePage(false);
                    // Removing the position offset shouldn't discard the death-head extras.
                    CommitDeathHeadExtras();
                    onClear();
                }, row, new Vector2(PopupUI.ButtonLeftX, 0f));
                return row;
            }, topPadding: PopupUI.BtnTopGap);
        }

        // ── Buttons ────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                if (deathShown) onDeathHeadTrigger?.Invoke(false);
                popup.ClosePage(false);
                onDone(null);
            }, row, new Vector2(PopupUI.ButtonLeftX, 0f));

            MenuAPI.CreateREPOButton("Done", () =>
            {
                if (deathShown) onDeathHeadTrigger?.Invoke(false);
                popup.ClosePage(false);
                CommitDeathHeadExtras();
                if (selCondition.HasValue)
                {
                    onDone(new CosmeticOffsetEntry
                    {
                        TriggerType = selCondition.Value,
                        PosX = posX,
                        PosY = posY,
                        PosZ = posZ,
                        RotX = rotX,
                        RotY = rotY,
                        RotZ = rotZ,
                        ScaleX = scaleX,
                        ScaleY = scaleY,
                        ScaleZ = scaleZ,
                        LerpSpeed = speed,
                    });
                }
                else
                {
                    onDone(null); // "None" selected — treat as cancel
                }
            }, row, new Vector2(PopupUI.ButtonRightX, 0f));

            return row;
        }, topPadding: PopupUI.BtnTopGap);

        // Stacks on the list popup, whose PopupScrollGuard already disabled external scroll boxes — this guard captures nothing.
        popup.OpenPage(openOnTop: true);
    }

}
