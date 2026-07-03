using System.Collections;
using UnityEngine;

namespace MoreHeadBridge;

// The "Original" button in the colour picker: replicates MenuButtonColor without a MetaManager.colors index. On click: confirm sound, move the ring, store OriginalColorSentinel (-1), RestoreOriginalColor() on live BridgeTintMaterials, finalise. Opened in original mode → initiallySelected + Start() places the indicator.
internal sealed class BridgeOriginalColorButton : MonoBehaviour
{
    // White mixed into the click flash (0.95 = vanilla swatch intensity). Flash DURATION is the shared vanilla MenuButton.clickTimer (0.2 s) — not adjustable here.
    internal const float ClickWhiteAmount = 0.95f;

    // Seconds for the "M" to fade back in after the click flash (pops above the ring, fades 0→1).
    private const float PopFadeDuration = 0.1f;

    internal MenuPageColor? menuPageColor;
    internal CosmeticAsset? cosmeticAsset;

    // Original primary colour from a live BridgeTintMaterial, to tint the button. Standard/Unlit cosmetics have white _Color (look from the albedo) — a white button means "no tint".
    internal Color originalColor = Color.white;

    // Set true by OriginalColorButtonPatch when the asset is already in original mode, so Start() positions the indicator without a click.
    internal bool initiallySelected;

    // Section-mode: clicking restores original colours for all eligible bridge cosmetics matching the section filter (colorKey/pageMode), not just one.
    internal bool sectionMode;
    internal int sectionColorKey;
    internal MenuPageColor.ColorPageType sectionPageMode;

    private MenuButton? _menuButton;
    private bool _buttonClicked;
    private bool _flashSunk;   // true while the "M" is temporarily below the ring (during a flash)
    private CanvasGroup? _canvasGroup;
    private float _fadeT = -1f;   // ≥ 0 while the pop-back fade-in is running

    private void Awake()
    {
        _menuButton = GetComponent<MenuButton>();
    }

    // Re-tints the button to a slot's original colour (on active-slot change, so it previews what it would restore).
    internal void SetDisplayColor(Color c)
    {
        originalColor = c;
        _menuButton ??= GetComponent<MenuButton>();
        if (_menuButton == null) return;
        _menuButton.colorNormal = c + Color.black * 0.5f;   // vanilla swatch rest tint
        _menuButton.colorHover = c;
        _menuButton.colorClick = c + Color.white * ClickWhiteAmount;   // re-primed per-frame in Update
    }

    private IEnumerator Start()
    {
        if (!initiallySelected) yield break;

        // Mirror the timing of MenuButtonColor.LateStart() so we fight for the same slot.
        yield return new WaitForSeconds(0.1f);

        // Wait until the page becomes active (it may be animating in).
        var page = GetComponentInParent<MenuPage>();
        if (page != null)
            while (page.currentPageState != MenuPage.PageState.Active)
                yield return new WaitForSeconds(0.1f);

        if (menuPageColor == null) yield break;

        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        var rt = GetComponent<RectTransform>();
        var pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.SetColor(originalColor, pos);
    }

