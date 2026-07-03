// Attached by CosmeticPrefabFixer to Animators whose states don't loop: "Loop Time" is read-only at runtime, so seek back to 0 when normalizedTime passes 1.0 — a seamless loop without touching shared assets. Handles all layers.

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

        for (int layer = 0; layer < _animator.layerCount; layer++)
        {
            var info = _animator.GetCurrentAnimatorStateInfo(layer);

            // If the current state is already marked as looping, nothing to do.
            if (info.loop) continue;

            // normalizedTime keeps counting past 1.0 once the non-looping clip ends.
            // Replaying from 0 gives the same visual as a looping clip.
            if (info.normalizedTime >= 1f)
                _animator.Play(info.shortNameHash, layer, 0f);
        }
    }
}
