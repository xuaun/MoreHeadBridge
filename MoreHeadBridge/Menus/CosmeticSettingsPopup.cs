// The "Sync Customizer": browse and import another room player's per-cosmetic settings — filter, preview each field vs your local override (NEW / same / differs), import one or all (with conflict confirmation).

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
    // ── Layout constants ──────────────────────────────────────────────────────
    private const float PopupX          = -120f;
    private const float TitleGap        = 15f;
    private const float BtnTopGap       = 10f;
    private const float BtnRowH         = 30f;
    private const float BtnBackX        = -137f;
    private const float BtnSaveX        =   56f;
    private const float BtnRefreshX     =  -63f;
    private const float BtnImportAllX   =   25f;
    private const float ItemLabelX      =  -40f;   // [main list] name button X (with-override rows)
    private const float ItemLabelY      =   5f;   // [main list] name button Y (with-override rows)
    private const float ItemLabelDisabledX = -55f; // [main list] dimmed name X (no-override rows) — was -80 (clipped left)
    private const float ItemFontSize    =   18f;
    private const float ItemDisabledAlpha = 0.35f;
    private const float PopupSpacing    =    5f;

    // Tags row — placed to the right of the name button
    private const float TagsX           =   40f;   // [main list] tag string X (e.g. "Type Offset Hide")
    private const float TagsFontSize    =   10f;

    // Preview popup layout (the sub-popup opened by a cosmetic name button)
    private const float PreviewFieldX   =  5f;   // [preview] field label X (e.g. "Type: Strong") — was -90 (off-screen left)
    private const float PreviewFontSize =   15f;

    // ── UI text constants ─────────────────────────────────────────────────────
    private const string OptionAll                = "All";
    private const string HeaderCosmeticSettings   = "Sync/Copy\nCosmetic Settings";
    private const string HeaderOverwriteFormat    = "Overwrite {0} override(s)?";
    private const string LabelNoOverrideData      = "(no override data)";
    private const string LabelMoreItemsFormat     = "...and {0} more";
    private const string BtnCancelText            = "Cancel";
    private const string BtnConfirmText           = "Confirm";
    private const string BtnImportText            = "Import";
    private const string BtnCloseText             = "Close";
    private const string BtnRefreshText           = "Refresh";
    private const string BtnImportAllText         = "Import All";

    // Preview status labels
    private const string StatusNew      = "NEW";
    private const string StatusSame     = "= same";
    private const string StatusDiffFmt  = "≠ yours: {0}";
    private const string StatusDiffPlain = "≠ yours";   // value-only fields (crown / impact pose) with no count to show
    private const string StatusReset    = "reset";   // remote value == cosmetic's original; import resets YOUR change
    private const string StatusRemove   = "REMOVED"; // you have this set, the import doesn't carry it → wiped

    // ── Preview row colours ───────────────────────────────────────────────────
    private static readonly Color ColNew    = new(0.65f, 1f,    0.65f); // soft green
    private static readonly Color ColSame   = new(0.5f,  0.5f,  0.5f);  // gray
    private static readonly Color ColDiff   = new(1f,    0.62f, 0.35f); // orange
    private static readonly Color ColReset  = new(0.6f,  0.8f,  1f);    // light blue (reverts to original)
    private static readonly Color ColRemove = new(1f,    0.4f,  0.4f);  // red (your setting will be wiped)

    // ── Lazy reflection for REPOLib.Objects.PlayerCosmeticsModded ────────────
    private static Type?      _moddedType;
    private static FieldInfo? _cosmeticEquippedField;

    private static void EnsureReflection()
    {
        if (_moddedType != null) return;
        _moddedType          = AccessTools.TypeByName("REPOLib.Objects.PlayerCosmeticsModded");
        _cosmeticEquippedField = _moddedType != null
            ? AccessTools.Field(_moddedType, "cosmeticEquipped")
            : null;
    }

    private static List<string> GetRemoteEquipped(int actorNumber)
    {
        EnsureReflection();
        if (_moddedType == null || _cosmeticEquippedField == null) return new();

        foreach (var pc in UnityEngine.Object.FindObjectsOfType<PlayerCosmetics>())
        {
            var pv = pc.photonView;
            if (pv == null || pv.Owner?.ActorNumber != actorNumber) continue;
            var moddedComp = pc.GetComponent(_moddedType);
            if (moddedComp == null) continue;
            return _cosmeticEquippedField.GetValue(moddedComp) as List<string> ?? new();
        }
        return new();
    }

    // ── Asset helpers ─────────────────────────────────────────────────────────
    private static CosmeticAsset? FindAsset(string assetId)
    {
        var meta = MetaManager.instance;
        if (meta == null) return null;
        return meta.cosmeticAssets.Find(a => a != null && a.assetId == assetId);
    }

    private static string DisplayName(string assetId)
    {
        var a = FindAsset(assetId);
        return !string.IsNullOrEmpty(a?.assetName) ? a!.assetName : a?.name ?? assetId;
    }

    // ── Category / filter helpers ─────────────────────────────────────────────
    private static MainCosmeticCategory? GetEffectiveMain(string assetId, BridgeSyncPayload? d)
    {
        if (d?.Type != null) return CustomizerStore.GetMainForType(d.Type.Value);
        var asset = FindAsset(assetId);
        return asset != null ? CustomizerStore.GetCurrentMain(asset) : null;
    }

    private static OverrideCosmeticType? GetEffectiveType(string assetId, BridgeSyncPayload? d)
    {
        if (d?.Type != null) return d.Type.Value;
        var asset = FindAsset(assetId);
        return asset != null ? CustomizerStore.GetEffectiveType(asset) : null;
    }

    // The cosmetic's ORIGINAL type (no Type override) — distinguishes a real Type change from a redundant one re-stating the original (e.g. a bridge cosmetic natively HEAD>HAT synced as Hat).
    private static OverrideCosmeticType? OriginalTypeOf(string assetId)
    {
        var asset = FindAsset(assetId);
        return asset != null ? CustomizerStore.GetOriginalType(asset) : (OverrideCosmeticType?)null;
    }

    private static string[] GetSubOptions(MainCosmeticCategory main)
    {
        var types  = CosmeticOverridePopup.SubOptions[main];
        var labels = new string[types.Length + 1];
        labels[0]  = OptionAll;
        for (int i = 0; i < types.Length; i++)
            labels[i + 1] = CosmeticOverridePopup.SubLabels[types[i]];
        return labels;
    }

    private static bool MatchesFilter(
        string assetId, BridgeSyncPayload? d,
        MainCosmeticCategory? category, OverrideCosmeticType? subCategory)
    {
        if (category == null) return true;
        var main = GetEffectiveMain(assetId, d);
        if (main == null) return false;
        if (main.Value != category.Value) return false;
        if (subCategory == null) return true;
        return GetEffectiveType(assetId, d) == subCategory;
    }

    // Entries "Import All" may take: exactly what the list shows as importable — asset present locally, matches the current filter, and the payload actually carries something.
    private static Dictionary<string, BridgeSyncPayload> FilterImportable(
        Dictionary<string, BridgeSyncPayload> data,
        MainCosmeticCategory? category, OverrideCosmeticType? subCategory)
    {
        var result = new Dictionary<string, BridgeSyncPayload>();
        foreach (var kvp in data)
            if (FindAsset(kvp.Key) != null
                && MatchesFilter(kvp.Key, kvp.Value, category, subCategory)
                && HasImportableContent(kvp.Key, kvp.Value))
                result[kvp.Key] = kvp.Value;
        return result;
    }

    // ── Row factory ───────────────────────────────────────────────────────────
    private static RectTransform CreateCosmeticRow(Transform scroller, float topPadding = 0f)
    {
        var row = new GameObject("Cosmetic Row", typeof(RectTransform))
            .GetComponent<RectTransform>();
        row.sizeDelta = new Vector2(0f, BtnRowH);
        row.SetParent(scroller, false);
        var el = row.gameObject.AddComponent<REPOScrollViewElement>();
        el.topPadding = topPadding;
        return row;
    }

    // ── Import with conflict check ────────────────────────────────────────────
    private static void TryImportWithConflicts(
        Dictionary<string, BridgeSyncPayload> batch,
        REPOPopupPage parentPopup,
        Action refresh)
    {
        // One row per conflicting cosmetic; local fields the import would WIPE are appended in red so removals are visible before confirming.
        var conflicts = new List<string>();
        string removeHex = ColorUtility.ToHtmlStringRGB(ColRemove);
        foreach (var kvp in batch)
        {
            bool hasRecord = CustomizerStore.TryGet(kvp.Key, out var local);
            bool hasWorldPrefs = WorldFollowPrefs.GetAvoidWalls(kvp.Key) || WorldFollowPrefs.GetHideOnKart(kvp.Key);
            if (!hasRecord && !hasWorldPrefs) continue;
            string name = DisplayName(kvp.Key);
            var lost = LostFieldNames(kvp.Key, kvp.Value, local);
            if (lost.Count > 4) lost = lost.Take(4).Append("…").ToList();
            conflicts.Add(lost.Count == 0
                ? name
                : $"{name} <color=#{removeHex}>− {string.Join(", ", lost)}</color>");
        }

        var asOverrideData = batch.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToOverrideData());

        void DoImport()
        {
            // Avoid Walls / Hide on Kart ride the payload but live in WorldFollowPrefs — apply them here
            // (full replace: absent = off), BEFORE ImportBatch so its BroadcastAll carries the new values.
            foreach (var kvp in batch)
            {
                WorldFollowPrefs.SetAvoidWalls(kvp.Key, kvp.Value.AvoidWalls == true);
                WorldFollowPrefs.SetHideOnKart(kvp.Key, kvp.Value.HideOnKart == true);
            }
            CustomizerStore.ImportBatch(asOverrideData);
            refresh();
        }

        if (conflicts.Count == 0)
        {
            DoImport();
            return;
        }

        ShowConflictPopup(conflicts, parentPopup, DoImport);
    }

    private static void ShowConflictPopup(List<string> conflictNames, REPOPopupPage parentPopup, Action onConfirm)
    {
        string header = string.Format(HeaderOverwriteFormat, conflictNames.Count);
        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: header,
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: PopupSpacing,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopup.transform);

        int shown = 0;
        foreach (var name in conflictNames)
        {
            if (shown >= 8) break;
            string captured = name;
            popup.AddElementToScrollView(sv =>
            {
                var lbl = MenuAPI.CreateREPOLabel(captured, sv);
                return (RectTransform)lbl.transform;
            }, topPadding: shown == 0 ? TitleGap : 2f);
            shown++;
        }

        if (conflictNames.Count > 8)
        {
            int remaining = conflictNames.Count - 8;
            popup.AddElementToScrollView(sv =>
            {
                var lbl = MenuAPI.CreateREPOLabel(string.Format(LabelMoreItemsFormat, remaining), sv);
                return (RectTransform)lbl.transform;
            }, topPadding: 2f);
        }

        popup.AddElementToScrollView(sv =>
        {
            var row = PopupUI.MakeRow(sv);
            MenuAPI.CreateREPOButton(BtnCancelText, () => popup.ClosePage(false),
                row, new Vector2(BtnBackX, 0f));
            MenuAPI.CreateREPOButton(BtnConfirmText, () =>
            {
                popup.ClosePage(false);
                onConfirm();
            }, row, new Vector2(BtnSaveX, 0f));
            return row;
        }, topPadding: BtnTopGap);

        popup.OpenPage(openOnTop: true);
    }

    // ── Main popup ────────────────────────────────────────────────────────────
    // Build deferred to mouse-release: a fresh page is ACTIVE before OpenPage, so a REPOSlider landing under the still-held click gets scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show()
        => PopupUI.AfterMouseRelease(Plugin.Instance, ShowNow);

    private static void ShowNow()
    {
        var players = CustomizerSync.GetRemotePlayersWithData();
        if (players.Count == 0) return;

        // Mutable state — reassigned inside local functions, seen by all closures.
        int currentActor = players[0].actorNumber;
        MainCosmeticCategory?  currentCategory    = null;
        OverrideCosmeticType?  currentSubcategory = null;

        string[] playerOptions = BuildPlayerOptions(players);
        Dictionary<string, int> actorByOption = BuildActorByOption(players);

        REPOSlider? playerSlider = null;
        REPOSlider? subSlider    = null;
        REPOScrollViewElement? noEntriesEl = null;
        Transform? noEntriesTr = null;
        var cosmeticRows = new List<GameObject>();
        REPOButton? refreshBtn = null;
        REPOButton? importAllBtn = null;
        bool anyImportable = false;
        Action<int>? dataChangedHandler = null;
        REPOPopupPage popup = null!;

        // ── Player option builders ────────────────────────────────────────────
        static string[] BuildPlayerOptions(
            List<(int actorNumber, string nickName, int overrideCount, bool isSteamFriend)> list)
            => list.Select(p =>
                p.isSteamFriend
                    ? $"★ {p.nickName} ({p.overrideCount})"
                    : $"{p.nickName} ({p.overrideCount})")
                .ToArray();

        static Dictionary<string, int> BuildActorByOption(
            List<(int actorNumber, string nickName, int overrideCount, bool isSteamFriend)> list)
            => list.ToDictionary(
                p => p.isSteamFriend
                    ? $"★ {p.nickName} ({p.overrideCount})"
                    : $"{p.nickName} ({p.overrideCount})",
                p => p.actorNumber);

        // ── Refresh player slider + actorByOption ─────────────────────────────
        void RefreshPlayerOptions()
        {
            var updated = CustomizerSync.GetRemotePlayersWithData();
            if (updated.Count == 0)
            {
                CustomizerSync.OnRemoteDataChanged -= dataChangedHandler;
                popup.ClosePage(false);
                return;
            }

            playerOptions  = BuildPlayerOptions(updated);
            actorByOption  = BuildActorByOption(updated);

            bool stillPresent = updated.Any(p => p.actorNumber == currentActor);
            if (!stillPresent) currentActor = updated[0].actorNumber;

            if (playerSlider != null)
            {
                playerSlider.stringOptions = playerOptions;
                int idx = Array.FindIndex(playerOptions, opt =>
                    actorByOption.TryGetValue(opt, out int a) && a == currentActor);
                playerSlider.SetValue(idx >= 0 ? idx : 0, invokeCallback: false);
            }
        }

        // ── Rebuild cosmetic list ─────────────────────────────────────────────
        void Rebuild()
        {
            foreach (var go in cosmeticRows)
            {
                if (go == null) continue;
                var el = go.GetComponent<REPOScrollViewElement>();
                if (el != null) el.visibility = false;
                UnityEngine.Object.Destroy(go);
            }
            cosmeticRows.Clear();

            var remoteOverrides = CustomizerSync.GetRemotePlayerData(currentActor);
            var remoteEquipped  = GetRemoteEquipped(currentActor);

            var overrideIds = remoteOverrides?.Keys ?? Enumerable.Empty<string>();
            var allIds = new HashSet<string>(overrideIds);
            allIds.UnionWith(remoteEquipped);

            var filtered = allIds
                .Where(id => FindAsset(id) != null)
                .Where(id =>
                {
                    var data = remoteOverrides?.GetValueOrDefault(id);
                    return MatchesFilter(id, data, currentCategory, currentSubcategory);
                })
                .OrderBy(id => FindAsset(id)?.assetName ?? id)
                .ToList();

            if (noEntriesEl != null)
                noEntriesEl.visibility = filtered.Count == 0;

            // Import All only makes sense when at least one visible row is importable.
            anyImportable = remoteOverrides != null && filtered.Any(id =>
                remoteOverrides.TryGetValue(id, out var p) && HasImportableContent(id, p));
            importAllBtn?.gameObject.SetActive(anyImportable);

            if (filtered.Count == 0)
            {
                popup.scrollView.UpdateElements();
                return;
            }

            int insertAt  = noEntriesTr!.GetSiblingIndex();
            var scroller  = popup.menuScrollBox.scroller;

            for (int i = 0; i < filtered.Count; i++)
            {
                string assetId     = filtered[i];
                // Importable only when the payload has something for YOU — a lone redundant "type == original" is not an override; a Type reset opportunity still counts.
                bool   hasOverride = remoteOverrides != null
                                     && remoteOverrides.TryGetValue(assetId, out var rowPayload)
                                     && HasImportableContent(assetId, rowPayload);
                string name        = DisplayName(assetId);
                float  padding     = i == 0 ? 5f : 0f;

                var row = CreateCosmeticRow(scroller, padding);
                row.SetSiblingIndex(insertAt + i);

                if (hasOverride)
                {
                    string capturedId = assetId;

                    // [Import] — direct import with conflict check
                    MenuAPI.CreateREPOButton(BtnImportText, () =>
                    {
                        var overrides = CustomizerSync.GetRemotePlayerData(currentActor);
                        if (overrides == null || !overrides.TryGetValue(capturedId, out var d)) return;
                        TryImportWithConflicts(
                            new Dictionary<string, BridgeSyncPayload> { [capturedId] = d },
                            popup, Rebuild);
                    }, row, new Vector2(BtnBackX, 0f));

                    // [Name button] — opens preview popup
                    var nameBtn = MenuAPI.CreateREPOButton(name, () =>
                    {
                        var overrides = CustomizerSync.GetRemotePlayerData(currentActor);
                        if (overrides == null || !overrides.TryGetValue(capturedId, out var d)) return;
                        ShowPreviewPopup(capturedId, d, popup, Rebuild);
                    }, row, new Vector2(ItemLabelX, ItemLabelY));
                    var nameTmp = nameBtn?.GetComponentInChildren<TextMeshProUGUI>();
                    if (nameTmp != null) nameTmp.fontSize = ItemFontSize;

                    // Tags label
                    var payload = remoteOverrides![assetId];
                    string tags = BuildTagString(assetId, payload);
                    if (!string.IsNullOrEmpty(tags))
                    {
                        var tagsLbl = MenuAPI.CreateREPOLabel(tags, row, new Vector2(TagsX, 0f));
                        var tagsTmp = tagsLbl?.GetComponentInChildren<TextMeshProUGUI>();
                        if (tagsTmp != null)
                            tagsTmp.fontSize = TagsFontSize;
                        // no color override — same default white as field labels
                    }
                }
                else
                {
                    var lbl = MenuAPI.CreateREPOLabel(name, row, new Vector2(ItemLabelDisabledX, 0f));
                    var tmp = lbl?.GetComponentInChildren<TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        tmp.color    = new Color(1f, 1f, 1f, ItemDisabledAlpha);
                        tmp.fontSize = ItemFontSize;
                    }
                }

                cosmeticRows.Add(row.gameObject);
            }

            popup.scrollView.UpdateElements();
        }

        // ── Create popup ──────────────────────────────────────────────────────
        popup = MenuAPI.CreateREPOPopupPage(
            headerText: HeaderCosmeticSettings,
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: PopupSpacing,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, inputGuard: false);

        // ── Player slider ─────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            playerSlider = MenuAPI.CreateREPOSlider(
                "Player", "",
                opt =>
                {
                    if (actorByOption.TryGetValue(opt, out int actor))
                        currentActor = actor;
                    Rebuild();
                },
                scrollView, playerOptions, playerOptions[0]);
            return (RectTransform)playerSlider.transform;
        }, topPadding: TitleGap);

        // ── Category slider ───────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                "Category", "",
                opt =>
                {
                    currentCategory    = opt == OptionAll ? null : Enum.Parse<MainCosmeticCategory>(opt);
                    currentSubcategory = null;

                    if (subSlider != null)
                    {
                        var subEl = subSlider.GetComponent<REPOScrollViewElement>();
                        if (currentCategory == null || currentCategory == MainCosmeticCategory.World)
                        {
                            if (subEl != null) subEl.visibility = false;
                        }
                        else
                        {
                            subSlider.stringOptions = GetSubOptions(currentCategory.Value);
                            subSlider.SetValue(0, invokeCallback: false);
                            if (subEl != null) subEl.visibility = true;
                        }
                    }
                    Rebuild();
                },
                scrollView,
                new[] { OptionAll }.Concat(Enum.GetNames(typeof(MainCosmeticCategory))).ToArray(),
                OptionAll);
            return (RectTransform)slider.transform;
        });

        // ── Sub Category slider ───────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            subSlider = MenuAPI.CreateREPOSlider(
                "Sub Category", "",
                opt =>
                {
                    currentSubcategory = opt == OptionAll ? null
                        : CosmeticOverridePopup.LabelToType.TryGetValue(opt, out var t) ? t : null;
                    Rebuild();
                },
                scrollView, new[] { OptionAll }, OptionAll);
            return (RectTransform)subSlider.transform;
        });

        // Sub Category starts hidden (Category = All)
        {
            var el = subSlider?.GetComponent<REPOScrollViewElement>();
            if (el != null) el.visibility = false;
        }

        // ── Placeholder: "no data" ────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var lbl = MenuAPI.CreateREPOLabel(LabelNoOverrideData, scrollView);
            noEntriesTr = lbl.transform;
            return (RectTransform)lbl.transform;
        }, topPadding: 10f);

        noEntriesEl = noEntriesTr!.GetComponent<REPOScrollViewElement>();

        // ── Bottom buttons ────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton(BtnCloseText, () =>
            {
                CustomizerSync.OnRemoteDataChanged -= dataChangedHandler;
                popup.ClosePage(false);
            }, row, new Vector2(BtnBackX, 0f));

            // Refresh — hidden until remote data changes while popup is open
            refreshBtn = MenuAPI.CreateREPOButton(BtnRefreshText, () =>
            {
                RefreshPlayerOptions();
                if (popup != null) // still open (RefreshPlayerOptions may close it)
                {
                    refreshBtn!.gameObject.SetActive(false);
                    Rebuild();
                }
            }, row, new Vector2(BtnRefreshX, 0f));
            refreshBtn.gameObject.SetActive(false);

            // Import All — hidden whenever the current list has nothing importable
            importAllBtn = MenuAPI.CreateREPOButton(BtnImportAllText, () =>
            {
                var remoteData = CustomizerSync.GetRemotePlayerData(currentActor);
                if (remoteData == null || remoteData.Count == 0) return;
                var filteredData = FilterImportable(remoteData, currentCategory, currentSubcategory);
                if (filteredData.Count == 0) return;
                TryImportWithConflicts(filteredData, popup, Rebuild);
            }, row, new Vector2(BtnImportAllX, 0f));
            importAllBtn.gameObject.SetActive(anyImportable);

            return row;
        }, topPadding: BtnTopGap);

        // ── Event subscription: show Refresh button when data changes ─────────
        dataChangedHandler = _ =>
        {
            if (popup == null) // popup destroyed — clean up stale subscription
            {
                CustomizerSync.OnRemoteDataChanged -= dataChangedHandler!;
                return;
            }
            if (refreshBtn != null)
                refreshBtn.gameObject.SetActive(true);
        };
        CustomizerSync.OnRemoteDataChanged += dataChangedHandler;

        // Initial population before OpenPage so layout is correct on first frame.
        Rebuild();

        popup.OpenPage(openOnTop: false);
    }
}
