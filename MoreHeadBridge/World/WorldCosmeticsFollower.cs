using UnityEngine;

namespace MoreHeadBridge;

internal sealed class WorldCosmeticsFollower : MonoBehaviour
{
    private Transform? _avatar;
    private string _assetId = "";

    private FollowSpring _spring;
    private Vector3 _easedPos;
    private Quaternion _easedRot;
    private bool _seeded;

    // Visibility: LOCAL wearer's "Show To Self" (others always see it) plus owner-authoritative "Hide on Kart" (hidden on every screen while on a vehicle / kart arena). Menu/preview avatars always render.
    private GameObject? _cosmetic;
    private PlayerAvatarVisuals? _wearer;
    private bool _localWearer;
    private Renderer[]? _renderers;
    private bool _visApplied;
    private bool _visState;

    internal void Configure(Transform avatar, string? assetId,
                            PlayerAvatarVisuals? wearer = null, GameObject? cosmetic = null)
    {
        _avatar = avatar;
        _assetId = assetId ?? "";
        _cosmetic = cosmetic;
        _wearer = wearer;
        _localWearer = wearer != null
            && !MiniSemibotSpawner.IsMenuOrPreviewWearer(wearer)
            && (!SemiFunc.IsMultiplayer() || wearer.playerAvatar?.photonView?.IsMine == true);
    }

    private void LateUpdate()
    {
        if (_avatar == null) _avatar = transform.parent;
        if (_avatar == null) return;

        Vector3 targetPos = _avatar.position;
        Quaternion targetRot = Quaternion.Euler(0f, _avatar.eulerAngles.y, 0f);

        // Per-cosmetic "Avoid Walls" (in-game wearers only — menu/preview avatars have no level): keep the visible body out of walls and let ground-sitting cosmetics step/stop like the Mini-Semibot. Needs the bounds snapshot, taken after the first placement.
        if (_boundsCached && AvoidWallsActive()
            && !MiniSemibotSpawner.IsMenuOrPreviewWearer(_wearer))
            targetPos = ClampToLevel(targetPos, targetRot);

        if (!_seeded) { _easedPos = targetPos; _easedRot = targetRot; _seeded = true; }

        var mode = Plugin.EnableWorldFollowSpring.Value
            ? WorldFollowPrefs.GetSpring(_assetId)
            : FollowSpringMode.Off;

        _easedPos = _spring.StepPosition(_easedPos, targetPos, Time.deltaTime, mode);
        _easedRot = FollowSpring.StepRotation(_easedRot, targetRot, Time.deltaTime, mode);

        var t = transform;
        t.position = _easedPos;
        t.rotation = _easedRot;
        t.localScale = Vector3.one;

        ApplyVisibility();
        if (!_boundsCached) CacheVisualBounds();
    }

    // ── Avoid Walls (per-cosmetic, optional) ────────────────────────────────────────────────────────
    private bool _boundsCached;
    private Vector3 _boundsLocal;
    private float _probeRadius;
    private bool _groundSitter;

    private const float GroundSitMax      = 0.3f;
    private const float WallProbeLiftMin  = 0.3f;
    private const float GroundProbeRadius = 0.15f;
    private const float StepProbeUp       = 0.75f;
    private const float StepSnapRange     = 0.6f;
    private const int   LedgePullSteps    = 3;

    private bool AvoidWallsActive()
    {
        var avatar = _wearer != null ? _wearer.playerAvatar : null;
        if (avatar != null && !avatar.isLocal && SemiFunc.IsMultiplayer())
            return CustomizerSync.TryGetRemote(MiniSemibotSync.ActorOf(avatar), _assetId, out var p)
                   && p != null && p.AvoidWalls == true;
        return WorldFollowPrefs.GetAvoidWalls(_assetId);
    }

