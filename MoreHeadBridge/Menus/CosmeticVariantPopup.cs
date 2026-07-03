// Variant picker popup (plain click on a grouped button). Variants are clones of sectionButtonPrefab so they look native; the live MenuElementCosmeticButton is DISABLED on clones (its Update pokes the real page's state), and VariantCell paints visuals + drives click/hover.
// Equip goes straight through MetaManager (same calls as vanilla ToggleCosmetic), unequipping siblings.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class CosmeticVariantPopup
{
    private static readonly MethodInfo? _updateIcon =
        AccessTools.Method(typeof(MenuElementCosmeticButton), "UpdateIcon");

    // The button caches its asset's list index in Start() and IsEquipped() reads it — after swapping the rep's cosmeticAsset we MUST refresh it, or the "selected" highlight checks the stale index.
    private static readonly FieldInfo? _cosmeticIndex =
        AccessTools.Field(typeof(MenuElementCosmeticButton), "cosmeticIndex");

    // Grid layout (tweak to taste): columns, cell side, horizontal step / start (centred), row height.
    private const int   Cols     = 4;
    private const float CellSize = 50f;
    private const float StepX    = 58f;
    private const float StartX   = -StepX * (Cols - 1) / 2f;
    private const float RowH     = 56f;

    // Vanilla button background colours (private Color32 on MenuElementCosmeticButton).
    private static readonly Color BgMainColor     = new Color32(0, 0, 0, 255);
    private static readonly Color BgSelectedColor = new Color32(0, 0, 0, 175);

    // Build deferred to mouse-release: a fresh page is ACTIVE before OpenPage, so the held click can interact with it early (see PopupUI.AfterMouseRelease).
    internal static void Show(MenuElementCosmeticButton repButton, CosmeticGroupButton group)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(repButton, group));

    private static void ShowNow(MenuElementCosmeticButton repButton, CosmeticGroupButton group)
    {
        if (group == null || !group.IsActive) return;
        var members = group.Members;

        // The button prefab + its page (for the prefab reference). Without it we can't build native-looking cells — bail rather than show something half-built.
        var page = repButton.cosmeticSection != null ? repButton.cosmeticSection.menuPageCosmetics : null;
        if (page == null || page.sectionButtonPrefab == null) return;

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: GroupTitle(members),
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: 5f,
            localPosition: new Vector2(-120f, 0f));

        PopupUI.AttachGuards(popup);
        popup.gameObject.AddComponent<VariantPreviewCleaner>();   // clear avatar preview when it closes

        for (int start = 0; start < members.Count; start += Cols)
        {
            int rowStart = start;
            popup.AddElementToScrollView(scrollView =>
            {
                var row = PopupUI.MakeRow(scrollView);
                row.sizeDelta = new Vector2(0f, RowH);
                int count = Mathf.Min(Cols, members.Count - rowStart);
                for (int col = 0; col < count; col++)
                {
                    var member = members[rowStart + col];
                    AddCosmeticCell(row, page, member, new Vector2(StartX + col * StepX, 0f),
                                    repButton, group, popup);
                }
                return row;
            }, topPadding: rowStart == 0 ? 15f : 6f);
        }

        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Back", () => popup.ClosePage(false), row, new Vector2(-137f, 0f));
            return row;
        }, topPadding: 36f);

        popup.OpenPage(openOnTop: false);
    }

    // Clones the game's cosmetic button prefab as a static, self-driven cell (no live component logic).
    private static void AddCosmeticCell(
        RectTransform row, MenuPageCosmetics page, CosmeticGrouping.Member member, Vector2 pos,
        MenuElementCosmeticButton repButton, CosmeticGroupButton group, REPOPopupPage popup)
    {
        try
        {
            var go = UnityEngine.Object.Instantiate(page.sectionButtonPrefab, row);
            var mecb = go.GetComponent<MenuElementCosmeticButton>();
            if (mecb == null) { UnityEngine.Object.Destroy(go); return; }

            mecb.cosmeticAsset = member.Asset;
            // Disable the LIVE logic component but keep MenuButton ENABLED — REPO uses MenuButton's own mouse polling (not Unity's EventSystem), so we reuse it for hover/click.
            mecb.enabled = false;
            if (mecb.tintableButton != null) mecb.tintableButton.gameObject.SetActive(false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CellSize, CellSize);
            rt.anchoredPosition = pos;

            // Icon.
            if (mecb.iconImage != null)
            {
                var sprite = member.Asset != null ? member.Asset.GetIcon() : null;
                if (sprite != null)
                {
                    mecb.iconImage.sprite = sprite;
                    if (sprite.texture != null) sprite.texture.filterMode = FilterMode.Point;
                }
                mecb.iconImage.color = Color.white;
                mecb.iconImage.gameObject.SetActive(true);
            }

            // Border base colour (themed or rarity); gradient themes also get their texture.
            Color baseColor = ResolveBorderBase(member.Asset, mecb.bgBorder);

            // Self-driven visuals (polls MenuButton.hovering) + click (MenuButton → ToggleCosmetic → FavHideTogglePatch intercept, which reads this VariantCell).
            var cell = go.AddComponent<VariantCell>();
            cell.Button = mecb.menuButton;
            cell.Border = mecb.bgBorder;
            cell.BgMain = mecb.bgMain;
            cell.BaseColor = baseColor;
            cell.Equipped = IsEquipped(member.Asset);
            var captured = member.Asset;
            cell.OnClick = () =>
            {
                EquipVariant(repButton, group, captured);
                popup.ClosePage(false);
            };
            cell.OnHoverEnter = () => PreviewVariant(group, captured);
            cell.OnHoverExit  = ClearPreview;
            cell.Refresh();
        }
        catch { /* one bad cell shouldn't break the picker */ }
    }

    private static Color ResolveBorderBase(CosmeticAsset? asset, RawImage? border)
    {
        if (asset == null) return Color.white;
        if (BorderTheme.TryResolve(asset, out var theme))
        {
            if (theme.HasSolid) return theme.Solid;
            if (border != null && theme.Gradient != null)
                border.texture = BorderTheme.GradientTexture(theme.Key, theme.Gradient);
            return Color.white;   // gradient texture shows its true colours
        }
        return asset.GetRarityColor();
    }

    // Equips <chosen>, unequipping any other equipped group member first (mutual exclusivity). Clicking the equipped variant toggles it off. Mirrors ToggleCosmetic's MetaManager calls.
    private static void EquipVariant(MenuElementCosmeticButton repButton, CosmeticGroupButton group, CosmeticAsset? chosen)
    {
        var meta = MetaManager.instance;
        if (meta == null || chosen == null) return;

        bool chosenEquipped = IsEquipped(chosen);

        // Positional args (this game version): CosmeticUnequip(asset, isPreview, resetColor) and CosmeticEquip(asset, isPreview). We Save() explicitly afterwards.
        foreach (var m in group.Members)
            if (IsEquipped(m.Asset))
                meta.CosmeticUnequip(m.Asset, false, false);

        if (!chosenEquipped)
            meta.CosmeticEquip(chosen, false);

        meta.Save();
        meta.CosmeticPreviewSet(false);
        meta.CosmeticPlayerUpdateLocal(false);

        // Show the newly-chosen variant on the representative button (unless we just toggled it off).
        if (!chosenEquipped)
        {
            repButton.cosmeticAsset = chosen;
            // Keep the cached index in sync so IsEquipped()/the selected highlight track the new variant.
            try { _cosmeticIndex?.SetValue(repButton, meta.cosmeticAssets.IndexOf(chosen)); } catch { }
        }
        try { _updateIcon?.Invoke(repButton, null); } catch { /* visual only */ }
    }

    private static bool IsEquipped(CosmeticAsset? asset)
    {
        var meta = MetaManager.instance;
        if (meta == null || asset == null) return false;
        int idx = meta.cosmeticAssets.IndexOf(asset);
        return idx >= 0 && meta.cosmeticEquipped.Contains(idx);
    }

    // ── Avatar hover preview ───────────────────────────────────────────────────
    // Hover preview only (no page/grabber poking): snapshot live equip, preview-unequip the other members, preview-equip the hovered variant, push to the avatar.
    private static void PreviewVariant(CosmeticGroupButton group, CosmeticAsset? asset)
    {
        var meta = MetaManager.instance;
        if (meta == null || asset == null) return;

        meta.cosmeticEquippedPreview = new List<int>(meta.cosmeticEquipped);
        meta.colorsEquippedPreview   = (int[])meta.colorsEquipped.Clone();

        foreach (var m in group.Members)
            if (IsEquipped(m.Asset))
                meta.CosmeticUnequip(m.Asset, true, false);   // preview-unequip the sibling

        meta.CosmeticEquip(asset, true);                       // preview-equip the hovered variant
        meta.CosmeticPreviewSet(true);
        meta.CosmeticPlayerUpdateLocal(false);
    }

    // Drops the avatar preview, restoring the real equipped look.
    internal static void ClearPreview()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;
        meta.CosmeticPreviewSet(false);
        meta.CosmeticPlayerUpdateLocal(false);
    }

    // ── Title ────────────────────────────────────────────────────────────────
    // Group display name = words common to ALL members (robust to the variant being any word), suffixed: RepoPride → " - Pride Colors", bridge packs → " - Colors".
    private static string GroupTitle(IReadOnlyList<CosmeticGrouping.Member> members)
    {
        var common = new List<string>(TitleWords(members[0].Asset));
        for (int i = 1; i < members.Count && common.Count > 0; i++)
        {
            var w = new HashSet<string>(TitleWords(members[i].Asset), StringComparer.OrdinalIgnoreCase);
            common.RemoveAll(x => !w.Contains(x));
        }

        string baseName = common.Count > 0 ? string.Join(" ", common) : CleanName(members[0].Asset);
        bool pride = (members[0].Asset?.assetId ?? "").StartsWith("repopride:", StringComparison.OrdinalIgnoreCase);
        return baseName + (pride ? " - Pride Colors" : " - Colors");
    }

    private static string[] TitleWords(CosmeticAsset? asset)
        => CleanName(asset).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    // A readable name: strip the "(variant)" token, any "~…" suffix and a leading "[Pack] ".
    private static string CleanName(CosmeticAsset? asset)
    {
        string? s = asset?.assetName;
        if (string.IsNullOrEmpty(s)) s = asset?.name ?? "Variants";
        s = Regex.Replace(s, @"\([^)]*\)", "").Trim();
        int tilde = s.IndexOf('~');
        if (tilde >= 0) s = s.Substring(0, tilde).Trim();
        s = Regex.Replace(s, @"^\[[^\]]*\]\s*", "").Trim();
        return string.IsNullOrEmpty(s) ? "Variants" : s;
    }
}