    private void Update()
    {
        if (_menuButton == null) return;

        bool clicked = _menuButton.clicked;

        // During a selecting click, drop below the ring so its fill hides the white flash (like vanilla swatches); pop back on top when the click animation ends.
        if (clicked)
        {
            if (!_flashSunk && !IsCurrentlySelected()) { _flashSunk = true; SinkBelowRing(); }
        }
        else if (_flashSunk)
        {
            _flashSunk = false;
            transform.SetAsLastSibling();
            BeginPopFade();
        }

        if (_fadeT >= 0f)
        {
            _fadeT += Time.deltaTime;
            float a = Mathf.Clamp01(_fadeT / PopFadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = a;
            if (a >= 1f) _fadeT = -1f;
        }

        if (!clicked)
        {
            _buttonClicked = false;
            // The white "select" flash only plays when not already selected — mirrors vanilla swatches.
            _menuButton.colorClick = IsCurrentlySelected()
                ? originalColor
                : originalColor + Color.white * ClickWhiteAmount;
            return;
        }

        if (_buttonClicked) return;
        _buttonClicked = true;

        if (menuPageColor == null) return;

        MenuManager.instance.MenuEffectClick(MenuManager.MenuClickEffectType.Confirm);

        if (sectionMode)
        {
            menuPageColor.menuColorSelected.gameObject.SetActive(true);
            var rtSec = GetComponent<RectTransform>();
            var posSec = rtSec.position + new Vector3(rtSec.rect.width / 2f, rtSec.rect.height / 2f, 0f);
            menuPageColor.menuColorSelected.SetColor(originalColor, posSec);

            ApplyOriginalToSection();
            MetaManager.instance.colorsPreviewEnabled = false;
            MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
            return;
        }

        if (cosmeticAsset == null) return;

        // Restoring the original color stops the active animation — the active slot in slot mode, or the whole asset (every slot) in ALL mode.
        ColorAnimatorRefresher.StopAnimation(cosmeticAsset.assetId, PerCosmeticColors.ActiveSlot);

        menuPageColor.menuColorSelected.gameObject.SetActive(true);
        var rt = GetComponent<RectTransform>();
        var pos = rt.position + new Vector3(rt.rect.width / 2f, rt.rect.height / 2f, 0f);
        menuPageColor.menuColorSelected.SetColor(originalColor, pos);

        // Persist the sentinel and apply to live BTMs. Slot mode: only the active slot; ALL mode: whole-asset sentinel (SetOriginalColor also clears slots).
        int activeSlot = PerCosmeticColors.ActiveSlot;
        if (activeSlot >= 0)
        {
            PerCosmeticColors.SetSlotColor(cosmeticAsset.assetId, activeSlot, PerCosmeticColors.OriginalColorSentinel);
            BridgeTintHelper.ApplySlotColorToLiveInstances(cosmeticAsset, activeSlot, PerCosmeticColors.OriginalColorSentinel);
        }
        else
        {
            PerCosmeticColors.SetOriginalColor(cosmeticAsset.assetId);
            ApplyToLiveInstances(cosmeticAsset);
        }

        // 3. Re-tint the slot selector so each slot shows its own original colour again (clearing the override falls back to per-slot originals).
        BridgeSlotSelectorRow.Active?.Refresh();

        MetaManager.instance.colorsPreviewEnabled = false;
        MetaManager.instance.CosmeticPlayerUpdateLocal(_synced: false);
    }

    // Moves the button just below the selection ring (siblings under colorButtonHolder) so the ring renders over it. No-op if no ring.
    private void SinkBelowRing()
    {
        var ring = menuPageColor?.menuColorSelected;
        if (ring == null) return;
        transform.SetSiblingIndex(ring.transform.GetSiblingIndex());
    }

    // Starts the alpha 0→1 fade used when the button pops back above the ring.
    private void BeginPopFade()
    {
        _canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _fadeT = 0f;
    }

    // True when this button is the current selection (active slot / whole asset in original mode). Section mode returns false so it flashes on use.
    private bool IsCurrentlySelected()
    {
        if (sectionMode || cosmeticAsset == null) return false;

        int activeSlot = PerCosmeticColors.ActiveSlot;
        if (PerCosmeticColors.IsSlotAnimated(cosmeticAsset.assetId, activeSlot)) return false;     // "A" owns it
        if (PerCosmeticColors.IsSlotCustom(cosmeticAsset.assetId, activeSlot)) return false;        // "C" owns it

        if (activeSlot >= 0 && PerCosmeticColors.TryGetSlotColor(cosmeticAsset.assetId, activeSlot, out int slotIdx))
            return slotIdx == PerCosmeticColors.OriginalColorSentinel;

        // ALL, or a slot inheriting the whole-asset colour → original if no real colour is stored.
        return !PerCosmeticColors.TryGetColor(cosmeticAsset.assetId, out int idx)
               || idx == PerCosmeticColors.OriginalColorSentinel;
    }

    // GLOBAL scan (not just the menu avatar) so the wearer's Mini-Semibot reverts too. Preset minis skipped (keep their rolled preset colours).
    private static void ApplyToLiveInstances(CosmeticAsset asset)
    {
        foreach (var btm in FindObjectsOfType<BridgeTintMaterial>(true))
        {
            if (btm?.cosmetic?.cosmeticAsset != asset) continue;
            if (MiniSemibotSpawner.IsPresetMiniComponent(btm)) continue; // preset minis keep the preset's colours
            btm.RestoreOriginalColor();
        }
    }

    private void ApplyOriginalToSection()
    {
        var meta = MetaManager.instance;
        if (meta?.cosmeticEquipped == null) return;

        // ── Per-cosmetic overrides ────────────────────────────────────────────
        bool anySlots = false;
        bool anyOverrides = false;
        bool anyCustom = false;
        bool anyAnim = false;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (!MatchesSectionFilter(asset)) continue;

            if (BridgeTintHelper.CanBridgeCosmeticReceivePaint(asset))
            {
                // Bridge tintable: restore original material colours (and drop any custom/animation).
                PerCosmeticColors.SetNoSave(asset.assetId, PerCosmeticColors.OriginalColorSentinel);
                anySlots  |= PerCosmeticColors.RemoveSlotsNoSave(asset.assetId);
                anyCustom |= PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId);
                anyCustom |= PerCosmeticColors.RemoveCustomSlotsNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveAnimationNoSave(asset.assetId);
                anyAnim   |= PerCosmeticColors.RemoveSlotAnimationsNoSave(asset.assetId);
                ApplyToLiveInstances(asset);
                anyOverrides = true;
            }
            else if (!BridgeIds.IsBridgeAsset(asset))
            {
                // Vanilla/modded: clear BOTH the palette override and any custom RGB — a custom-only cosmetic must also revert here, or "Original" leaves it custom-coloured.
                if (PerCosmeticColors.RemoveColorNoSave(asset.assetId)) anyOverrides = true;
                if (PerCosmeticColors.RemoveCustomColorNoSave(asset.assetId)) anyCustom = true;
                if (PerCosmeticColors.RemoveCustomSlotsNoSave(asset.assetId)) anyCustom = true;
            }
        }

