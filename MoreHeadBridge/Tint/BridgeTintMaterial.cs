using UnityEngine;

namespace MoreHeadBridge;

// Lightweight tinting component for bridge (.hhh) cosmetics.
// Unlike PlayerMaterial (hard-wired to Hurtable's _AlbedoColor/_EmissionColor), stores the ACTUAL property ID detected at equip time — works on Standard, URP, Unlit and custom shaders.
// r.material only returns slot[0] — use r.materials to tint EVERY slot exposing the primary property (e.g. NicksHat packs 7 materials on one renderer).
// Added by BridgeTintHelper.InjectBridgeTintMaterials. Coloured by ApplyTypeColors (type), PerCosmeticColors.ApplyOverrides (local override) and PerCosmeticColorSyncComponent (remote override).
internal sealed class BridgeTintMaterial : MonoBehaviour
{
    // Instance materials fetched once in Setup() (r.materials → per-slot instance copies, shared assets untouched). Slots without the primary property stay null so indices align with the renderer's layout.
    internal Material[]? materials;

    // Primary colour property ID, from the first shared material with a supported property; applied to every slot that has it (_AlbedoColor / _Color / _BaseColor).
    internal int primaryPropId;

    // Optional emission channel — Hurtable-shader cosmetics only, matching vanilla PlayerMaterial.
    internal int emissionPropId;
    internal bool hasEmission;

    // Cosmetic type used to index into PlayerCosmetics.colorsEquipped for type tinting.
    internal SemiFunc.CosmeticType cosmeticType;

    // Linked Cosmetic component — used by ApplyOverrides to look up per-cosmetic colour overrides by assetId.
    internal Cosmetic? cosmetic;

    // Index of this component among all BridgeTintMaterials on the same cosmetic instance; assigned sequentially (0, 1, 2 …) in BridgeTintHelper.InjectBridgeTintMaterials.
    internal int btmIndex;

    // Flat index of this BTM's first slot in the cosmetic's combined slot space (sum of preceding BTMs' material counts) — maps the UI's flat slot number to a local slot.
    internal int materialSlotOffset;

    // Optional per-local-material slot id, set by a ModdedSlotLayout to GROUP several materials/renderers under one UI slot (e.g. YoshiCarry's "skin"). null → identity (materialSlotOffset + i), so existing .hhh cosmetics keep one slot per material.
    internal int[]? slotIds;

    // Logical slot id of a local material index: the layout's id when grouped, else the default flat index.
    internal int SlotIdOf(int localIndex)
        => slotIds != null && localIndex >= 0 && localIndex < slotIds.Length
            ? slotIds[localIndex]
            : materialSlotOffset + localIndex;

    // Original colours per slot, captured in Setup() BEFORE any tinting — RestoreOriginalColor() returns the author's look. Slots without the property hold default(Color), never read back (HasProperty is checked first).
    internal Color[]? originalPrimaryColors;
    internal Color[]? originalEmissionColors;

    private bool _setup;

    // Calls Setup() if it hasn't run yet — a safety net for apply methods when Setup() was skipped (e.g. renderer not yet available at inject time).
    internal void EnsureSetup()
    {
        if (!_setup) Setup();
    }

    internal void Setup()
    {
        if (_setup) return;
        var r = GetComponent<Renderer>();
        if (r == null) return;

        // r.materials creates instance copies for ALL material slots, not just slot [0].
        materials = r.materials;

        originalPrimaryColors  = new Color[materials.Length];
        originalEmissionColors = new Color[materials.Length];

        // Snapshot the original colours of every slot exposing the primary property; slots with a different shader (no primaryPropId) are silently skipped.
        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            if (mat == null || !mat.HasProperty(primaryPropId)) continue;

            originalPrimaryColors[i] = mat.GetColor(primaryPropId);
            if (hasEmission && mat.HasProperty(emissionPropId))
                originalEmissionColors[i] = mat.GetColor(emissionPropId);
        }

