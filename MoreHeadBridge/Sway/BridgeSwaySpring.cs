using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class BridgeSwaySpring : MonoBehaviour
{
    private readonly struct Preset
    {
        internal readonly float Speed;
        internal readonly float Damping;
        internal readonly bool Clamp;
        internal readonly float MaxAngle;
        internal readonly float Multiplier;
        internal readonly CosmeticSprings.CosmeticSpring.JumpDirection Direction;

        internal Preset(float speed,
                        float damping,
                        bool clamp,
                        float maxAngle,
                        float multiplier,
                        CosmeticSprings.CosmeticSpring.JumpDirection direction)
        {
            Speed = speed;
            Damping = damping;
            Clamp = clamp;
            MaxAngle = maxAngle;
            Multiplier = multiplier;
            Direction = direction;
        }
    }

    private sealed class TargetSpring
    {
        internal Transform Target = null!;
        internal Quaternion BaseLocalRotation;
        internal SpringQuaternion Spring = null!;
    }

    private readonly List<TargetSpring> _targets = new();
    private Preset _preset = GetPreset(SemiFunc.CosmeticType.Hat);
    private float _intensityFactor = 1f;

    // ── Look-down droop ──────────────────────────────────────────────────────────
    // Vanilla lets head accessories droop forward as the player looks down (CosmeticLookDown). Bridge cosmetics have no authored CosmeticLookDown, so replicate it on head-region cosmetics by tilting the spring target downward (source = the avatar's headLookAt).
    private const float MaxLookDownTilt = 12f;   // extra degrees of droop at full look-down
    private Transform? _lookDownSource;
    private bool _lookDownEnabled;

    internal void Init(Cosmetic cosmetic, float intensityFactor = 1f)
    {
        // Re-Init mid-oscillation must not bake the current sprung rotation into the new base.
        RestoreBaseRotations();
        _targets.Clear();
        _preset = GetPreset(cosmetic.type);
        _intensityFactor = intensityFactor;

        // Look-down droop only reads naturally on head-region accessories; resolve the head direction transform from the owning avatar (null on death heads → effect disabled).
        _lookDownEnabled = IsHeadRegion(cosmetic.type);
        _lookDownSource = _lookDownEnabled
            ? cosmetic.playerCosmetics != null && cosmetic.playerCosmetics.playerAvatarVisuals != null
                ? cosmetic.playerCosmetics.playerAvatarVisuals.headLookAtTransform
                : null
            : null;

        if (cosmetic.meshParents != null)
        {
            foreach (var meshParent in cosmetic.meshParents)
                AddTarget(meshParent);
        }

        if (_targets.Count == 0)
        {
            foreach (Transform child in cosmetic.transform)
                AddTarget(child);
        }

        enabled = _targets.Count > 0;
    }

    // Head-region cosmetics that visually droop when looking down (mirrors which vanilla springs carry a CosmeticLookDown). Body/arm/leg cosmetics are excluded — drooping there looks wrong.
    private static bool IsHeadRegion(SemiFunc.CosmeticType type) => type switch
    {
        SemiFunc.CosmeticType.Hat
        or SemiFunc.CosmeticType.HeadTopMesh
        or SemiFunc.CosmeticType.HeadBottom
        or SemiFunc.CosmeticType.HeadBottomMesh
        or SemiFunc.CosmeticType.Ears
        or SemiFunc.CosmeticType.Eyewear
        or SemiFunc.CosmeticType.FaceTop
        or SemiFunc.CosmeticType.FaceBottom => true,
        _ => false,
    };

#if DEBUG
    internal int DebugTargetCount => _targets.Count;

    internal string DebugTargetNames()
        => _targets.Count == 0
            ? "-"
            : string.Join(", ", _targets.ConvertAll(t => t.Target != null ? t.Target.name : "null"));
#endif

    /// Puts every target back on its captured base rotation. Must run before anything (re)captures
    /// baselines — a base captured while sprung ratchets the cosmetic away from its authored pose.
    internal void RestoreBaseRotations()
    {
        foreach (var entry in _targets)
            if (entry.Target != null) entry.Target.localRotation = entry.BaseLocalRotation;
    }

    private void OnDestroy() => RestoreBaseRotations();

    private void AddTarget(Transform? target)
    {
        if (target == null) return;
        foreach (var entry in _targets)
            if (entry.Target == target)
                return;

        _targets.Add(new TargetSpring
        {
            Target = target,
            BaseLocalRotation = target.localRotation,
            Spring = NewSpring(_preset),
        });
    }

    private void Update()
    {
        for (int i = _targets.Count - 1; i >= 0; i--)
        {
            var entry = _targets[i];
            var target = entry.Target;
            if (target == null)
            {
                _targets.RemoveAt(i);
                continue;
            }

            Quaternion targetWorldRotation = target.parent != null
                ? target.parent.rotation * entry.BaseLocalRotation
                : entry.BaseLocalRotation;

            // Look-down droop: tilt the rest target downward; the spring chases it, so the cosmetic droops and springs back when the player looks up.
            if (_lookDownEnabled && _lookDownSource != null)
            {
                float lookDown = Mathf.Clamp01(Vector3.Dot(_lookDownSource.forward, Vector3.down));
                if (lookDown > 0f)
                {
                    Vector3 pitchAxis = target.parent != null ? target.parent.right : Vector3.right;
                    targetWorldRotation = Quaternion.AngleAxis(lookDown * MaxLookDownTilt, pitchAxis)
                                          * targetWorldRotation;
                }
            }

            target.rotation = SemiFunc.SpringQuaternionGet(entry.Spring, targetWorldRotation);
        }
    }

    internal void Impulse(float force, CosmeticSprings.CosmeticSpring.JumpDirection direction)
    {
        if (!enabled) return;

        force *= _preset.Multiplier * _intensityFactor;

        foreach (var entry in _targets)
        {
            var target = entry.Target;
            if (target == null) continue;

            Vector3 axis = direction switch
            {
                CosmeticSprings.CosmeticSpring.JumpDirection.Up => target.up,
                CosmeticSprings.CosmeticSpring.JumpDirection.Down => -target.up,
                CosmeticSprings.CosmeticSpring.JumpDirection.Left => -target.right,
                CosmeticSprings.CosmeticSpring.JumpDirection.Forward => target.forward,
                CosmeticSprings.CosmeticSpring.JumpDirection.Backward => -target.forward,
                _ => target.right,
            };

            entry.Spring.springVelocity += axis * force;
        }
    }

    private static SpringQuaternion NewSpring(Preset preset)
        => new()
        {
            damping = preset.Damping,
            speed = preset.Speed,
            clamp = preset.Clamp,
            maxAngle = preset.MaxAngle,
            bounce = 0.35f,
        };

    internal static CosmeticSprings.CosmeticSpring.JumpDirection DefaultDirection(SemiFunc.CosmeticType type)
        => GetPreset(type).Direction;

    private static Preset GetPreset(SemiFunc.CosmeticType type)
    {
        const CosmeticSprings.CosmeticSpring.JumpDirection Right = CosmeticSprings.CosmeticSpring.JumpDirection.Right;
        const CosmeticSprings.CosmeticSpring.JumpDirection Left = CosmeticSprings.CosmeticSpring.JumpDirection.Left;

        return type switch
        {
            SemiFunc.CosmeticType.Hat => new Preset(18f, 0.55f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.HeadTopMesh => new Preset(13f, 0.60f, false, 25f, 1f, Right),
            SemiFunc.CosmeticType.HeadBottom => new Preset(35f, 0.70f, true, 8f, 1f, Right),
            SemiFunc.CosmeticType.Ears => new Preset(18f, 0.70f, true, 20f, 1f, Right),
            SemiFunc.CosmeticType.Eyewear => new Preset(35f, 0.75f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.FaceTop => new Preset(22f, 0.70f, true, 10f, 1f, Right),
            SemiFunc.CosmeticType.FaceBottom => new Preset(20f, 0.65f, false, 24f, 1f, Right),
            SemiFunc.CosmeticType.BodyTop => new Preset(13f, 0.50f, false, 20f, 1.5f, Right),
            SemiFunc.CosmeticType.BodyBottom => new Preset(15f, 0.50f, false, 20f, 1.25f, Right),
            SemiFunc.CosmeticType.BodyTopMesh => new Preset(13f, 0.50f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.BodyBottomMesh => new Preset(16f, 0.50f, true, 60f, 1f, Left),
            SemiFunc.CosmeticType.ArmLeft => new Preset(16f, 0.65f, false, 18f, 1f, Right),
            SemiFunc.CosmeticType.ArmRight => new Preset(16f, 0.65f, false, 18f, 1f, Right),
            SemiFunc.CosmeticType.ArmLeftMesh => new Preset(10f, 0.50f, false, 10f, 1f, Right),
            SemiFunc.CosmeticType.ArmRightMesh => new Preset(10f, 0.50f, false, 16f, 1f, Right),
            SemiFunc.CosmeticType.LegLeft => new Preset(13f, 0.50f, false, 10f, 3f, Right),
            SemiFunc.CosmeticType.LegRight => new Preset(15f, 0.50f, false, 5f, 5f, Right),
            SemiFunc.CosmeticType.FootLeft => new Preset(10f, 0.50f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.FootRight => new Preset(10f, 0.50f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.EyeLidRightMesh => new Preset(10f, 0.50f, false, 20f, 1f, Right),
            SemiFunc.CosmeticType.EyeLidLeftMesh => new Preset(10f, 0.50f, false, 20f, 1f, Right),
            _ => new Preset(18f, 0.55f, false, 20f, 1f, Right),
        };
    }
}
