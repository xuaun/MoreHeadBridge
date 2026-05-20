// Attached by HhhCosmeticLoader.FixPrefab to any Animator component on a bridge
// cosmetic whose AnimatorController states are not set to loop.
//
// The Animator's "Loop Time" flag lives inside the AnimatorStateMachine and is
// read-only at runtime — we cannot flip it directly. Instead, this component
// detects when the normalizedTime of layer 0 has passed 1.0 (i.e. the clip
// reached its end without looping) and immediately seeks back to 0, producing
// a seamless loop without modifying shared assets.

using UnityEngine;

namespace MoreHeadBridge;

internal sealed class AnimatorLooper : MonoBehaviour
{
    private Animator? _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (_animator == null || !_animator.isActiveAndEnabled) return;
        if (_animator.layerCount == 0) return;

        var info = _animator.GetCurrentAnimatorStateInfo(0);

        // If the current state is already marked as looping, we have nothing to do.
        if (info.loop) return;

        // normalizedTime keeps counting past 1.0 once the non-looping clip ends.
        // Replaying from 0 gives the same visual as a looping clip.
        if (info.normalizedTime >= 1f)
            _animator.Play(info.shortNameHash, 0, 0f);
    }
}
