// Per-cosmetic "floor reaction" on the gameplay death head — the .hhh equivalent of vanilla's CosmeticBlocked pose: springs to the configured DeathHeadFloorPose on ground/wall contact. Detection mirrors CosmeticBlocked (same layer mask, own colliders ignored, hysteresis cooldown); applied in LateUpdate so it layers over CosmeticOffsetCondition; MaxBlockedDuration is the stuck fallback.

using UnityEngine;

namespace MoreHeadBridge;

internal sealed class BridgeDeathHeadBlocked : MonoBehaviour
{
    private const float CheckInterval = 0.1f;     // overlap test cadence
    private const float BlockedCooldown = 0.25f;  // hysteresis once blocked (matches vanilla)
    private const float SwitchDebounce = 0.1f;    // after any state flip, hold before re-testing (vanilla blockedSwitchTimer)

    // Small probe at the TOP of the cosmetic: only the tip pushing into geometry squishes it — a full-size sphere would overlap the floor the head rests on and flicker.
    private const float ProbeRadiusMin = 0.03f;
    private const float ProbeRadiusMax = 0.10f;

    // Spring: slightly underdamped (zeta ≈ 0.64) → small overshoot on contact/release. omega_n = sqrt(SpringStiffness) ≈ 11 rad/s; settling time ≈ 0.3 s.
    private const float SpringStiffness   = 120f;
    private const float SpringDamping     = 14f;
    private const float SpringKickVelocity = 6f;  // instant kick on state change for snap

    // Robust fallback: force-unblock after this many seconds of continuous contact.
    private const float MaxBlockedDuration = 4f;

    private PlayerDeathHead? _deathHead;
    private Transform _target = null!;            // cosmetic root we pose
    private Transform _anchor = null!;            // mount parent — stable frame for the test sphere
    private DeathHeadFloorPose _pose = null!;

    private Vector3 _anchorLocalCenter;           // detection sphere centre, in anchor-local space
    private float _worldRadius;                   // detection sphere radius (constant)
    private bool _valid;

    private LayerMask _layerMask;
    private float _springPos;                     // 0 = unblocked (base), 1 = fully in the floor pose
    private float _springVel;
    private bool _blocked;
    private float _checkTimer;
    private float _cooldownTimer;
    private float _switchTimer;
    private float _blockedDuration;

    // Captured "unblocked" reference pose (the offset/default pose) used as the lerp origin.
    private Vector3 _refPos;
    private Vector3 _refEuler;
    private Vector3 _refScale;

    internal void Init(PlayerDeathHead deathHead, Transform target, DeathHeadFloorPose pose)
    {
        _deathHead = deathHead;
        _target = target;
        _anchor = target.parent != null ? target.parent : target;
        _pose = pose;
        _pose.MigrateLegacy(); // fold legacy "React to Floor" into ReactWhenDead

        _refPos = target.localPosition;
        _refEuler = target.localEulerAngles;
        _refScale = target.localScale;

        // Probe at the top of the VISIBLE bounds, relative to the stable anchor so posing this transform never moves the test volume.
        if (TryGetWorldBounds(out var bounds))
        {
            Vector3 topWorld = bounds.center + Vector3.up * bounds.extents.y;
            _anchorLocalCenter = _anchor.InverseTransformPoint(topWorld);
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            _worldRadius = Mathf.Clamp(maxExtent * 0.25f, ProbeRadiusMin, ProbeRadiusMax);
            _valid = maxExtent > 0.0001f;
        }

        // Same layers CosmeticBlocked tests against.
        _layerMask = (int)SemiFunc.LayerMaskGetPhysGrabObject()
                     + LayerMask.GetMask("Default")
                     + LayerMask.GetMask("Enemy");
    }

