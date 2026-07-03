// Per-cosmetic "hide-self" for bridge cosmetics (.hhh equivalent of vanilla CosmeticHideCondition): while a configured rule matches, the meshes spring to scale 0 and back. Reads equipped types + conditions off the avatar itself (works local AND remote, no networking); runs on live body, menu avatar and death head. Scale applied in LateUpdate over a "shown reference" recaptured while fully visible, so the equip anim / CosmeticOffsetCondition keep owning the transform.

using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class BridgeHideCondition : MonoBehaviour
{
    private const float CheckInterval = 0.1f;   // condition re-evaluation cadence (10×/sec)
    private const float HideSpeed = 6f;          // lerp speed toward hidden
    private const float ShowSpeed = 4f;          // lerp speed toward shown (slightly slower pop-in)

    private sealed class Target
    {
        internal Transform Transform = null!;
        internal Vector3 BaseScale;
        internal Renderer[] Renderers = System.Array.Empty<Renderer>();
    }

    private Cosmetic _cosmetic = null!;
    private PlayerCosmetics? _pc;
    private CosmeticHideConfig _config = null!;
    private readonly List<Target> _targets = new();

    private float _checkTimer;
    private bool _hidden;
    private float _shownFactor = 1f;             // 1 = fully shown, 0 = fully hidden
    private bool _renderersDisabled;

    internal void Init(Cosmetic cosmetic, CosmeticHideConfig config)
    {
        _cosmetic = cosmetic;
        _pc = cosmetic.playerCosmetics;
        _config = config;

        // Destroy any native CosmeticHideCondition so the two don't fight the scale (rules already
        // absorbed at import). Before BaseScale capture, while the native still has the authored scale.
        foreach (var native in cosmetic.GetComponentsInChildren<CosmeticHideCondition>(includeInactive: true))
            if (native != null) Object.Destroy(native);

        _targets.Clear();
        // Prefer the authored mesh roots; fall back to every renderer's own transform.
        if (cosmetic.meshParents is { Count: > 0 })
        {
            foreach (var mp in cosmetic.meshParents)
                AddTarget(mp);
        }
        if (_targets.Count == 0)
        {
            foreach (var r in cosmetic.GetComponentsInChildren<Renderer>(includeInactive: true))
                if (r != null) AddTarget(r.transform);
        }

        enabled = _targets.Count > 0 && config.HasAny;
    }

    private void AddTarget(Transform? t)
    {
        if (t == null) return;
        foreach (var existing in _targets)
            if (existing.Transform == t) return;
        _targets.Add(new Target
        {
            Transform = t,
            BaseScale = t.localScale,
            Renderers = t.GetComponentsInChildren<Renderer>(includeInactive: true),
        });
    }

    private void LateUpdate()
    {
        if (_targets.Count == 0) return;

        // ── Evaluate the hide rules (throttled) ──────────────────────────────
        _checkTimer -= Time.deltaTime;
        if (_checkTimer <= 0f)
        {
            _checkTimer = CheckInterval;
            _hidden = ShouldHide();
        }

        // ── Animate the shown factor ─────────────────────────────────────────
        float targetFactor = _hidden ? 0f : 1f;
        float speed = _hidden ? HideSpeed : ShowSpeed;
        _shownFactor = Mathf.MoveTowards(_shownFactor, targetFactor, speed * Time.deltaTime);

        // Fully shown and settled: let other systems own the scale and recapture the reference.
        if (_shownFactor >= 0.999f && !_hidden)
        {
            EnsureRenderers(true);
            for (int i = 0; i < _targets.Count; i++)
            {
                var tgt = _targets[i];
                if (tgt.Transform != null) tgt.BaseScale = tgt.Transform.localScale;
            }
            return;
        }

        float eased = Mathf.SmoothStep(0f, 1f, _shownFactor);
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            var tgt = _targets[i];
            if (tgt.Transform == null) { _targets.RemoveAt(i); continue; }
            tgt.Transform.localScale = tgt.BaseScale * eased;
        }

        // Disable renderers once fully hidden (degenerate scale-0 meshes are cheap, but this is tidy).
        EnsureRenderers(_shownFactor > 0.001f);
    }

    // Restore every target to its captured scale + visible renderers when this component is removed (e.g. on override re-apply), so a cosmetic is never left shrunk/hidden on re-inject.
    private void OnDestroy()
    {
        foreach (var tgt in _targets)
        {
            if (tgt.Transform != null) tgt.Transform.localScale = tgt.BaseScale;
            foreach (var r in tgt.Renderers)
                if (r != null) r.enabled = true;
        }
    }

    private void EnsureRenderers(bool on)
    {
        if (_renderersDisabled == !on) return;
        _renderersDisabled = !on;
        foreach (var tgt in _targets)
            foreach (var r in tgt.Renderers)
                if (r != null) r.enabled = on;
    }

    private bool ShouldHide()
    {
        if (_pc == null) return false;

        // Hide while a condition the cosmetic listens for is active on this avatar's player.
        if (_config.WhenConditions is { Count: > 0 })
        {
            foreach (var cond in _config.WhenConditions)
                if (_pc.ConditionCustomCheck(cond)) return true;
        }

        // Hide while the player's current pose is listed.
        if (_config.WhenPoses is { Count: > 0 })
        {
            var visuals = _pc.playerAvatarVisuals;
            if (visuals != null && _config.WhenPoses.Contains(visuals.currentPose)) return true;
        }

        // Hide while another equipped cosmetic (not this one) has a listed type, or is a listed
        // specific asset (matched by CosmeticAsset.name — the same key the import stored).
        bool checkTypes = _config.WhenTypes is { Count: > 0 };
        bool checkAssets = _config.WhenCosmetics is { Count: > 0 };
        if (checkTypes || checkAssets)
        {
            var visuals = _pc.playerAvatarVisuals;
            if (visuals != null)
            {
                foreach (var other in visuals.GetComponentsInChildren<Cosmetic>(includeInactive: false))
                {
                    if (other == null || other == _cosmetic) continue;
                    if (checkTypes && _config.WhenTypes!.Contains(other.type)) return true;
                    if (checkAssets && other.cosmeticAsset != null
                        && _config.WhenCosmetics!.Contains(other.cosmeticAsset.name)) return true;
                }
            }
        }
        return false;
    }
}