        // Base mesh ("Default" head/arm) custom colours have no CosmeticAsset, so the loop misses them — clear them for the section's meshSwitch types too.
        if (VanillaTintHelper.RemoveSectionBaseMeshCustomsNoSave(sectionColorKey, sectionPageMode))
            anyCustom = true;

        // ── Type colour reset to defaultColor (mirrors CosmeticEquip behaviour) ──
        // Cosmetics/All modes: reset colorsEquipped per type to the equipped cosmetic's first-equip colour (defaultColor → colors.IndexOf, else 0). Specific-type mode only clears overrides.
        bool anyTypeReset = false;
        if (sectionColorKey < 0)
        {
            for (int typeIdx = 0; typeIdx < meta.colorsEquipped.Length; typeIdx++)
            {
                if (!TypeIndexMatchesMode(typeIdx, meta)) continue;
                int defaultColor = GetDefaultTypeColor(typeIdx, meta);
                if (meta.colorsEquipped[typeIdx] != defaultColor)
                {
                    meta.colorsEquipped[typeIdx] = defaultColor;
                    anyTypeReset = true;
                }
                // Pin vanilla cosmetics whose defaultColor differs from the type default — with multi-equip the type slot holds one value, so the rest need per-cosmetic overrides.
                PinVanillaCosmeticDefaults(typeIdx, defaultColor, meta, ref anyOverrides);
            }
        }

        if (anyOverrides) PerCosmeticColors.Save();
        if (anySlots)     PerCosmeticColors.SaveSlots();
        if (anyCustom)    { PerCosmeticColors.SaveCustom(); PerCosmeticColors.SaveCustomSlots(); }
        if (anyAnim)
        {
            PerCosmeticColors.SaveAnimations();
            PerCosmeticColors.SaveSlotAnimations();
            ColorAnimatorRefresher.RefreshLocal();   // strip the now-removed animators immediately
        }
        if (anyTypeReset) meta.Save();

