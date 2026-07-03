// On-body "impact" reaction (.hhh equivalent of vanilla CosmeticBlocked): with Impact Pose + "React when alive", springs to the pose while overlapping the environment.
// Detection mirrors CosmeticBlocked (same layer mask, 10×/sec, own colliders ignored). Live body ONLY — never the menu preview or a death head (BridgeDeathHeadBlocked owns that). LateUpdate, over the equip-anim/offset reference.

using UnityEngine;

namespace MoreHeadBridge;

internal sealed class BridgeLiveBlocked : MonoBehaviour
{
    private const float CheckInterval = 0.1f;      // overlap test cadence (10×/sec, like vanilla)
    private const float BlockedCooldown = 0.25f;   // hysteresis once blocked (matches vanilla)
    private const float SwitchDebounce = 0.1f;     // after any state flip, hold before re-testing (vanilla blockedSwitchTimer)

    // Small probe at the TOP of the cosmetic — only its tip pushing into geometry (e.g. a low ceiling) squishes it, instead of a big sphere sized to the whole cosmetic.
    private const float ProbeRadiusMin = 0.03f;
    private const float ProbeRadiusMax = 0.10f;

    // Spring: slightly underdamped → small overshoot on contact/release (matches CosmeticBlocked feel).
    private const float SpringStiffness    = 120f;
    private const float SpringDamping      = 14f;
    private const float SpringKickVelocity = 6f;

    // Robust fallback: force-unblock after this many seconds of continuous contact.
    private const float MaxBlockedDuration = 6f;

    // Vanilla's grab-handle collider name (mirrors CosmeticBlocked's own ignore). If a game update renames it, blocked cosmetics would wrongly catch on the handle — update this string then.
    private const string GrabHandleColliderName = "Health Grab";

    private PlayerCosmetics? _pc;
    private Transform _target = null!;             // cosmetic root we pose
    private Transform _anchor = null!;             // mount parent — stable frame for the test sphere
    private DeathHeadFloorPose _pose = null!;
    private MiniSemibotFollow? _miniFollow;         // non-null when worn by a Mini-Semibot

    private Vector3 _anchorLocalCenter;            // detection sphere centre, in anchor-local space
    private float _localMaxExtent;                 // cosmetic extent in anchor-local units — the probe scales with the wearer (a mini is scaled AFTER mount; a fixed world radius would dwarf its cosmetics)
    private bool _valid;
    private LayerMask _layerMask;

    private float _springPos;                      // 0 = natural, 1 = fully in the impact pose
    private float _springVel;
    private bool _blocked;
    private float _checkTimer;
    private float _cooldownTimer;
    private float _switchTimer;
    private float _blockedDuration;

    private Vector3 _refPos, _refEuler, _refScale; // captured "unblocked" reference (equip/offset pose)

    internal void Init(Cosmetic cosmetic, DeathHeadFloorPose pose)
    {
        _pc = cosmetic.playerCosmetics;
        _target = cosmetic.transform;
        _anchor = _target.parent != null ? _target.parent : _target;
        _pose = pose;
        _pose.MigrateLegacy();

        _refPos = _target.localPosition;
        _refEuler = _target.localEulerAngles;
        _refScale = _target.localScale;

        // Cosmetic worn by a Mini-Semibot? Manual parent walk (the body can be inactive).
        for (var t = _target; t != null && _miniFollow == null; t = t.parent)
            _miniFollow = t.GetComponent<MiniSemibotFollow>();

        if (TryGetWorldBounds(out var bounds))
        {
            Vector3 topWorld = bounds.center + Vector3.up * bounds.extents.y;
            _anchorLocalCenter = _anchor.InverseTransformPoint(topWorld);
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            _localMaxExtent = maxExtent / Mathf.Max(0.0001f, Mathf.Abs(_anchor.lossyScale.y));
            _valid = maxExtent > 0.0001f;
        }

        _layerMask = (int)SemiFunc.LayerMaskGetPhysGrabObject()
                     + LayerMask.GetMask("Default")
                     + LayerMask.GetMask("Enemy");
    }