    private void CacheVisualBounds()
    {
        _boundsCached = true;
        if (_cosmetic == null) return;
        var rs = _cosmetic.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            if (rs[i] != null) b.Encapsulate(rs[i].bounds);
        var t = transform;
        _boundsLocal = Quaternion.Inverse(t.rotation) * (b.center - t.position);
        _probeRadius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.05f, 0.3f);
        _groundSitter = b.min.y - t.position.y < GroundSitMax;
    }

    private Vector3 ClampToLevel(Vector3 targetPos, Quaternion targetRot)
    {
        Vector3 avatarPos = targetPos;
        Vector3 center = targetPos + targetRot * _boundsLocal;

        float castY = Mathf.Max(center.y, avatarPos.y + WallProbeLiftMin);
        Vector3 origin = new(avatarPos.x, castY, avatarPos.z);
        Vector3 flat = new(center.x, castY, center.z);
        Vector3 to = flat - origin;
        float dist = to.magnitude;
        if (dist > 0.05f && Physics.SphereCast(origin, _probeRadius, to / dist, out var hit,
                dist, MiniSemibotFollow.PlacementMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 stop = hit.point + hit.normal * _probeRadius;
            targetPos += new Vector3(stop.x - center.x, 0f, stop.z - center.z);
        }

        if (!_groundSitter) return targetPos;

        for (int i = 0; i <= LedgePullSteps; i++)
        {
            Vector3 c = targetPos + targetRot * _boundsLocal;
            if (Physics.SphereCast(new Vector3(c.x, avatarPos.y + StepProbeUp, c.z), GroundProbeRadius,
                    Vector3.down, out var ground, StepProbeUp + StepSnapRange,
                    MiniSemibotFollow.PlacementMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(ground.point.y - avatarPos.y) <= StepSnapRange)
                return new Vector3(targetPos.x, ground.point.y, targetPos.z);
            targetPos += new Vector3((avatarPos.x - c.x) * 0.5f, 0f, (avatarPos.z - c.z) * 0.5f);
        }
        return new Vector3(targetPos.x, avatarPos.y, targetPos.z);
    }

    private void ApplyVisibility()
    {
        if (_cosmetic == null) return;

        // "Show To Self": the LOCAL wearer hides their own world cosmetic (others always see it).
        bool hide = _localWearer && !WorldFollowPrefs.GetShowToSelf(_assetId);

        // "Hide on Kart": owner-authoritative — hidden on every screen while on a vehicle (Semiscooter) or the kart-arena level; skipped for menu/preview avatars. Detection works on every client (vehicle/level state is synced); only the choice is synced.
        if (!hide && !MiniSemibotSpawner.IsMenuOrPreviewWearer(_wearer)
            && HideOnKartActive() && WearerOnKart())
            hide = true;

        if (_visApplied && hide == _visState) return;

        _renderers ??= _cosmetic.GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
            if (r != null) r.forceRenderingOff = hide;

        _visApplied = true;
        _visState = hide;
    }

    // Wearer is on the kart-arena level or seated on a vehicle — the Semiscooter specifically (ItemVehicle.GetVehicleForPlayer), NOT generic physRiding, mirroring the Mini-Semibot.
    private bool WearerOnKart()
    {
        var avatar = _wearer != null ? _wearer.playerAvatar : null;
        return MiniSemibotFollow.RunIsKartArena()
               || (avatar != null && ItemVehicle.GetVehicleForPlayer(avatar) != null);
    }

    // Owner-authoritative: remote wearers follow their broadcast choice, the local wearer reads prefs.
    private bool HideOnKartActive()
    {
        var avatar = _wearer != null ? _wearer.playerAvatar : null;
        if (avatar != null && !avatar.isLocal && SemiFunc.IsMultiplayer())
            return CustomizerSync.TryGetRemote(MiniSemibotSync.ActorOf(avatar), _assetId, out var p)
                   && p != null && p.HideOnKart == true;
        return WorldFollowPrefs.GetHideOnKart(_assetId);
    }
}

internal sealed class WorldFollowerCleanup : MonoBehaviour
{
    internal GameObject? Node;

    private void OnDestroy()
    {
        if (Node != null) Object.Destroy(Node);
    }
}
