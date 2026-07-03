// ============================================================================
// Turns a freshly-instantiated death-head prefab (or cosmetic clone) into an inert, visual-only model: strips scripts/physics/photon/particles/light/audio. Callers instantiate under an INACTIVE container so nothing Awakes; DestroyImmediate keeps it that way (a deferred Destroy would let a component run one OnEnable frame later — e.g. PlayerCrown hiding its mesh).
// ============================================================================

using UnityEngine;

namespace MoreHeadBridge;

internal static class DeathHeadModelStripper
{
    // Removes every script/physics/photon component so only the visual model + bones remain.
    internal static void StripGameplay(GameObject instance)
    {
        // Icon-render subtree first: vanilla Cosmetic.Start() deactivates it, but a source dressed on an
        // INACTIVE body (mini death-head) never ran Start — the clone then carries an active icon mesh.
        foreach (var im in instance.GetComponentsInChildren<SemiIconMaker>(true))
            if (im != null) im.gameObject.SetActive(false);

        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) Object.DestroyImmediate(mb);
        foreach (var anim in instance.GetComponentsInChildren<Animator>(true))
            if (anim != null) Object.DestroyImmediate(anim);
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) Object.DestroyImmediate(rb);
        foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            if (col != null) Object.DestroyImmediate(col);
    }

    // Removes non-visual clutter that is NOT a MonoBehaviour (so StripGameplay missed it): smoke/spectated particles, the point light, and audio sources.
    internal static void CleanVisualClutter(GameObject instance)
    {
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            if (ps != null) Object.DestroyImmediate(ps.gameObject);
        foreach (var light in instance.GetComponentsInChildren<Light>(true))
            if (light != null) Object.DestroyImmediate(light.gameObject);
        // Destroy the whole audio GameObject — removing just the AudioSource fails when an AudioLowPassFilter (or other filter) depends on it.
        foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true))
            if (audio != null && audio.gameObject != instance) Object.DestroyImmediate(audio.gameObject);
    }

    internal static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
