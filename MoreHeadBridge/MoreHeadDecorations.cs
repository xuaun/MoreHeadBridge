// Hides/shows the decorations equipped via the MoreHead MENU (not bridge cosmetics)

using UnityEngine;

namespace MoreHeadBridge;

internal static class MoreHeadDecorations
{
    internal const string ContainerName = "HeadDecorations";

    // includeInactive so a hidden container can be found again and restored.
    internal static void SetContainersActive(Transform? root, bool active)
    {
        if (root == null) return;
        foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (t == null || t.name != ContainerName) continue;
            if (t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }
    }
}

// Keeps the decoration containers hidden on one avatar, re-applying so containers MoreHead builds
// later are caught; restores them when removed. everyFrame is for the preset-icon avatar, whose
// single render frame can't wait for the throttled cadence.
internal sealed class MoreHeadDecorationHider : MonoBehaviour
{
    private const float Interval = 0.1f;

    private Transform _root = null!;
    private bool _everyFrame;
    private float _timer;

    internal void Init(Transform root, bool everyFrame = false)
    {
        _root = root;
        _everyFrame = everyFrame;
        _timer = 0f;
        Apply();
    }

    private void OnEnable() => Apply();

    private void LateUpdate()
    {
        if (!_everyFrame)
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = Interval;
        }
        Apply();
    }

    private void Apply()
    {
        if (_root != null) MoreHeadDecorations.SetContainersActive(_root, false);
    }

    private void OnDestroy()
    {
        // Restore so a re-shown avatar (toggle off / left room) isn't left blank.
        if (_root != null) MoreHeadDecorations.SetContainersActive(_root, true);
    }
}