        // Re-apply colours on every local/menu PlayerCosmetics (avatar + Mini-Semibot) since base meshes have no CosmeticAsset; preset-aware (preset minis keep their colours).
        if (anyCustom)
            RuntimeConfigApplier.ReapplyLocalCosmeticColors();
    }

    // Each equipped vanilla cosmetic of the type shows its own defaultColor: matches the type default → drop any stale override; differs → store a per-cosmetic override.
    private static void PinVanillaCosmeticDefaults(int typeIdx, int typeDefaultColor, MetaManager meta, ref bool anyOverridesChanged)
    {
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || BridgeIds.IsBridgeAsset(asset)) continue;
            if ((int)asset.type != typeIdx) continue;

            // Mirror CosmeticEquip: this cosmetic's original colour is its defaultColor index.
            int cosmeticDefault = 0;
            if (asset.defaultColor != null)
            {
                int ci = meta.colors.IndexOf(asset.defaultColor);
                if (ci >= 0) cosmeticDefault = ci;
            }

            if (cosmeticDefault == typeDefaultColor)
            {
                // Covered by type colour — remove any stale per-cosmetic override.
                if (PerCosmeticColors.HasOverride(asset.assetId))
                {
                    PerCosmeticColors.RemoveColorNoSave(asset.assetId);
                    anyOverridesChanged = true;
                }
            }
            else
            {
                // Different from type default — pin so this cosmetic shows its own colour.
                PerCosmeticColors.SetNoSave(asset.assetId, cosmeticDefault);
                anyOverridesChanged = true;
            }
        }
    }

    // Mirrors MetaManager.CosmeticEquip: type colour = last-equipped cosmetic of that type. Returns colors.IndexOf(asset.defaultColor), or 0.
    private static int GetDefaultTypeColor(int typeIdx, MetaManager meta)
    {
        int result = 0;
        foreach (int idx in meta.cosmeticEquipped)
        {
            if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
            var asset = meta.cosmeticAssets[idx];
            if (asset == null || (int)asset.type != typeIdx) continue;
            if (asset.defaultColor != null)
            {
                int colorIdx = meta.colors.IndexOf(asset.defaultColor);
                result = colorIdx >= 0 ? colorIdx : 0;
            }
            else
            {
                result = 0;
            }
        }
        return result;
    }

    // True when the type index falls within the section's paint scope. Mirrors vanilla MenuButtonColor's meshSwitch filter for All/Cosmetics/Body.
    private bool TypeIndexMatchesMode(int typeIdx, MetaManager meta)
    {
        if (sectionPageMode == MenuPageColor.ColorPageType.All) return true;
        if (meta?.cosmeticTypeAssets == null
            || typeIdx < 0 || typeIdx >= meta.cosmeticTypeAssets.Count)
            return true; // fallback

        bool isMeshSwitch = meta.cosmeticTypeAssets[typeIdx].meshSwitch;
        return sectionPageMode == MenuPageColor.ColorPageType.Cosmetics
            ? !isMeshSwitch
            : isMeshSwitch;
    }

    private bool MatchesSectionFilter(CosmeticAsset asset)
        => VanillaTintHelper.CosmeticMatchesSection(asset, sectionColorKey, sectionPageMode);

    // Scans the scene for any BridgeTintMaterial of the asset and returns its originalPrimaryColors[0], or white. Standard/Unlit have white _Color (texture provides the look).
    internal static Color FindOriginalColor(CosmeticAsset asset, PlayerCosmetics? preferredCosmetics = null)
    {
        var btms = preferredCosmetics?.playerAvatarVisuals != null
            ? preferredCosmetics.playerAvatarVisuals.GetComponentsInChildren<BridgeTintMaterial>(true)
            : FindObjectsOfType<BridgeTintMaterial>(true);

        foreach (var btm in btms)
        {
            if (btm.cosmetic?.cosmeticAsset != asset) continue;
            if (btm.originalPrimaryColors?.Length > 0)
                return btm.originalPrimaryColors[0];
        }

        if (preferredCosmetics?.playerAvatarVisuals != null)
        {
            var allBtm = FindObjectsOfType<BridgeTintMaterial>(true);
            foreach (var btm in allBtm)
            {
                if (btm.cosmetic?.cosmeticAsset != asset) continue;
                if (btm.originalPrimaryColors?.Length > 0)
                    return btm.originalPrimaryColors[0];
            }
        }

        return Color.white;
    }

    // Original colour per flat slot (0..slotCount-1) so each slot button previews its OWN original instead of sharing slot 0's. Missing colour property → white.
    internal static Color[] BuildSlotOriginalColors(CosmeticAsset asset, int slotCount)
    {
        var result = new Color[slotCount];
        for (int i = 0; i < slotCount; i++) result[i] = Color.white;

        foreach (var btm in FindObjectsOfType<BridgeTintMaterial>(true))
        {
            if (btm.cosmetic?.cosmeticAsset != asset) continue;
            var orig = btm.originalPrimaryColors;
            if (orig == null) continue;
            for (int local = 0; local < orig.Length; local++)
            {
                int flat = btm.SlotIdOf(local);
                if (flat >= 0 && flat < slotCount && orig[local].a > 0f)
                    result[flat] = orig[local];
            }
        }
        return result;
    }
}
