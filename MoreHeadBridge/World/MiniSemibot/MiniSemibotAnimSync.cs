using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Copies the wearer's animator parameters onto the mini every frame (both share PlayerAvatar.controller, so params match by hash). Also forwards one-shot impulse TRIGGERS via rising edges on their bools, speeds the legs while moving, and honours a forced death pose.
// Animators resolved lazily: PlayerAvatarVisuals.animator is set in Start() (after spawn), so we grab it on the first frame it's populated.
// Update with high execution order, NOT LateUpdate: the mini's AnimationLogic() resets "Turning"/"Grabbing" each Update and the Animator evaluates between Update and LateUpdate — LateUpdate writes would land after evaluation and lose.
[DefaultExecutionOrder(10000)]
internal sealed class MiniSemibotAnimSync : MonoBehaviour
{
    internal PlayerAvatarVisuals? SourceVisuals;   // wearer's body visuals (driven by their movement)
    internal PlayerAvatarVisuals? TargetVisuals;   // the mini avatar's visuals
    internal MiniPoseOverride Override;            // set by MiniSemibotFollow

    private Animator? _source;
    private Animator? _target;
    private PlayerAvatarMenu? _targetMenu;   // mini's menu component — its physGrabBeamActive drives the arm
    private AnimatorControllerParameter[]? _params;

    // Cached param hashes.
    private static readonly int HMoving    = Animator.StringToHash("Moving");
    private static readonly int HSprinting = Animator.StringToHash("Sprinting");
    private static readonly int HSliding   = Animator.StringToHash("Sliding");
    private static readonly int HJumping   = Animator.StringToHash("Jumping");
    private static readonly int HFalling   = Animator.StringToHash("Falling");
    private static readonly int HCrouching = Animator.StringToHash("Crouching");
    private static readonly int HCrawling  = Animator.StringToHash("Crawling");
    private static readonly int HTumbling  = Animator.StringToHash("Tumbling");
    private static readonly int HTumblingMove = Animator.StringToHash("TumblingMove");
    private static readonly int HTurning   = Animator.StringToHash("Turning");
    private static readonly int HGrabbing  = Animator.StringToHash("Grabbing");
    private static readonly int HSprintImpulse = Animator.StringToHash("SprintingImpulse");
    private static readonly int HSlideImpulse  = Animator.StringToHash("SlidingImpulse");
    private static readonly int HJumpImpulse   = Animator.StringToHash("JumpingImpulse");
    private static readonly int HFallImpulse   = Animator.StringToHash("FallingImpulse");
    private static readonly int HTumbleImpulse = Animator.StringToHash("TumblingImpulse");

    // Previous-frame source bools for rising-edge impulse forwarding.
    private bool _wasJumping, _wasSprinting, _wasSliding, _wasTumbling;

    // Turn assist: the wearer's yaw last frame + an init guard. We drive "Turning" at a LOWER threshold than vanilla so the mini steps its legs when you pivot in place.
    private float _prevYaw;
    private bool _yawInit;
    private const float TurnRateThreshold = 8f;        // deg/sec (vanilla effectively ~30); lower = easier
    private const float TumbleSpinThreshold = 0.015f;   // tumble-body angular speed (rad/s) to flail the legs
                                                       // (live-rb log: rest ~0.01, gentle turn ~0.3, roll 1–3)