        // Disable physics collisions on cosmetic objects (mirrors PlayerMaterial.Setup()) so bridge cosmetics don't interact with the physics world.
        foreach (var col in GetComponentsInChildren<Collider>(includeInactive: true))
            col.isTrigger = true;

        _setup = true;
    }

    // Applies colors[colorIndex] to every slot with the primary property; alpha preserved per-slot.
    internal void ApplyColor(int colorIndex)
    {
        EnsureSetup();
        if (!_setup || materials == null) return;
        if (MetaManager.instance?.colors == null) return;
        if (colorIndex < 0 || colorIndex >= MetaManager.instance.colors.Count) return;

        Color c = MetaManager.instance.colors[colorIndex].color;

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            if (mat == null || !mat.HasProperty(primaryPropId)) continue;
            TintSlot(mat, c, c);
        }
    }

    // Applies an arbitrary RGB to every slot (alpha preserved) — used by BridgeColorAnimator, whose lerped/HSV colours match no palette index.
    internal void ApplyColorRGB(Color c)
    {
        EnsureSetup();
        if (!_setup || materials == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            if (mat == null || !mat.HasProperty(primaryPropId)) continue;
            TintSlot(mat, c, c);
        }
    }

    // Applies MetaManager.instance.colors[colorIndex] to a single local material slot (index into materials[], 0-based within this BTM).
    internal void ApplyColorToSlot(int localSlot, int colorIndex)
    {
        EnsureSetup();
        if (!_setup || materials == null) return;
        if (localSlot < 0 || localSlot >= materials.Length) return;
        if (MetaManager.instance?.colors == null) return;
        if (colorIndex < 0 || colorIndex >= MetaManager.instance.colors.Count) return;

        var mat = materials[localSlot];
        if (mat == null || !mat.HasProperty(primaryPropId)) return;

        Color c = MetaManager.instance.colors[colorIndex].color;
        TintSlot(mat, c, c);
    }

    // Applies an arbitrary RGB (custom colour) to a single local material slot.
    internal void ApplyColorRGBToSlot(int localSlot, Color c)
    {
        EnsureSetup();
        if (!_setup || materials == null) return;
        if (localSlot < 0 || localSlot >= materials.Length) return;

        var mat = materials[localSlot];
        if (mat == null || !mat.HasProperty(primaryPropId)) return;
        TintSlot(mat, c, c);
    }

    // Restores a single local material slot to the colour captured at equip time.
    internal void RestoreOriginalColorInSlot(int localSlot)
    {
        EnsureSetup();
        if (!_setup || materials == null
            || originalPrimaryColors == null || originalEmissionColors == null) return;
        if (localSlot < 0 || localSlot >= materials.Length) return;

        var mat = materials[localSlot];
        if (mat == null || !mat.HasProperty(primaryPropId)) return;
        TintSlot(mat, originalPrimaryColors[localSlot], originalEmissionColors[localSlot]);
    }

    // Restores every material slot to the colour captured at equip time (before any tinting); fired by the "Original" colour-picker option.
    internal void RestoreOriginalColor()
    {
        EnsureSetup();
        if (!_setup || materials == null
            || originalPrimaryColors == null || originalEmissionColors == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            if (mat == null || !mat.HasProperty(primaryPropId)) continue;
            TintSlot(mat, originalPrimaryColors[i], originalEmissionColors[i]);
        }
    }

    // Sets a slot's primary property to primaryRgb (and emission to emissionRgb when present),
    // each preserving the slot's existing alpha. Caller guards mat != null && HasProperty(primaryPropId).
    private void TintSlot(Material mat, Color primaryRgb, Color emissionRgb)
    {
        Color existing = mat.GetColor(primaryPropId);
        mat.SetColor(primaryPropId, new Color(primaryRgb.r, primaryRgb.g, primaryRgb.b, existing.a));

        if (hasEmission && mat.HasProperty(emissionPropId))
        {
            Color existingEm = mat.GetColor(emissionPropId);
            mat.SetColor(emissionPropId, new Color(emissionRgb.r, emissionRgb.g, emissionRgb.b, existingEm.a));
        }
    }
}
