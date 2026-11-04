using UnityEngine;

namespace MoreHeadBridge;

// Keeps the preview crown visible while Crown Settings is open: PlayerCrown.DisableLogic() SetActive(false)s it every frame, so re-assert in LateUpdate and replicate FollowLogic so it tracks the rotating head. Created when Crown Settings opens; destroyed on any close path — OnDestroy hides the crown so normal logic resumes.
[UnityEngine.DefaultExecutionOrder(32000)]
internal sealed class CrownForceVisibleGuard : MonoBehaviour
{
    private PlayerCrown? _playerCrown;
    private GameObject? _cosmeticGo;
    private CosmeticPlayerCrown? _cosmeticPlayerCrown;

    /// Attaches the guard to <paramref name="playerCrown"/>'s GO. Without a cosmetic on the preview, the crown stays at its default position.
    internal static CrownForceVisibleGuard Attach(PlayerCrown playerCrown, GameObject? cosmeticGo)
    {
        var guard = playerCrown.gameObject.AddComponent<CrownForceVisibleGuard>();
        guard._playerCrown = playerCrown;
        guard._cosmeticGo = cosmeticGo;
        return guard;
    }

    private void LateUpdate()
    {
        if (_playerCrown == null || _playerCrown.crownMesh == null) return;

        // Replicate PlayerCrown.FollowLogic (keep crown at its target) — DisableLogic returns true for the preview avatar so FollowLogic never runs.
        // A cosmetic without a CosmeticPlayerCrown only gets one from the popup's first preview.
        if (_cosmeticPlayerCrown == null && _cosmeticGo != null)
            _cosmeticPlayerCrown = _cosmeticGo.GetComponentInChildren<CosmeticPlayerCrown>(true);

        var target = _cosmeticPlayerCrown != null && _cosmeticPlayerCrown.targetMain != null
            ? _cosmeticPlayerCrown.targetMain
            : _playerCrown.defaultPosition;
        if (target != null)
        {
            _playerCrown.transform.position = target.position;
            _playerCrown.transform.rotation = target.rotation;
        }

        // Re-assert visibility — PlayerCrown.Update/LateUpdate both call DisableLogic (sets the mesh inactive); running at order 32000 we win the last word.
        _playerCrown.crownMesh.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        // Crown Settings closed — hide crown so normal PlayerCrown logic resumes cleanly.
        if (_playerCrown?.crownMesh != null)
            _playerCrown.crownMesh.gameObject.SetActive(false);
    }
}