    // True only for a genuine on-body avatar (local or remote): not the menu preview, not a death head (null playerAvatarVisuals — BridgeDeathHeadBlocked owns it). EXCEPTION: an in-world Mini-Semibot is a menu-avatar clone but lives in the level and squishes under doorways/furniture like the wearer; menu/preview/expression minis stay excluded (the staging area has colliders that would pose the cosmetic for no reason).
    private bool IsLiveBody()
    {
        if (_pc == null) return false;
        var vis = _pc.playerAvatarVisuals;
        if (vis == null) return false;
        if (!vis.isMenuAvatar) return true;
        return _miniFollow != null
            && !_miniFollow.ExpressionPreview
            && _miniFollow.WearerVisuals != null
            && !MiniSemibotSpawner.IsMenuOrPreviewWearer(_miniFollow.WearerVisuals);
    }

    private void LateUpdate()
    {
        if (!_valid || _pose == null) return;

        // Not reacting while alive, or not a live body → make sure no pose is left applied.
        if (!_pose.ReactWhenAlive || !IsLiveBody())
        {
            if (_springPos > 0.001f || Mathf.Abs(_springVel) > 0.001f)
            {
                _target.localPosition = _refPos;
                _target.localRotation = Quaternion.Euler(_refEuler);
                _target.localScale = _refScale;
            }
            _springPos = 0f; _springVel = 0f; _blocked = false; _blockedDuration = 0f;
            return;
        }

        // ── Detection ────────────────────────────────────────────────────────
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        if (_switchTimer > 0f) _switchTimer -= Time.deltaTime;

        bool wasBlocked = _blocked;
        // After any state flip, hold for SwitchDebounce before re-testing — vanilla blockedSwitchTimer.
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

        if (_blocked)
        {
            _blockedDuration += Time.deltaTime;
            if (_blockedDuration >= MaxBlockedDuration)
            {
                _blocked = false; _cooldownTimer = 0f; _blockedDuration = 0f;
            }
        }
        else _blockedDuration = 0f;

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

        // At/below natural: let the equip anim / offset condition own the transform and recapture the reference so we always blend FROM the correct natural pose next time.
        if (lerp <= 0f)
        {
            _refPos = _target.localPosition;
            _refEuler = _target.localEulerAngles;
            _refScale = _target.localScale;
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

    // Restore the captured natural pose when removed (e.g. when the override is re-applied) so a cosmetic is never left posed/squished on re-inject.
    private void OnDestroy()
    {
        if (_target == null) return;
        if (_springPos > 0.001f || Mathf.Abs(_springVel) > 0.001f)
        {
            _target.localPosition = _refPos;
            _target.localRotation = Quaternion.Euler(_refEuler);
            _target.localScale = _refScale;
        }
    }

    private bool CheckBlocked()
    {
        Vector3 center = _anchor.TransformPoint(_anchorLocalCenter);
        // Radius proportional to the wearer's CURRENT scale: 1.0 on players (= the old fixed world radius). Minis use sqrt(scale) — strictly linear left the probe too small to trigger at mid sizes (Junior), so smaller bodies get a relatively bigger probe.
        float s = Mathf.Max(0.0001f, Mathf.Abs(_anchor.lossyScale.y));
        float k = s < 1f ? Mathf.Sqrt(s) : s;
        float radius = Mathf.Clamp(_localMaxExtent * k * 0.25f, ProbeRadiusMin * k, ProbeRadiusMax * k);
        var hits = Physics.OverlapSphere(center, radius, _layerMask, QueryTriggerInteraction.Collide);
        foreach (var c in hits)
        {
            if (c == null) continue;
            // Mirror CosmeticBlocked: ignore the grab handle and the player's death-head / tumble colliders (the player's own body colliders aren't in the layer mask).
            if (c.name == GrabHandleColliderName) continue;
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
