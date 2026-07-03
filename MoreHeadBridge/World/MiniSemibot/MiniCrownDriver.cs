// Crowns the mini when its WEARER is the crowned player. The avatar clone ships a complete PlayerCrown and the mini's PlayerCosmetics already calls UpdateTarget on every dress, so vanilla logic runs as-is. The only gap: FetchLogic resolves the private playerAvatar field from the clone's visuals, which is null on a worldAvatar (crown never activates) or the LOCAL player (wrong for remote wearers). Feed it the wearer; hide with the body so death/kart/expression frames stay clean.

using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[DefaultExecutionOrder(32000)] // after PlayerCrown's Update/LateUpdate — our hide wins the frame
internal sealed class MiniCrownDriver : MonoBehaviour
{
    private static readonly FieldInfo? PlayerAvatarField =
        AccessTools.Field(typeof(PlayerCrown), "playerAvatar");

    // Resolved current crown target — used by the late-snap below.
    private static readonly FieldInfo? CrownCurrentField =
        AccessTools.Field(typeof(PlayerCrown), "cosmeticPlayerCrownCurrent");

    internal PlayerAvatar? WearerAvatar;
    internal MiniSemibotFollow? Follow;

    private PlayerCrown? _crown;
    private float _retargetTimer;

    private void Awake() => _crown = GetComponentInChildren<PlayerCrown>(true);

    private void LateUpdate()
    {
        if (_crown == null) return;

        // MENU avatar visuals carry no PlayerAvatar at spawn (exprSource was null), so resolve the wearer lazily — and never gate the retarget/snap below on it (gating kept menu/preview minis wobbling until a re-equip re-ran UpdateTarget).
        if (WearerAvatar == null && Follow != null && Follow.WearerVisuals != null)
            WearerAvatar = Follow.WearerVisuals.playerAvatar;

        // Keep vanilla pointed at the WEARER (FetchLogic would re-resolve a null field from the clone's visuals and land on the local player).
        if (WearerAvatar != null && PlayerAvatarField != null
            && !ReferenceEquals(PlayerAvatarField.GetValue(_crown), WearerAvatar))
            PlayerAvatarField.SetValue(_crown, WearerAvatar);

        // Re-scan crown targets at vanilla's 2 Hz cadence: the mini re-dresses through paths that don't reliably end in vanilla's UpdateTarget, leaving cosmeticPlayerCrownCurrent stale — then disableSpring outfits are ignored and the crown wobbles on the default head anchor instead of the cosmetic's target.
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.5f;
            _crown.UpdateTarget();
        }

        // Late snap: vanilla PlayerCrown.FollowLogic ran earlier this LateUpdate, BEFORE the mini's follow moved the body, so the crown trails the head by a frame — on a tiny fast-bobbing body that reads as heavy wobble (and makes disableSpring look ignored). Re-resolve the target and snap now that everything moved; we run at order 32000, after both.
        var curCrown = CrownCurrentField?.GetValue(_crown) as CosmeticPlayerCrown;
        Transform? target = null;
        if (curCrown != null)
            target = curCrown.cosmeticBlocked == null ? curCrown.targetMain
                : curCrown.cosmeticBlocked.blocked ? curCrown.targetBlocked : curCrown.targetUnblocked;
        if (target == null) target = _crown.defaultPosition;
        if (target != null)
        {
            _crown.transform.position = target.position;
            _crown.transform.rotation = target.rotation;
            // disableSpring outfits pin the spring rotation too (same as vanilla AnimationLogic); other outfits keep the natural spring wobble (excited by MiniHeadSync's head motion).
            if (curCrown != null && curCrown.disableSpring && _crown.spring?.transform != null)
                _crown.spring.transform.localRotation = target.localRotation;
        }

        // Mirror the WEARER's real crown — the ground truth every client already renders. The clone's own
        // FetchLogic depends on cloned internal refs (DisableLogic can mis-resolve and never activate, e.g.
        // remote wearers), so assert visibility every frame; we run after PlayerCrown, so our SetActive wins.
        var wearerCrown = Follow != null && Follow.WearerVisuals != null && Follow.WearerVisuals.playerCosmetics != null
            ? Follow.WearerVisuals.playerCosmetics.playerCrown : null;
        bool shouldShow = wearerCrown != null && wearerCrown.crownMesh != null
                          && wearerCrown.crownMesh.gameObject.activeInHierarchy;
        bool visible = shouldShow && !(Follow != null && Follow.BodyHidden);
        if (_crown.crownMesh != null && _crown.crownMesh.gameObject.activeSelf != visible)
            _crown.crownMesh.gameObject.SetActive(visible);
    }
}
