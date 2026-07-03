using System.Collections.Generic;

namespace MoreHeadBridge;

// How the colour walks through the palette over time.
internal enum ColorAnimMode
{
    // Lerp smoothly between consecutive palette entries.
    CycleSmooth,
    // Ignore the palette and sweep the full HSV hue wheel.
    Rainbow,
}

// Whether the sequence wraps around or bounces back at the ends.
internal enum ColorAnimDir
{
    Loop,
    PingPong,
}

// Animation spec stored per asset. Palette holds indices into MetaManager.instance.colors (the same palette indices used everywhere else in PerCosmeticColors).
internal sealed class ColorAnimation
{
    public List<int> Palette = new();           // sequence of MetaManager.colors indices
    public float SecondsPerStep = 1f;           // CycleSmooth: seconds per palette step; Rainbow: seconds per full hue loop
    public ColorAnimMode Mode = ColorAnimMode.CycleSmooth;
    public ColorAnimDir Dir = ColorAnimDir.Loop;
}

// Specs driving one cosmetic: whole-asset and/or per-slot (per-slot overrides the whole spec for its slot). Slots with no spec keep their static colour.
internal readonly struct AnimSet
{
    internal readonly ColorAnimation? Whole;
    internal readonly IReadOnlyDictionary<int, ColorAnimation>? PerSlot;
    // Flat slots carrying a STATIC per-slot colour (palette/custom) — they "punch a hole" in the whole-asset animation so a slot painted a fixed colour stays fixed while the rest animate.
    internal readonly HashSet<int>? StaticSlots;

    internal AnimSet(ColorAnimation? whole, IReadOnlyDictionary<int, ColorAnimation>? perSlot,
                     HashSet<int>? staticSlots = null)
    { Whole = whole; PerSlot = perSlot; StaticSlots = staticSlots; }

    internal bool Any => Whole != null || (PerSlot != null && PerSlot.Count > 0);

    // The spec governing a given flat material slot: its per-slot override, else the whole-asset spec (unless a static colour holds that slot).
    internal ColorAnimation? ForSlot(int flatSlot)
    {
        if (PerSlot != null && PerSlot.TryGetValue(flatSlot, out var s)) return s;
        if (StaticSlots != null && StaticSlots.Contains(flatSlot)) return null;
        return Whole;
    }
}