    private void LateUpdate()
    {
        if (!_valid || _deathHead == null || _pose == null || !_pose.ReactWhenDead) return;

        // Only active while this is a triggered death head; otherwise snap to natural pose.
        if (!_deathHead.triggered)
        {
            if (_springPos > 0.001f || Mathf.Abs(_springVel) > 0.001f)
            {
                _target.localPosition = _refPos;
                _target.localRotation = Quaternion.Euler(_refEuler);
                _target.localScale = _refScale;
            }
            _springPos = 0f; _springVel = 0f;
            _blocked = false; _blockedDuration = 0f;
            return;
        }

        // ── Detection ────────────────────────────────────────────────────────
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        if (_switchTimer > 0f) _switchTimer -= Time.deltaTime;

        bool wasBlocked = _blocked;
        // After any state flip, hold for SwitchDebounce before re-testing (vanilla blockedSwitchTimer) — debounces BOTH directions, killing rapid on/off flicker the cooldown alone misses.
        if (_switchTimer <= 0f)
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CheckInterval;
                if (CheckBlocked()) { _blocked = true; _cooldownTimer = BlockedCooldown; }
                else if (_cooldownTimer <= 0f) _blocked = false;
            }
        }

        // Robust fallback: if stuck squished for too long, force-clear (e.g. detection glitch).
        if (_blocked)
        {
            _blockedDuration += Time.deltaTime;
            if (_blockedDuration >= MaxBlockedDuration)
            {
                _blocked = false; _cooldownTimer = 0f; _blockedDuration = 0f;
            }
        }
        else
        {
            _blockedDuration = 0f;
        }

        // Kick the spring on state transitions (mirrors vanilla's lastPosition + springVelocity kick) and arm the SwitchDebounce.
        if (_blocked != wasBlocked)
        {
            _switchTimer = SwitchDebounce;
            _springVel += _blocked ? SpringKickVelocity : -SpringKickVelocity;
        }

        // ── Spring physics ───────────────────────────────────────────────────
        float springTarget = _blocked ? 1f : 0f;
        float force = (springTarget - _springPos) * SpringStiffness - _springVel * SpringDamping;
        _springVel += force * Time.deltaTime;
        _springPos += _springVel * Time.deltaTime;

        float lerp = Mathf.Clamp01(_springPos);

        // Spring at or below base: let CosmeticOffsetCondition own the transform and recapture the reference so we always lerp FROM the correct offset/default pose next time.
        if (lerp <= 0f)
        {
            _refPos = _target.localPosition;
            _refEuler = _target.localEulerAngles;
            _refScale = _target.localScale;
            // Snap spring to zero once fully settled to prevent drift.
            if (!_blocked && Mathf.Abs(_springVel) < 0.05f) { _springPos = 0f; _springVel = 0f; }
            return;
        }

        // ── Apply pose blend ─────────────────────────────────────────────────
        _target.localPosition = Vector3.Lerp(_refPos,
            new Vector3(_pose.PosX, _pose.PosY, _pose.PosZ), lerp);
        _target.localRotation = Quaternion.Slerp(
            Quaternion.Euler(_refEuler),
            Quaternion.Euler(_pose.RotX, _pose.RotY, _pose.RotZ), lerp);
        _target.localScale = Vector3.Lerp(_refScale,
            new Vector3(_pose.ScaleX, _pose.ScaleY, _pose.ScaleZ), lerp);
    }

    private bool CheckBlocked()
    {
        Vector3 center = _anchor.TransformPoint(_anchorLocalCenter);
        var hits = Physics.OverlapSphere(center, _worldRadius, _layerMask, QueryTriggerInteraction.Collide);
        foreach (var c in hits)
        {
            if (c == null) continue;
            // Ignore the death head's own body/tumble colliders (and the grab handle) — only real environment geometry counts, exactly as CosmeticBlocked does.
            if (c.name == "Health Grab") continue;
            if (c.GetComponentInParent<PlayerDeathHead>() != null) continue;
            if (c.GetComponentInParent<PlayerTumble>() != null) continue;
            return true;
        }
        return false;
    }

    // Combined bounds of VISIBLE meshes: enabled renderers preferred so a disabled body mesh can't inflate the bounds and push the probe above the visible tip; all renderers only as fallback.
    private bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        var all = _target.GetComponentsInChildren<Renderer>(includeInactive: true);

        bool any = false;
        foreach (var r in all)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (any) return true;

        // Fallback: no visible renderer — use all of them so a hidden-by-default cosmetic still reacts.
        foreach (var r in all)
        {
            if (r == null) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
