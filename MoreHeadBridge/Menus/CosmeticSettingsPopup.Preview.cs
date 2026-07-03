// Sync Customizer preview: builds the per-field NEW/same/≠/reset/REMOVED rows for one cosmetic and
// shows them in the sub-popup opened from a cosmetic's name button.

using HarmonyLib;
using MenuLib;
using MenuLib.MonoBehaviors;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal static partial class CosmeticSettingsPopup
{
    // ── Preview row data ──────────────────────────────────────────────────────
    private readonly struct PreviewRow
    {
        internal readonly string FieldText;
        internal readonly string StatusText;
        internal readonly Color  StatusColor;
        internal PreviewRow(string field, string status, Color color)
        { FieldText = field; StatusText = status; StatusColor = color; }
    }

    // ── Tags ──────────────────────────────────────────────────────────────────
    private const int TagsMaxVisible = 3; // main list: show at most N tags, then "..."

    private static string BuildTagString(string assetId, BridgeSyncPayload p)
    {
        var tags = new List<string>(9);
        // Count Type as a change only when it differs from the cosmetic's original type — a redundant "type == original" sync (common for bridge cosmetics) must not look like a change.
        if (p.Type != null && OriginalTypeOf(assetId) is { } ot && p.Type.Value != ot) tags.Add("Type");
        if (p.EnableSway is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong) tags.Add("Sway");
        if (p.Offsets?.Count > 0)                                                   tags.Add("Offset");
        if (p.CustomTypes?.Count > 0)                                               tags.Add("Custom");
        if (p.Crown != null)                                                         tags.Add("Crown");
        if (p.ShowOnDeathHead == false || p.FloorPose != null)                       tags.Add("Death");
        if (p.FixAnimation != null)                                                  tags.Add("Anim");
        if (p.Tintable != null)                                                      tags.Add("Tint");
        if (p.HideConditions is { HasAny: true })                                    tags.Add("Hide");

        if (tags.Count == 0) return "";

        bool truncated = tags.Count > TagsMaxVisible;
        var visible = truncated ? tags.Take(TagsMaxVisible) : tags;
        string result = string.Join(" ", visible.Select(t => $"[{t}]"));
        if (truncated) result += " ...";
        return result;
    }

    // True when importing would show at least one row (a real synced field, or a Type reset). Keeps redundant-only payloads out of the importable list.
    private static bool HasImportableContent(string assetId, BridgeSyncPayload p)
    {
        CustomizerStore.TryGet(assetId, out var local);
        return BuildPreviewRows(assetId, p, local).Count > 0;
    }

    // ── Preview rows builder ──────────────────────────────────────────────────
    private static List<PreviewRow> BuildPreviewRows(string assetId, BridgeSyncPayload r, CosmeticOverrideData? l)
    {
        var rows = new List<PreviewRow>();

        // Type
        if (r.Type != null)
        {
            var remoteType = r.Type.Value;
            string rv = TypeLabel(remoteType);
            var original = OriginalTypeOf(assetId);
            bool remoteIsOriginal = original.HasValue && remoteType == original.Value;

            if (remoteIsOriginal)
            {
                // Remote type == the cosmetic's ORIGINAL: importing only matters if YOU override the type — it RESETS you to original. Nothing changed on either side → omit the row.
                bool localChanged = l?.Type != null && (!original.HasValue || l.Type.Value != original.Value);
                if (localChanged)
                    rows.Add(new($"Type: {rv}", StatusReset, ColReset));
            }
            else if (l?.Type == null)     rows.Add(new($"Type: {rv}", StatusNew, ColNew));
            else if (l.Type == r.Type)    rows.Add(new($"Type: {rv}", StatusSame, ColSame));
            else                          rows.Add(new($"Type: {rv}", string.Format(StatusDiffFmt, TypeLabel(l.Type.Value)), ColDiff));
        }

        // Sway
        if (r.EnableSway is not null)
        {
            string rv = SwayLabel(r.EnableSway.Value);
            if (l?.EnableSway == null)               rows.Add(new($"Sway: {rv}", StatusNew, ColNew));
            else if (l.EnableSway == r.EnableSway)   rows.Add(new($"Sway: {rv}", StatusSame, ColSame));
            else                                     rows.Add(new($"Sway: {rv}", string.Format(StatusDiffFmt, SwayLabel(l.EnableSway.Value)), ColDiff));
        }

        // Offsets — compare by VALUE (a same-count list with different pos/rot/scale must read ≠).
        if (r.Offsets is { Count: > 0 })
        {
            int rc = r.Offsets.Count;
            string rv = $"Offset: {rc}";
            if (l?.Offsets is not { Count: > 0 })          rows.Add(new(rv, StatusNew, ColNew));
            else if (OffsetsEqual(l.Offsets, r.Offsets))   rows.Add(new(rv, StatusSame, ColSame));
            else                                           rows.Add(new(rv, string.Format(StatusDiffFmt, l.Offsets.Count), ColDiff));
        }

        // Custom types — compare by VALUE (same count, different set must read ≠).
        if (r.CustomTypes is { Count: > 0 })
        {
            int rc = r.CustomTypes.Count;
            string rv = $"Custom: {rc}";
            if (l?.CustomTypes is not { Count: > 0 })            rows.Add(new(rv, StatusNew, ColNew));
            else if (EnumSetEqual(l.CustomTypes, r.CustomTypes)) rows.Add(new(rv, StatusSame, ColSame));
            else                                                 rows.Add(new(rv, string.Format(StatusDiffFmt, l.CustomTypes.Count), ColDiff));
        }

        // Crown — compare by VALUE (both "configured" but different transform/priority must read ≠).
        if (r.Crown != null)
        {
            if (l?.Crown == null)                  rows.Add(new("Crown: configured", StatusNew, ColNew));
            else if (CosmeticCrownConfig.ValueEquals(l.Crown, r.Crown, FloatEps)) rows.Add(new("Crown: configured", StatusSame, ColSame));
            else                                   rows.Add(new("Crown: configured", StatusDiffPlain, ColDiff));
        }

        // Death Head visibility
        if (r.ShowOnDeathHead == false)
        {
            bool lSame = l?.ShowOnDeathHead == false;
            rows.Add(new("Death Head: hidden", lSame ? StatusSame : StatusNew, lSame ? ColSame : ColNew));
        }

        // Impact Pose — compare by VALUE (both "configured" but different pose/react flags must read ≠).
        if (r.FloorPose != null)
        {
            if (l?.FloorPose == null)                      rows.Add(new("Impact Pose: configured", StatusNew, ColNew));
            else if (FloorPoseEqual(l.FloorPose, r.FloorPose)) rows.Add(new("Impact Pose: configured", StatusSame, ColSame));
            else                                           rows.Add(new("Impact Pose: configured", StatusDiffPlain, ColDiff));
        }

        // Fix Animation
        if (r.FixAnimation != null)
        {
            string rv = r.FixAnimation.Value ? "Loop: on" : "Loop: off";
            if (l?.FixAnimation == null)                  rows.Add(new($"Anim: {rv}", StatusNew, ColNew));
            else if (l.FixAnimation == r.FixAnimation)    rows.Add(new($"Anim: {rv}", StatusSame, ColSame));
            else
            {
                string lv = l.FixAnimation.Value ? "Loop: on" : "Loop: off";
                rows.Add(new($"Anim: {rv}", string.Format(StatusDiffFmt, lv), ColDiff));
            }
        }

        // Tintable
        if (r.Tintable != null)
        {
            string rv = r.Tintable.Value ? "Tint: on" : "Tint: off";
            if (l?.Tintable == null)              rows.Add(new($"{rv}", StatusNew, ColNew));
            else if (l.Tintable == r.Tintable)    rows.Add(new($"{rv}", StatusSame, ColSame));
            else
            {
                string lv = l.Tintable.Value ? "Tint: on" : "Tint: off";
                rows.Add(new($"{rv}", string.Format(StatusDiffFmt, lv), ColDiff));
            }
        }

        // Hide rules — compare by VALUE (same rule count, different types/conditions must read ≠).
        if (r.HideConditions is { HasAny: true } rh)
        {
            int rc = HideRuleCount(rh);
            var lh = l?.HideConditions is { HasAny: true } ? l.HideConditions : null;
            string rv = $"Hide: {rc} rule(s)";
            if (lh == null)            rows.Add(new(rv, StatusNew, ColNew));
            else if (HideEqual(lh, rh)) rows.Add(new(rv, StatusSame, ColSame));
            else
            {
                int lc = HideRuleCount(lh);
                rows.Add(new(rv, string.Format(StatusDiffFmt, $"{lc} rule(s)"), ColDiff));
            }
        }

        // World follow prefs — ride the payload but live in WorldFollowPrefs, not the override store.
        if (r.AvoidWalls == true)
        {
            bool lSame = WorldFollowPrefs.GetAvoidWalls(assetId);
            rows.Add(new("Avoid Walls: on", lSame ? StatusSame : StatusNew, lSame ? ColSame : ColNew));
        }
        if (r.HideOnKart == true)
        {
            bool lSame = WorldFollowPrefs.GetHideOnKart(assetId);
            rows.Add(new("Hide on Kart: on", lSame ? StatusSame : StatusNew, lSame ? ColSame : ColNew));
        }

        return rows;
    }

    // ── "Will be removed" rows ────────────────────────────────────────────────
    // Import is a FULL REPLACE (ImportBatch): anything local this payload doesn't carry gets WIPED — including the LOCAL-ONLY fields sync can't carry (rarity, border, physics, equip anim, colour toggles, isolated icon).
    // These warning rows show red, after the gain/change rows, ONLY in the preview popup.
    private static List<PreviewRow> BuildLossRows(string assetId, BridgeSyncPayload r, CosmeticOverrideData? l)
    {
        var rows = new List<PreviewRow>();
        void Lost(string field) => rows.Add(new(field, StatusRemove, ColRemove));

        // World follow prefs live outside the override store — check them even without a local record.
        if (WorldFollowPrefs.GetAvoidWalls(assetId) && r.AvoidWalls != true) Lost("Avoid Walls");
        if (WorldFollowPrefs.GetHideOnKart(assetId) && r.HideOnKart != true) Lost("Hide on Kart");

        if (l == null) return rows;

        // Synced fields you set locally that this payload leaves unset → wiped on import.
        var origT = OriginalTypeOf(assetId);
        bool localTypeChanged = l.Type != null && (!origT.HasValue || l.Type.Value != origT.Value);
        if (localTypeChanged && r.Type == null)                                    Lost($"Type: {TypeLabel(l.Type!.Value)}");
        if (l.EnableSway != null && r.EnableSway == null)                          Lost($"Sway: {SwayLabel(l.EnableSway.Value)}"); // includes None (native-sway suppression)
        if (l.Offsets is { Count: > 0 } && r.Offsets is not { Count: > 0 })        Lost($"Offset: {l.Offsets.Count}");
        if (l.CustomTypes is { Count: > 0 } && r.CustomTypes is not { Count: > 0 })Lost($"Custom: {l.CustomTypes.Count}");
        if (l.Crown != null && r.Crown == null)                                    Lost("Crown: configured");
        if (l.FloorPose != null && r.FloorPose == null)                           Lost("Impact Pose: configured");
        if (l.ShowOnDeathHead == false && r.ShowOnDeathHead != false)             Lost("Death Head: hidden");
        if (l.FixAnimation != null && r.FixAnimation == null)                     Lost($"Anim: {(l.FixAnimation.Value ? "Loop on" : "Loop off")}");
        if (l.Tintable != null && r.Tintable == null)                             Lost($"Tint: {(l.Tintable.Value ? "on" : "off")}");
        if (l.HideConditions is { HasAny: true } && r.HideConditions is not { HasAny: true }) Lost("Hide rules");

        // Local-only fields — the sync payload NEVER carries these, so import always drops them.
        if (l.Rarity != null)                    Lost($"Rarity: {l.Rarity.Value}");
        if (l.IsModded != null)                  Lost($"Border: {(l.IsModded.Value ? "on" : "off")}");
        if (l.FixCollider != null)               Lost("Remove Physics");
        if (l.FixCrown != null)                  Lost("Fix Crown");
        if (l.VanillaEquipAnimationMode != null) Lost($"Equip Anim: {l.VanillaEquipAnimationMode.Value}");
        if (l.EnableCustomColors != null)        Lost("Custom Colors");
        if (l.EnableColorAnimations != null)     Lost("Color Animations");
        if (l.UseIsolatedIcon != null)           Lost("Isolated Icon");
        if (l.UseFitOffsets != null)             Lost("Vanilla Position Fixes");

        return rows;
    }

    // Short names of the local fields this import would wipe — shown inline in the conflict popup rows.
    private static List<string> LostFieldNames(string assetId, BridgeSyncPayload r, CosmeticOverrideData? l)
    {
        var rows = BuildLossRows(assetId, r, l);
        var names = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            int colon = row.FieldText.IndexOf(':');
            names.Add(colon > 0 ? row.FieldText.Substring(0, colon) : row.FieldText);
        }
        return names;
    }

    // Friendly label for a type — same wording the sub-category slider shows. Raw enum names ("BodyTop") don't match the rest of the UI and defeat dictionary-based translation mods.
    private static string TypeLabel(OverrideCosmeticType t)
        => CosmeticOverridePopup.SubLabels.TryGetValue(t, out var label) ? label : t.ToString();

    private static string SwayLabel(SwayMode m) => m switch
    {
        SwayMode.Light    => "Light",
        SwayMode.Moderate => "Moderate",
        SwayMode.Strong   => "Strong",
        _                 => "None",
    };

    // ── Preview popup ─────────────────────────────────────────────────────────
    private static void ShowPreviewPopup(
        string assetId,
        BridgeSyncPayload remote,
        REPOPopupPage parentPopup,
        Action refresh)
    {
        CustomizerStore.TryGet(assetId, out var local);
        var previewRows = BuildPreviewRows(assetId, remote, local);
        // Append red "REMOVED" rows showing what importing would wipe (import is a full replace and silently drops local-only fields + anything they didn't set).
        previewRows.AddRange(BuildLossRows(assetId, remote, local));

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: DisplayName(assetId),
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: PopupSpacing,
            localPosition: new Vector2(PopupX, 0f));

        // Sub-popup over the Sync Customizer: FULL guard (input-block + local dimmer) so the parent can't be clicked through and is visibly dimmed.
        PopupUI.AttachGuards(popup, parentPopup.transform);

        bool firstRow = true;
        foreach (var pr in previewRows)
        {
            string capturedField  = pr.FieldText;
            string capturedStatus = pr.StatusText;
            Color  capturedColor  = pr.StatusColor;
            float  pad            = firstRow ? TitleGap : 2f;

            popup.AddElementToScrollView(sv =>
            {
                // Status is appended inline: "Type: Strong  <color=#A6FFA6>NEW</color>"
                string hex      = ColorUtility.ToHtmlStringRGB(capturedColor);
                string combined = $"{capturedField}  <color=#{hex}>{capturedStatus}</color>";

                var lbl = MenuAPI.CreateREPOLabel(combined, sv, new Vector2(PreviewFieldX, 0f));
                var tmp = lbl?.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.fontSize = PreviewFontSize;
                    tmp.richText = true;
                }
                return (RectTransform)lbl!.transform;
            }, topPadding: pad);

            firstRow = false;
        }

        if (previewRows.Count == 0)
        {
            popup.AddElementToScrollView(sv =>
            {
                var lbl = MenuAPI.CreateREPOLabel(LabelNoOverrideData, sv);
                return (RectTransform)lbl.transform;
            }, topPadding: TitleGap);
        }

        popup.AddElementToScrollView(sv =>
        {
            var row = PopupUI.MakeRow(sv);

            MenuAPI.CreateREPOButton(BtnCloseText, () => popup.ClosePage(false),
                row, new Vector2(BtnBackX, 0f));

            MenuAPI.CreateREPOButton(BtnImportText, () =>
            {
                popup.ClosePage(false);
                TryImportWithConflicts(
                    new Dictionary<string, BridgeSyncPayload> { [assetId] = remote },
                    parentPopup, refresh);
            }, row, new Vector2(BtnSaveX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        popup.OpenPage(openOnTop: true);
    }
}
