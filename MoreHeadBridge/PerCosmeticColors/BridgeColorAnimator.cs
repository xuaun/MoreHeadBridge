using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace MoreHeadBridge;

// Runtime colour animator for bridge (.hhh) cosmetics. Added to a cosmetic's root by RefreshLiveAnimators when a ColorAnimation is stored: every frame (~30 Hz) evaluates the spec and writes the RGB to every BridgeTintMaterial below.
// Time source: PhotonNetwork.Time in multiplayer (server clock → phase stays in sync), Time.timeAsDouble in singleplayer. Mirrors the lifecycle of BridgeSwaySpring (per-cosmetic MonoBehaviour driven by a spec, refreshed from the same SetupColorsLogic hook).
internal sealed class BridgeColorAnimator : MonoBehaviour
{
    private const float UpdateInterval = 1f / 30f;   // ~30 Hz throttle (material writes are not free)

    // One animated material slot: which BTM, the local slot index inside it, and its governing spec.
    private readonly struct Binding
    {
        internal readonly BridgeTintMaterial Btm;
        internal readonly int LocalSlot;
        internal readonly ColorAnimation Spec;
        internal Binding(BridgeTintMaterial btm, int localSlot, ColorAnimation spec)
        { Btm = btm; LocalSlot = localSlot; Spec = spec; }
    }

    private Binding[] _bindings = Array.Empty<Binding>();
    private float _accum;

    // (Re)binds from an AnimSet; safe to call repeatedly. Only animated slots get a binding — static slots keep the colour ApplyOverrides set. searchRoot = GameObject scanned for tint materials (cosmetic root on live avatars, stripped clone on the death-head preview).
    internal void Init(GameObject searchRoot, AnimSet set)
    {
        var btms = searchRoot.GetComponentsInChildren<BridgeTintMaterial>(includeInactive: true);

        var list = new List<Binding>();
        foreach (var btm in btms)
        {
            if (btm == null) continue;
            int matCount = btm.materials?.Length ?? 0;
            for (int i = 0; i < matCount; i++)
            {
                var spec = set.ForSlot(btm.SlotIdOf(i));
                if (spec != null) list.Add(new Binding(btm, i, spec));
            }
        }
        _bindings = list.ToArray();
        _accum = UpdateInterval;

        // Apply eagerly so a re-instantiated cosmetic doesn't flash its original colour for one frame before the first Update.
        if (_bindings.Length > 0)
        {
            double t = SemiFunc.IsMultiplayer() ? PhotonNetwork.Time : Time.timeAsDouble;
            foreach (var b in _bindings)
                if (b.Btm != null)
                    b.Btm.ApplyColorRGBToSlot(b.LocalSlot, Evaluate(b.Spec, t));
        }
    }

    // True when this animator has nothing to drive (the caller can destroy it).
    internal bool IsEmpty => _bindings.Length == 0;

    // Clear bindings BEFORE the deferred Destroy — otherwise the animator writes one stale colour frame after being superseded (e.g. switching hovered presets).
    internal void Stop()
    {
        _bindings = Array.Empty<Binding>();
        UnityEngine.Object.Destroy(this);
    }

    private void Update()
    {
        if (_bindings.Length == 0) return;

        _accum += Time.deltaTime;
        if (_accum < UpdateInterval) return;
        _accum = 0f;

        double t = SemiFunc.IsMultiplayer() ? PhotonNetwork.Time : Time.timeAsDouble;
        foreach (var b in _bindings)
            if (b.Btm != null)
                b.Btm.ApplyColorRGBToSlot(b.LocalSlot, Evaluate(b.Spec, t));
    }

    // Evaluates an animation spec's colour at server/local time t.
    private static Color Evaluate(ColorAnimation spec, double t)
    {
        if (spec.Mode == ColorAnimMode.Rainbow)
        {
            double cycle = spec.SecondsPerStep <= 0f ? 1.0 : spec.SecondsPerStep;
            double phase = t / cycle;
            float h = spec.Dir == ColorAnimDir.PingPong
                ? Mathf.PingPong((float)phase, 1f)
                : (float)(phase - Math.Floor(phase));
            return Color.HSVToRGB(Mathf.Clamp01(h), 1f, 1f);
        }

        // CycleSmooth: lerp between consecutive palette entries.
        int n = spec.Palette?.Count ?? 0;
        if (n == 0) return Color.white;
        if (n == 1) return ResolvePaletteColor(spec.Palette![0]);

        double sps = spec.SecondsPerStep <= 0f ? 1.0 : spec.SecondsPerStep;
        double pos = t / sps;

        int i0, i1;
        float frac;
        if (spec.Dir == ColorAnimDir.PingPong)
        {
            // Bounce across [0, n-1] then back. PingPong gives a value in [0, n-1].
            float fpos = Mathf.PingPong((float)pos, n - 1);
            i0 = Mathf.Clamp(Mathf.FloorToInt(fpos), 0, n - 2);
            i1 = i0 + 1;
            frac = fpos - i0;
        }
        else // Loop
        {
            long step = (long)Math.Floor(pos);
            i0 = (int)(((step % n) + n) % n);
            i1 = (i0 + 1) % n;
            frac = (float)(pos - Math.Floor(pos));
        }

        return Color.Lerp(ResolvePaletteColor(spec.Palette![i0]),
                          ResolvePaletteColor(spec.Palette![i1]),
                          frac);
    }

    private static Color ResolvePaletteColor(int paletteIdx)
    {
        var colors = MetaManager.instance?.colors;
        if (colors == null || paletteIdx < 0 || paletteIdx >= colors.Count) return Color.white;
        return colors[paletteIdx].color;
    }
}