    private void Update()
    {
        if (_source == null) _source = ResolveAnimator(SourceVisuals);
        if (_target == null) { _target = ResolveAnimator(TargetVisuals); _params = null; }
        if (_target == null) return;

        if (Override != MiniPoseOverride.None)
        {
            ApplyForcedPose();
            return;
        }

        if (_source == null) return;
        _params ??= _target.parameters;

        // Raise the mini's arm when YOU grab / open the map: mirror the wearer's "Grabbing" bool — the mini's own PlayerAvatarRightArm poses the arm from it.
        if (_targetMenu == null) _targetMenu = TargetVisuals != null ? TargetVisuals.playerAvatarMenu : null;
        if (_targetMenu != null) _targetMenu.physGrabBeamActive = _source.GetBool(HGrabbing);

        // Plain copy of every continuous parameter (all bools here; no floats/ints in this controller).
        foreach (var p in _params)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                    _target.SetBool(p.nameHash, _source.GetBool(p.nameHash));
                    break;
                case AnimatorControllerParameterType.Float:
                    _target.SetFloat(p.nameHash, _source.GetFloat(p.nameHash));
                    break;
                case AnimatorControllerParameterType.Int:
                    _target.SetInteger(p.nameHash, _source.GetInteger(p.nameHash));
                    break;
                // Triggers handled explicitly below (a plain copy can't read a consumed trigger).
            }
        }

        ForwardImpulses();
        ApplyLegSpeed();
        ApplyYawAssist();
    }

    // Pivot-in-place assist (non-tumble): step the legs at a lower yaw-rate threshold than vanilla so the mini shuffles when you turn on the spot. Runs AFTER the generic copy — only ever overrides "Turning".
    private void ApplyYawAssist()
    {
        if (SourceVisuals == null) return;

        // During a tumble we FULLY own TumblingMove, driven by the LIVE ragdoll rb's spin — NOT physGrabObject.rbVelocity (network-smoothed snapshots that lag/stick → "flails while still"). Angular speed only; linear sliding deliberately ignored.
        if (_source != null && _source.GetBool(HTumbling))
        {
            float ang = 0f;
            var av = SourceVisuals.playerAvatar;
            if (av != null && av.tumble != null && av.tumble.rb != null)
                ang = av.tumble.rb.angularVelocity.magnitude;
            _target!.SetBool(HTumblingMove, ang > TumbleSpinThreshold);
            return;
        }

        float yaw = SourceVisuals.transform.eulerAngles.y;
        if (!_yawInit) { _prevYaw = yaw; _yawInit = true; return; }

        float rate = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) / Mathf.Max(Time.deltaTime, 0.0001f);
        _prevYaw = yaw;

        // Pivot-in-place assist: step the legs (Turning) at a lower threshold than vanilla. Only when not already moving / mid-jump.
        bool moving = _source!.GetBool(HMoving) || _source.GetBool(HSprinting) || _source.GetBool(HSliding);
        bool busy   = _source.GetBool(HJumping);
        if (!moving && !busy && rate > TurnRateThreshold)
            _target!.SetBool(HTurning, true);
    }

    // Fires impulse triggers when the matching state begins. Jumping rising edge = real jump UNLESS Falling is already set (the walked-off-an-edge case → FallingImpulse).
    private void ForwardImpulses()
    {
        bool jumping   = _source!.GetBool(HJumping);
        bool sprinting = _source.GetBool(HSprinting);
        bool sliding   = _source.GetBool(HSliding);
        bool tumbling  = _source.GetBool(HTumbling);

        if (jumping && !_wasJumping)
            _target!.SetTrigger(_source.GetBool(HFalling) ? HFallImpulse : HJumpImpulse);
        if (sprinting && !_wasSprinting) _target!.SetTrigger(HSprintImpulse);
        if (sliding && !_wasSliding) _target!.SetTrigger(HSlideImpulse);
        if (tumbling && !_wasTumbling) _target!.SetTrigger(HTumbleImpulse);

        _wasJumping = jumping;
        _wasSprinting = sprinting;
        _wasSliding = sliding;
        _wasTumbling = tumbling;
    }

    // Scurry: faster legs while moving so it looks like the little guy is hurrying to keep up.
    private void ApplyLegSpeed()
    {
        bool moving = _source!.GetBool(HMoving) || _source.GetBool(HSprinting) || _source.GetBool(HSliding);
        float legSpeed = MiniSemibotSync.Resolve(SourceVisuals != null ? SourceVisuals.playerAvatar : null).LegSpeed;
        _target!.speed = moving ? Mathf.Max(1f, legSpeed) : 1f;
    }

    // Death poses: drive the bools directly (source ignored). Reset impulse edge state so the first move after revival re-fires.
    private void ApplyForcedPose()
    {
        _target!.speed = 1f;
        if (_targetMenu == null) _targetMenu = TargetVisuals != null ? TargetVisuals.playerAvatarMenu : null;
        if (_targetMenu != null) _targetMenu.physGrabBeamActive = false;  // lower the arm while dead
        bool crouch = Override == MiniPoseOverride.CrouchIdle;
        bool tumble = Override == MiniPoseOverride.TumbleIdle;

        _target.SetBool(HMoving, false);
        _target.SetBool(HSprinting, false);
        _target.SetBool(HSliding, false);
        _target.SetBool(HJumping, false);
        _target.SetBool(HFalling, false);
        _target.SetBool(HTurning, false);
        _target.SetBool(HGrabbing, false);
        _target.SetBool(HTumblingMove, false);
        _target.SetBool(HCrouching, crouch);
        _target.SetBool(HCrawling, false);
        _target.SetBool(HTumbling, tumble);

        _wasJumping = _wasSprinting = _wasSliding = _wasTumbling = false;
    }

    // PlayerAvatarVisuals.animator is assigned in Start(); fall back to GetComponent (works the instant the object exists; the Animator is on the same GameObject).
    private static Animator? ResolveAnimator(PlayerAvatarVisuals? visuals)
    {
        if (visuals == null) return null;
        return visuals.animator != null ? visuals.animator : visuals.GetComponent<Animator>();
    }
}
