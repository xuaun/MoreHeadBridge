// Tunable placement constants for the Mini-Semibot avatar (death-state + expression-preview), with per-size lookups.
// Spawn/lifecycle lives in MiniSemibotSpawner.cs; preset-roll logic in MiniSemibotSpawner.Presets.cs.

using UnityEngine;

namespace MoreHeadBridge;

internal static partial class MiniSemibotSpawner
{
    // Local-space offsets per placement option, identity facing (mini shows its back, walks like a shadow).
    internal static readonly Vector3 OffsetBehind = new(0.3f, 0.2f, -0.5f);
    internal static readonly Vector3 OffsetFront  = new(0.3f, 0.2f, 0.9f);

    // ── Death-state placement (tunable) ────────────────────────────────────────────────────────────
    // Death-state placement: same side, fixed gap from the head, distance shared, Y lift per size.
    internal const float DeathHeadDistance  = 0.7f;
    internal const float DeathHeadLiftBaby  = -0.2f;
    internal const float DeathHeadLiftChild = -0.29f;
    internal const float DeathHeadLiftTeen  = -0.38f;
    internal const float DeathHeadLiftJunior  = -0.55f;

    internal static float DeathHeadLiftForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => DeathHeadLiftBaby,
        MiniSemibotSize.Teen => DeathHeadLiftTeen,
        MiniSemibotSize.Junior => DeathHeadLiftJunior,
        _               => DeathHeadLiftChild,
    };

    // CrouchWait: stays on its side, fixed gap, facing the head; Y lift per size.
    internal const float CrouchWaitDistance  = 0.65f;
    internal const float CrouchWaitLiftBaby  = 0.13f;
    internal const float CrouchWaitLiftChild = 0.2f;
    internal const float CrouchWaitLiftTeen  = 0.28f;
    internal const float CrouchWaitLiftJunior  = 0.4f;

    internal static float CrouchWaitLiftForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => CrouchWaitLiftBaby,
        MiniSemibotSize.Teen => CrouchWaitLiftTeen,
        MiniSemibotSize.Junior => CrouchWaitLiftJunior,
        _               => CrouchWaitLiftChild,
    };
    // Expression forced on the crouch-wait mini while the wearer is dead (0 = neutral, 1 = first real expression).
    internal const int   CrouchWaitExpression = 2;

    // ── Expression-preview placement (tunable) ──────────────────────────────────────────────────────
    // Mini placement in the 320×320 expression-preview frame (expression avatar's local space; X/Y/Z per size). ExprPreviewScale frames the CHILD tier; actual scale tracks the chosen size proportionally.
    internal const float   ExprPreviewScale  = 0.40f;
    internal const float   ExprPreviewYaw    = 0f;   // extra yaw if the mini shows its back (try 180)

    // Per-size sideways offset (root X in the expression avatar's local space). Tune per tier.
    internal const float ExprPreviewXBaby  = 0.45f;
    internal const float ExprPreviewXChild = 0.45f;
    internal const float ExprPreviewXTeen  = 0.42f;
    internal const float ExprPreviewXJunior  = 0.36f;   // nudged toward centre

    internal static float ExprPreviewXForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => ExprPreviewXBaby,
        MiniSemibotSize.Teen => ExprPreviewXTeen,
        MiniSemibotSize.Junior => ExprPreviewXJunior,
        _               => ExprPreviewXChild,
    };

    // Per-size height (root Y in the expression avatar's local space; root is at the feet). Tune per tier.
    internal const float ExprPreviewYBaby  = 0.82f;
    internal const float ExprPreviewYChild = 0.77f;
    internal const float ExprPreviewYTeen  = 0.72f;
    internal const float ExprPreviewYJunior  = 0.64f;

    internal static float ExprPreviewYForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => ExprPreviewYBaby,
        MiniSemibotSize.Teen => ExprPreviewYTeen,
        MiniSemibotSize.Junior => ExprPreviewYJunior,
        _               => ExprPreviewYChild,
    };

    // Per-size depth (root Z in the expression avatar's local space; +Z toward camera). Tune per tier.
    internal const float ExprPreviewZBaby  = -0.20f;
    internal const float ExprPreviewZChild = -0.20f;
    internal const float ExprPreviewZTeen  = -0.20f;
    internal const float ExprPreviewZJunior  = -0.20f;

    internal static float ExprPreviewZForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => ExprPreviewZBaby,
        MiniSemibotSize.Teen => ExprPreviewZTeen,
        MiniSemibotSize.Junior => ExprPreviewZJunior,
        _               => ExprPreviewZChild,
    };
}
