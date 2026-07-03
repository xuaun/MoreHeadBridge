// Variant grouping: families differing only by a variant token (RepoPride flag / MoreHead pack colour) collapse into one button (2+ members) opening CosmeticVariantPopup. Presentation only — cosmetics equip normally.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticGrouping
{
    internal readonly struct Member
    {
        internal readonly CosmeticAsset Asset;
        internal readonly string Variant;   // label shown in the picker (e.g. "Ace", "brown")
        internal Member(CosmeticAsset a, string v) { Asset = a; Variant = v; }
    }

    internal static bool Enabled => Plugin.MenuLibAvailable && Plugin.GroupCosmeticVariants.Value;

    // RepoPride flag tokens (lowercase) → display label. Longest-first so "newpride" beats "pride" / "agender" beats "aro" when matching the prefix after the 3-letter type token.
    private static readonly (string token, string label)[] PrideFlags =
    {
        ("newpride", "Progress"), ("agender", "Agender"), ("intersex", "Intersex"),
        ("lesbian", "Lesbian"), ("trans", "Trans"), ("enby", "Non-binary"),
        ("pride", "Rainbow"), ("ace", "Ace"), ("aro", "Aro"), ("pan", "Pan"), ("bi", "Bi"),
    };

    // (groupKey, variant label) for a cosmetic that belongs to a variant family, else false.
    private static bool TryGetInfo(CosmeticAsset asset, out string groupKey, out string variant)
    {
        groupKey = ""; variant = "";
        if (asset == null) return false;

        // RepoPride: "repopride:" + 3-letter type + flag token + style. Group ignores the flag.
        string id = asset.assetId ?? "";
        const string pride = "repopride:";
        if (id.StartsWith(pride, StringComparison.OrdinalIgnoreCase) && id.Length > pride.Length + 3)
        {
            string rest = id.Substring(pride.Length);
            string type = rest.Substring(0, 3);
            string afterType = rest.Substring(3);
            foreach (var (token, label) in PrideFlags)
                if (afterType.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    string style = afterType.Substring(token.Length);
                    groupKey = $"{pride}{type}|{style}";   // type + style, flag removed
                    variant = label;
                    return true;
                }
        }

        // Bridge packs (BASICS-style): a parenthesised colour/variant token in the name. Group key = base name (rest) + type, so only the colour separates members.
        if (BridgeIds.IsBridgeAsset(asset))
        {
            string name = asset.name ?? "";
            int lp = name.IndexOf('(');
            int rp = lp >= 0 ? name.IndexOf(')', lp + 1) : -1;
            if (lp >= 0 && rp > lp)
            {
                variant = name.Substring(lp + 1, rp - lp - 1).Trim();
                string baseName = (name.Substring(0, lp) + name.Substring(rp + 1)).Replace("  ", " ").Trim();
                groupKey = $"bridge:{(int)asset.type}|{baseName.ToLowerInvariant()}";
                return variant.Length > 0;
            }
        }
        return false;
    }

    // One entry per group, order preserved: members == null → plain singleton; members != null → grouped button whose rep is the equipped member (or first by label).
    internal static List<(CosmeticAsset rep, List<Member>? members)> Collapse(IEnumerable<CosmeticAsset> assets)
    {
        var result = new List<(CosmeticAsset, List<Member>?)>();
        if (!Enabled)
        {
            foreach (var a in assets) result.Add((a, null));
            return result;
        }

        var slotByKey = new Dictionary<string, int>();   // group key → its index in result

        // Currently-equipped assets resolved once — avoids an O(n) IndexOf per member on the 1000+ asset list when picking each group's representative.
        var equippedAssets = new HashSet<CosmeticAsset>();
        var meta = MetaManager.instance;
        if (meta != null)
            foreach (int idx in meta.cosmeticEquipped)
                if (idx >= 0 && idx < meta.cosmeticAssets.Count && meta.cosmeticAssets[idx] != null)
                    equippedAssets.Add(meta.cosmeticAssets[idx]);

        foreach (var a in assets)
        {
            if (a != null && TryGetInfo(a, out var key, out var variant))
            {
                if (!slotByKey.TryGetValue(key, out int slot))
                {
                    slot = result.Count;
                    slotByKey[key] = slot;
                    result.Add((a, new List<Member>()));
                }
                result[slot].Item2!.Add(new Member(a, variant));
            }
            else
            {
                result.Add((a!, null));
            }
        }

        // Finalise: 1 member → plain singleton; 2+ → sort variants + pick the representative.
        for (int i = 0; i < result.Count; i++)
        {
            var members = result[i].Item2;
            if (members == null) continue;
            if (members.Count == 1) { result[i] = (members[0].Asset, null); continue; }

            members.Sort((x, y) => string.Compare(x.Variant, y.Variant, StringComparison.OrdinalIgnoreCase));
            CosmeticAsset chosen = members[0].Asset;
            foreach (var m in members)
                if (equippedAssets.Contains(m.Asset)) { chosen = m.Asset; break; }
            result[i] = (chosen, members);
        }
        return result;
    }
}

// Marks a cosmetic button as a variant-group representative and carries its members. Added by the scroll builder; the click patch checks for it to open the picker, which reads the members.
internal sealed class CosmeticGroupButton : MonoBehaviour
{
    internal List<CosmeticGrouping.Member> Members = new();
    internal bool IsActive => Members != null && Members.Count > 1;

    internal static void Attach(MenuElementCosmeticButton btn, List<CosmeticGrouping.Member> members)
    {
        var c = btn.GetComponent<CosmeticGroupButton>() ?? btn.gameObject.AddComponent<CosmeticGroupButton>();
        c.Members = members;
        c.AddCountBadge(btn, members.Count);
    }

    // Small "N" badge in the top-right corner so the player knows the button is a multi-variant group.
    private void AddCountBadge(MenuElementCosmeticButton btn, int count)
    {
        try
        {
            if (btn.transform.Find("MHB_GroupCount") != null) return;
            var refTmp = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);

            var go = new GameObject("MHB_GroupCount", typeof(RectTransform));
            go.transform.SetParent(btn.transform, false);
            var rt = (RectTransform)go.transform;
            // Top-LEFT corner (top-right holds the colour swatch), nudged slightly in/down. anchoredPosition: +x = inward, -y = down.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(7f, -4f);
            rt.sizeDelta = new Vector2(26f, 14f);

            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (refTmp != null) tmp.font = refTmp.font;
            tmp.text = count.ToString();
            tmp.fontSize = 10f;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }
        catch { /* badge is cosmetic-only — never let it break the menu */ }
    }
}
