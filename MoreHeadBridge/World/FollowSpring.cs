using UnityEngine;

namespace MoreHeadBridge;

// How a follower eases toward its target each frame. Purely a visual "feel" choice.
public enum FollowSpringMode
{
    Off,      // snap exactly to the target every frame (the original rigid behaviour)
    Soft,     // critically-damped lag — trails smoothly toward the target, no overshoot
    Springy,  // underdamped — overshoots a touch and settles, bouncy feel
}

// Reusable damped-spring integrator for a follower's position, plus a stateless rotation easer. Each follower keeps its OWN FollowSpring field so velocity state doesn't bleed between them.
internal struct FollowSpring
{
    private Vector3 _vel;
    private bool _seeded;

    // Distance beyond which a jump is treated as a teleport (scene load / respawn / mode switch) and SNAPped instead of springing across the whole map.
    private const float TeleportDistance = 4f;

    // (stiffness k, damping c) per mode.
    private static (float k, float c) Tuning(FollowSpringMode mode) => mode switch
    {
        FollowSpringMode.Springy => (160f, 17f),
        _                        => (130f, 23f),
    };

    // Eases <current> toward <target> with spring dynamics. Off snaps; also snaps on a teleport-sized jump so the follower never visibly slides across the level.
    internal Vector3 StepPosition(Vector3 current, Vector3 target, float dt, FollowSpringMode mode)
    {
        if (mode == FollowSpringMode.Off || dt <= 0f)
        {
            _vel = Vector3.zero;
            _seeded = true;
            return target;
        }

        if (!_seeded || (current - target).sqrMagnitude > TeleportDistance * TeleportDistance)
        {
            _vel = Vector3.zero;
            _seeded = true;
            return target;
        }

        dt = Mathf.Min(dt, 0.05f);
        var (k, c) = Tuning(mode);
        Vector3 accel = (target - current) * k - _vel * c;
        _vel += accel * dt;
        return current + _vel * dt;
    }

    // Stateless exponential rotation easer. Off snaps; Springy turns a touch slower than Soft, but neither overshoots.
    internal static Quaternion StepRotation(Quaternion current, Quaternion target, float dt, FollowSpringMode mode)
    {
        if (mode == FollowSpringMode.Off || dt <= 0f) return target;
        float rate = mode == FollowSpringMode.Springy ? 9f : 13f;
        return Quaternion.Slerp(current, target, 1f - Mathf.Exp(-rate * dt));
    }
}
