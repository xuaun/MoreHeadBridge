using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Drives the unparented mini: positions it at the configured placement each frame, reacts to tumble and death (death-head / crouch-wait / hide), self-destructs when the wearer goes.
internal sealed class MiniSemibotFollow : MonoBehaviour
{
    internal PlayerAvatarVisuals? WearerVisuals;   // the wearer's body visuals (follow anchor)
    internal PlayerAvatar? WearerAvatar;           // the wearer's avatar (tumble / death state)
    internal MiniSemibotAnimSync? AnimSync;         // told which pose to force in special states
    internal GameObject? Body;                     // the mini's renderable body (toggled for Hide)
    internal PlayerAvatarVisuals? MiniVisuals;     // the mini's own visuals (for menu gaze)
    internal Vector3 Scale = Vector3.one;
    internal bool ExpressionPreview;               // pinned in the 5-9 expression preview frame, not the world

    private MiniSemibotConfig _cfg;     // wearer's effective config (own settings or remote owner's), resolved per frame
    private FollowSpring _followSpring;   // optional easing/bounce for the normal follow placement
    private bool _hadVisuals;
    private bool _bodyHidden;
    internal bool BodyHidden => _bodyHidden;   // read by the sibling face/beam/map so they go silent/hidden too
    private float _tumbleRecover;   // seconds left of ground-anchored placement after a tumble ends
    private bool _arenaExploded;    // the mini already blew up for the current arena death
    private MiniDeathHead? _deathHead;   // the "When You Die → Death Head" model (lazy; only while dead)
    private Vector3 _deathHeadSmoothPos; // smoothed world position of the death-head model
    private bool _deathHeadPosInit;      // false until the smoothed position is seeded
    private MiniSemibotFace? _face;       // sibling face component (for forcing a death expression)

    // Upright-ground hold after a tumble ends, so the mini doesn't snap to the recovering ragdoll (flash below floor).
    private const float TumbleRecoverTime = 0.5f;

    // Small upward nudge while tumbling so the mini doesn't clip into the floor as it rolls around.
    private const float TumbleYLift = 0.01f;

    // Exponential smoothing rate for the death-head / crouch-wait / tumble follow positions.
    private const float StateSmoothingRate = 12f;

    private void LateUpdate()
    {
        if (WearerVisuals == null)
        {
            if (_hadVisuals) Object.Destroy(gameObject);   // wearer gone → self-destruct
            return;
        }
        _hadVisuals = true;

        // Expression-preview mini: pinned to a tuned corner of the frame, facing the camera. No world-follow / gaze / death logic; face + body idle run independently.
        if (ExpressionPreview)
        {
            PlaceExpressionPreview();
            return;
        }

        // Clear any forced (death) expression by default; the crouch-wait death branch re-asserts it.
        SetForcedExpression(-1);

        // Hide In Arena: hide on the kart-arena level or while on a vehicle (Semiscooter clipping); vehicle-specific, NOT physRiding. Local pref; menus unaffected.
        bool onVehicle = WearerAvatar != null && ItemVehicle.GetVehicleForPlayer(WearerAvatar) != null;
        if (!MiniSemibotSpawner.IsMenuOrPreviewWearer(WearerVisuals) && MiniSemibotVisualPrefs.HideInArena
            && (RunIsKartArena() || onVehicle))
        {
            SetBodyHidden(true);
            if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;
            HideDeathHead();
            return;
        }

        // Effective config per frame (own settings, or the remote owner's synced choice). Drives scale/placement/death.
        _cfg = MiniSemibotSync.Resolve(WearerAvatar);
        Scale = Vector3.one * _cfg.Scale;

        // Gaze: menus use Still/Copy/Mouse; in-game mirrors the wearer's head/eyes (vanilla head-look can't run on a worldAvatar).
        ApplyMenuGaze();
        ApplyInGameGaze();

        var avatar = WearerAvatar;
        // Menu/preview avatars never inherit real death/tumble state (else the preview mini sticks dead). Full check, not just isMenuAvatar — the cosmetics preview isn't flagged but IS a PlayerAvatarMenu.
        bool isMenu = MiniSemibotSpawner.IsMenuOrPreviewWearer(WearerVisuals);
        bool dead = !isMenu && avatar != null && (avatar.isDisabled || avatar.deadSet);
        bool tumbling = !isMenu && avatar != null && avatar.isTumbling;

        if (dead)
        {
            // Arena deaths are EXPLOSIONS — the mini goes out the same way, once per death, then stays gone. Both arena types; a kart-hidden mini already returned above and never explodes.
            if (SemiFunc.RunIsArena())
            {
                if (!_arenaExploded)
                {
                    _arenaExploded = true;
                    ExplodeLikeWearer();
                }
                SetBodyHidden(true);
                HideDeathHead();
                if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;
                return;
            }

            switch (_cfg.Death)
            {
                case MiniSemibotDeathBehavior.Hide:
                    SetBodyHidden(true);
                    HideDeathHead();
                    if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;
                    return;

                case MiniSemibotDeathBehavior.DeathHead:
                    // Turn the mini INTO a static death head in its current outfit (popup-preview pipeline: no animation/physics/talk), sitting at the wearer's real death head and following it when carried. Falls back to the slumped body if the model can't be built.
                    if (ShowDeathHead(avatar!))
                    {
                        SetBodyHidden(true);
                        if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;
                        // One smoke puff as the body becomes the head (mirrors the wearer's death smoke; only this branch "dies").
                        if (!_deathSmoked)
                        {
                            _deathSmoked = true;
                            PuffDeathSmoke(DeathHeadPosition(avatar!));
                        }
                    }
                    else
                    {
                        SetBodyHidden(false);
                        PlaceAtGround(DeathHeadPosition(avatar!));
                        if (AnimSync != null) AnimSync.Override = MiniPoseOverride.TumbleIdle;
                    }
                    return;

                default: // CrouchWait
                    SetBodyHidden(false);
                    HideDeathHead();
                    // Crouch beside the wearer's death-head facing it, smoothed as it's carried; muted + sad expression until revive.
                    PlaceCrouchWaitByDeathHead(avatar!);
                    SetForcedExpression(MiniSemibotSpawner.CrouchWaitExpression);
                    if (AnimSync != null) AnimSync.Override = MiniPoseOverride.CrouchIdle;
                    return;
            }
        }

        HideDeathHead();
        SetBodyHidden(false);
        if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;
        _arenaExploded = false;   // revived/respawned → armed for the next arena death
        _deathSmoked = false;

        // While tumbling (+ a short recovery window) anchor to the wearer's LOGICAL transform, SMOOTHED — kills the spin stutter, and the recovery window stops a stand-up snap to the still-low ragdoll (which flashed the mini below the floor).
        if (tumbling) _tumbleRecover = TumbleRecoverTime;
        else if (_tumbleRecover > 0f) _tumbleRecover -= Time.deltaTime;

        if ((tumbling || _tumbleRecover > 0f) && avatar != null)
        {
            PlaceRelativeUprightSmooth(avatar.transform.position + Vector3.up * TumbleYLift,
                                       avatar.transform.eulerAngles.y);
            return;
        }

        // Normal placement in the wearer's visual frame, optionally eased by the follow-spring (local feel; Off snaps exactly; teleport-sized jumps snap internally).
        var t = transform;
        var anchor = WearerVisuals.transform;
        Vector3 targetPos = anchor.TransformPoint(ResolveOffset());
        if (!isMenu && _cfg.AvoidWalls)   // owner-authoritative (synced); our own pref for our mini
            targetPos = ClampToLevel(anchor.position, targetPos);
        Quaternion targetRot = anchor.rotation;            // identity facing → mini shows its back
        var springMode = MiniSemibotVisualPrefs.FollowSpring;
        t.position = _followSpring.StepPosition(t.position, targetPos, Time.deltaTime, springMode);
        t.rotation = FollowSpring.StepRotation(t.rotation, targetRot, Time.deltaTime, springMode);
        if (t.localScale != Scale) t.localScale = Scale;
    }

    // Behind/Front per config, but always Behind on menu avatars (a front mini would block the avatar you're customising).
    private Vector3 ResolveOffset()
    {
        bool forceBehind = WearerVisuals != null && WearerVisuals.isMenuAvatar;
        var pos = forceBehind ? MiniSemibotPosition.Behind : _cfg.Position;
        var o = pos == MiniSemibotPosition.Front
            ? MiniSemibotSpawner.OffsetFront
            : MiniSemibotSpawner.OffsetBehind;

        // Scale only the VERTICAL lift with size (pivot isn't at the feet, so bigger minis sink); horizontal gap stays at base.
        float f = _cfg.Scale / MiniSemibotSettings.BaseScale;
        return new Vector3(o.x, o.y * f, o.z);
    }

    // Pins the mini in the expression-preview frame: tuned local offset in the expression avatar's space, facing the camera, small fixed scale (Expr* fields on MiniSemibotSpawner).
    private void PlaceExpressionPreview()
    {
        SetBodyHidden(false);
        if (AnimSync != null) AnimSync.Override = MiniPoseOverride.None;

        // Track the size tier proportionally (Child = framed base) so tiers read smaller/bigger in the portrait.
        float sizeFactor = MiniSemibotSettings.Scale / MiniSemibotSettings.BaseScale;
        float worldScale = MiniSemibotSpawner.ExprPreviewScale * sizeFactor;

        // Per-size X/Y/Z keeps the mini framed at every tier (pivot at the feet).
        var size = MiniSemibotVisualPrefs.Size;
        var offset = new Vector3(
            MiniSemibotSpawner.ExprPreviewXForSize(size),
            MiniSemibotSpawner.ExprPreviewYForSize(size),
            MiniSemibotSpawner.ExprPreviewZForSize(size));

        var anchor = WearerVisuals!.transform;
        var t = transform;
        t.position = anchor.TransformPoint(offset);
        t.rotation = anchor.rotation * Quaternion.Euler(0f, MiniSemibotSpawner.ExprPreviewYaw, 0f);

        var scale = Vector3.one * worldScale;
        if (t.localScale != scale) t.localScale = scale;
    }

    // Builds/positions the static death-head model at the wearer's real death head, upright at body yaw (the head has no clear "face"). False = model couldn't build (caller falls back to the slumped body).
    private bool ShowDeathHead(PlayerAvatar avatar)
    {
        if (MiniVisuals == null) return false;
        _deathHead ??= new MiniDeathHead();

        float yaw = avatar.transform.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        // Keep whatever side it was, a fixed gap from the real death head, per-size lift, smooth follow when carried.
        Vector3 headPos = DeathHeadPosition(avatar);
        Vector3 cur = _deathHeadPosInit ? _deathHeadSmoothPos : transform.position;
        Vector3 toModel = cur - headPos; toModel.y = 0f;
        if (toModel.sqrMagnitude < 0.0001f) toModel = -avatar.transform.forward;   // default: behind the body
        Vector3 desired = headPos
                          + toModel.normalized * MiniSemibotSpawner.DeathHeadDistance
                          + Vector3.up * MiniSemibotSpawner.DeathHeadLiftForSize(_cfg.Size);
        if (!_deathHeadPosInit) { _deathHeadSmoothPos = desired; _deathHeadPosInit = true; }
        else
        {
            float k = 1f - Mathf.Exp(-StateSmoothingRate * Time.deltaTime);
            _deathHeadSmoothPos = Vector3.Lerp(_deathHeadSmoothPos, desired, k);
        }

        // Mirror the wearer's winner-crown visibility onto the death-head's own crown mesh.
        var crownPc = WearerVisuals != null ? WearerVisuals.playerCosmetics : null;
        bool crown = crownPc != null && crownPc.playerCrown != null && crownPc.playerCrown.crownMesh != null
                     && crownPc.playerCrown.crownMesh.gameObject.activeInHierarchy;

        return _deathHead.ShowAt(MiniVisuals, _deathHeadSmoothPos, rot, _cfg.Scale, crown);
    }

    // Tears down the death-head model; cheap no-op if already gone. Resets the smoothing seed.
    private void HideDeathHead() { _deathHead?.Destroy(); _deathHeadPosInit = false; }

    // Invalidates the built death-head model (it clones the outfit ONCE) so the next ShowDeathHead rebuilds with the CURRENT look — else it keeps the first build's appearance ("stale/bald/no colour"). Keeps the smoothed position.
    internal void InvalidateDeathHead() => _deathHead?.Destroy();

    // Forces (or clears, with -1) a held facial expression on the mini via its sibling face component.
    private void SetForcedExpression(int index)
    {
        _face ??= GetComponent<MiniSemibotFace>();
        if (_face != null) _face.ForcedExpression = index;
    }

    private void OnDestroy() => HideDeathHead();

    // Crouch-wait anchored to the wearer's death head: fixed gap on whatever side it already was, facing it, SMOOTHED — glides along as the head is carried, stays through a revive.
    private void PlaceCrouchWaitByDeathHead(PlayerAvatar avatar)
    {
        Vector3 headPos = DeathHeadPosition(avatar);
        var t = transform;

        Vector3 toMini = t.position - headPos; toMini.y = 0f;
        if (toMini.sqrMagnitude < 0.0001f) toMini = avatar.transform.forward;
        Vector3 desired = headPos
                          + toMini.normalized * MiniSemibotSpawner.CrouchWaitDistance
                          + Vector3.up * MiniSemibotSpawner.CrouchWaitLiftForSize(_cfg.Size);

        float k = 1f - Mathf.Exp(-StateSmoothingRate * Time.deltaTime);
        t.position = Vector3.Lerp(t.position, desired, k);

        Vector3 look = headPos - t.position; look.y = 0f;
        if (look.sqrMagnitude > 0.0001f)
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look), k);

        if (t.localScale != Scale) t.localScale = Scale;
    }

    // Upright placement at a world position, facing the wearer's body yaw, slightly to the side.
    private void PlaceAtGround(Vector3 worldPos)
    {
        var t = transform;
        float yaw = WearerAvatar != null ? WearerAvatar.transform.eulerAngles.y
                  : WearerVisuals != null ? WearerVisuals.transform.eulerAngles.y : 0f;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        t.position = worldPos + t.right * 0.4f;
        if (t.localScale != Scale) t.localScale = Scale;
    }

    // Relative-upright placement eased toward target (framerate-independent), to absorb tumble jitter.
    private void PlaceRelativeUprightSmooth(Vector3 anchorPos, float yaw)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var targetPos = anchorPos + rot * ResolveOffset();
        float k = 1f - Mathf.Exp(-StateSmoothingRate * Time.deltaTime);
        var t = transform;
        t.position = Vector3.Lerp(t.position, targetPos, k);
        t.rotation = Quaternion.Slerp(t.rotation, rot, k);
        if (t.localScale != Scale) t.localScale = Scale;
    }

    private Vector3 DeathHeadPosition(PlayerAvatar avatar)
    {
        var dh = avatar.playerDeathHead;
        if (dh != null && dh.transform != null) return dh.transform.position;
        return avatar.transform.position;
    }

    private void SetBodyHidden(bool hidden)
    {
        if (Body == null || _bodyHidden == hidden) return;
        _bodyHidden = hidden;
        Body.SetActive(!hidden);
    }

    // Probe mask: walls/floors ("Default") + welded-static props. Players and carried phys objects deliberately excluded — the wearer's collider would block every cast, carried items would shove the mini. Shared with WorldCosmeticsFollower.
    internal static readonly LayerMask PlacementMask = LayerMask.GetMask("Default", "StaticGrabObject");

    // Wall probe also blocks doors/cart/loose props the player can't pass, but skips GRABBED objects (they move with the player). Ground probe stays static-only.
    private static readonly LayerMask WallObstacleMask = LayerMask.GetMask(
        "Default", "StaticGrabObject", "PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge");

    private static readonly RaycastHit[] _wallHits = new RaycastHit[8];

    private static bool WallProbe(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        float bestDist = float.MaxValue;

        int n = Physics.SphereCastNonAlloc(origin, WallProbeRadius, dir, _wallHits, dist,
            WallObstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var h = _wallHits[i];
            if (h.collider == null) continue;
            // The "Health Grab" heal target isn't a wall: it's a StaticGrabObject copying the wearer's transform (not parented under the player), so skip it — else crouching yanked the mini.
            if (h.collider.GetComponentInParent<PlayerHealthGrab>() != null) continue;
            var pgo = h.collider.GetComponentInParent<PhysGrabObject>();
            if (pgo != null && pgo.grabbed) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }

        return bestDist < float.MaxValue;
    }

    private const float WallProbeRadius   = 0.15f;  // ≈ the mini's half-width at the Child base size
    private const float WallProbeHeight   = 0.5f;   // wall probe height above the wearer's feet — stair
                                                    // steps (≈0.2-0.3) pass under the sphere, walls don't
    private const float GroundProbeRadius = 0.15f;  // down-probe sphere — gaps narrower than this aren't holes
    private const float StepProbeUp       = 0.75f;  // step probe starts this high above the target's feet
    private const float StepSnapRange     = 0.6f;   // ground within ± this of the wearer's level = a step
    private const int   LedgePullSteps    = 3;      // pull-toward-wearer attempts when over a drop

    // "Avoid Walls": a wall probe wearer→target (third-person-camera style) so the mini squeezes against walls, then a step/ledge probe so it stands ON stairs and stops at drop-offs. The spring smooths corrections into dodges.
    private Vector3 ClampToLevel(Vector3 anchorPos, Vector3 target)
    {
        float lift = target.y - anchorPos.y;   // configured hover height above the wearer's ground level

        // Wall probe: HORIZONTAL at fixed height, clamping only X/Z — stair steps pass under the sphere, so stairs stay walkable while real walls block.
        float castY = anchorPos.y + WallProbeHeight;
        Vector3 origin = new(anchorPos.x, castY, anchorPos.z);
        Vector3 flat = new(target.x, castY, target.z);
        Vector3 toTarget = flat - origin;
        float dist = toTarget.magnitude;
        if (dist > 0.05f && WallProbe(origin, toTarget / dist, dist, out var hit))
        {
            Vector3 stop = hit.point + hit.normal * WallProbeRadius;
            target = new Vector3(stop.x, target.y, stop.z);
        }

        // Step snap + ledge stop: ground within ±StepSnapRange = a step; else pull the PROBE toward the wearer and retry. The probe is only a search position — never the result (airborne → plain frame-follow, not parked on the wearer).
        Vector3 probe = target;
        for (int i = 0; i <= LedgePullSteps; i++)
        {
            float footY = probe.y - lift;
            if (Physics.SphereCast(new Vector3(probe.x, footY + StepProbeUp, probe.z),
                    GroundProbeRadius, Vector3.down, out var ground, StepProbeUp + StepSnapRange,
                    PlacementMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(ground.point.y - footY) <= StepSnapRange)
                return new Vector3(probe.x, ground.point.y + lift, probe.z);
            probe = Vector3.Lerp(probe, anchorPos + Vector3.up * lift, 0.5f);
        }
        return target;   // no ground in range even beside the wearer (falling/jumping) → frame-follow
    }

    private bool _deathSmoked;

    // One scaled puff of the wearer's death smoke at the mini's death-head spot; smoke only, best-effort.
    private void PuffDeathSmoke(Vector3 position)
    {
        try
        {
            var srcFx = WearerAvatar != null ? WearerAvatar.playerDeathEffects : null;
            var smoke = srcFx != null ? srcFx.smokeParticles : null;
            if (smoke == null) return;

            var clone = Object.Instantiate(smoke.gameObject, position + Vector3.up * 0.1f, Quaternion.identity);
            clone.name = "MHB_MiniDeathSmoke";
            clone.transform.localScale = Vector3.one * Mathf.Max(0.2f, Scale.x);
            var ps = clone.GetComponent<ParticleSystem>();
            if (ps != null) { ps.Clear(); ps.Play(); }
            Object.Destroy(clone, 10f);
        }
        catch (System.Exception ex)
        {
            BridgeLog.Debug($"Mini-Semibot death smoke failed: {ex.Message}");
        }
    }

    // Explodes the mini with a clone of the wearer's PlayerDeathEffects, scaled and coloured from the MINI's outfit. Purely cosmetic — the HurtCollider is switched off before any physics step; best-effort (a failure just skips the blast).
    private void ExplodeLikeWearer()
    {
        try
        {
            var srcFx = WearerAvatar != null ? WearerAvatar.playerDeathEffects : null;
            if (srcFx == null || Body == null || MiniVisuals == null) return;

            var holder = new GameObject("MHB_MiniDeathExplosion");
            holder.transform.position = Body.transform.position + Vector3.up * 0.1f;

            var clone = Object.Instantiate(srcFx.gameObject, holder.transform.position, transform.rotation);
            clone.transform.localScale = Vector3.one * Mathf.Max(0.2f, Scale.x);

            var fx = clone.GetComponent<PlayerDeathEffects>();
            if (fx == null) { Object.Destroy(clone); Object.Destroy(holder); return; }

            fx.followTransform = holder.transform;   // Update() pins the effect here
            fx.playerAvatarVisuals = MiniVisuals;    // GetColors → the mini's outfit colors
            clone.SetActive(true);
            fx.Trigger();
            // Same call stack as Trigger — no physics step has run, so nothing was hurt.
            if (fx.hurtCollider != null) fx.hurtCollider.gameObject.SetActive(false);

            Object.Destroy(clone, 10f);
            Object.Destroy(holder, 10f);
        }
        catch (System.Exception ex)
        {
            BridgeLog.Debug($"Mini-Semibot arena explosion failed: {ex.Message}");
        }
    }

    // True ONLY on the kart arena (Level - Arena Race), not the Arena Fight — SemiFunc.RunIsArena() covers both; the game distinguishes them by level name.
    internal static bool RunIsKartArena()
    {
        var lvl = RunManager.instance != null ? RunManager.instance.levelCurrent : null;
        return lvl != null && lvl.name == "Level - Arena Race";
    }

    // Menu-only gaze (the game disables its menu look-at for worldAvatars): Copy = mirror the big avatar's eye/head bones; Mouse = feed the mini's own PlayerEyes the cursor's world point via OverrideForce.
    private void ApplyMenuGaze()
    {
        if (MiniVisuals == null || WearerVisuals == null || !WearerVisuals.isMenuAvatar) return;
        var mode = MiniSemibotSettings.LookAt;
        if (mode == MiniSemibotLookAt.Still) return;

        _sway = WearerHeadSway();   // folded into DeriveSecondaryGazeBones (Mouse mode)

        var wearerEyes = WearerVisuals.playerEyes;
        var miniEyes = MiniVisuals.playerEyes;

        if (mode == MiniSemibotLookAt.Mouse)
        {
            // The menu mini faces the camera, so aim its head AND eyes at the cursor point together (eyes via OverrideForce, head + lean bones at the same world point) — matching the in-game SameTarget behaviour.
            var ptr = wearerEyes != null ? wearerEyes.menuAvatarPointer : null;
            if (miniEyes != null && ptr != null)
            {
                miniEyes.OverrideForce(ptr.position, 0.3f, gameObject);
                if (AimHeadAtPoint(ptr.position)) DeriveSecondaryGazeBones();
            }
            return;
        }

        // Copy: mirror the resolved gaze bones from the big avatar.
        CopyWearerGaze();
    }

    private MiniMapHold? _mapHold;   // sibling component (added right after this one in Spawn; resolved lazily)

    // In-game look-at: vanilla head-look needs a real playerAvatar (the mini lacks one), so mirror the WEARER's already-resolved head + eye bones — driven/synced on every client, same rig. Skipped in menus.
    private void ApplyInGameGaze()
    {
        if (MiniVisuals == null || WearerVisuals == null || WearerVisuals.isMenuAvatar) return;

        // Per-frame head sway (camera pitch + turn steer), folded into every gaze path via DeriveSecondaryGazeBones.
        _sway = WearerHeadSway();

        // Mini holding its map → look at ITS OWN map. Before idle glance (would wander) and SameTarget (would aim at the wearer's map).
        _mapHold ??= GetComponent<MiniMapHold>();
        var mapClone = _mapHold != null ? _mapHold.ActiveCloneTransform : null;
        if (mapClone != null && AimHeadAtPoint(mapClone.position))
        {
            var miniEyes = MiniVisuals.playerEyes;
            if (miniEyes != null) miniEyes.OverrideForce(mapClone.position, 0.3f, gameObject);
            DeriveSecondaryGazeBones();
            return;
        }

        // Idle "look around": when the wearer isn't fixating, give the mini a gentle wandering gaze. Only while idle — a real look-target falls through to normal gaze.
        if (MiniSemibotVisualPrefs.IdleGlance && ApplyIdleGlance()) return;

        if (_cfg.Gaze == MiniSemibotGaze.SameTarget && AimHeadAtWearerTarget())
        {
            // Drive the mini's OWN eyes at the same world point via its PlayerEyes (spring-aimed from the mini's position), and DERIVE the lean bones from the mini's own head aim (vanilla 0.5/0.25 ratios) — the mini leans toward what IT looks at, not a copy of the wearer's lean.
            AimEyesAtWearerTarget();
            DeriveSecondaryGazeBones();
            return;
        }

        // CopyHead (or SameTarget w/ no target) → mirror bones, EXCEPT a local wearer whose bones vanilla zeroes — synthesize sway-only lean instead (eyes still mirrored).
        if (WearerIsDriven())
        {
            CopyWearerGaze();
        }
        else
        {
            var head = MiniVisuals.headLookAtTransform;
            if (head != null)
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.identity, Time.deltaTime * 15f);
            DeriveSecondaryGazeBones();
            CopyWearerEyes();
        }
    }

    // ── Head sway (look smoothing) ──────────────────────────────────────────────────────────────────
    // Vanilla drives headUp/headSide from camera pitch + turn steer on MENU/REMOTE bodies only. Driven wearer → recover the sway share from their bones (0.5 ratio); LOCAL wearer → recompute from their camera.
    private Vector2 _sway;                 // (pitch°, yaw°) — valid for the current frame
    private SpringFloat? _swayUpSpring;
    private float _swaySteer, _swayPrevYaw;
    private bool _swayYawInit;

    private bool WearerIsDriven()
    {
        var sv = WearerVisuals;
        var av = sv != null ? sv.playerAvatar : null;
        return sv != null && (sv.isMenuAvatar
            || (GameManager.Multiplayer() && av != null && av.photonView != null && !av.photonView.IsMine));
    }

    private Vector2 WearerHeadSway()
    {
        var sv = WearerVisuals;
        if (sv == null) return Vector2.zero;

        if (WearerIsDriven())
        {
            // headUp/headSide = (sway + gaze) * 0.5 on their machine — undo the ratio, remove gaze.
            float upX   = sv.headUpTransform   != null ? NormalizeAngle(sv.headUpTransform.localEulerAngles.x)   : 0f;
            float sideY = sv.headSideTransform != null ? NormalizeAngle(sv.headSideTransform.localEulerAngles.y) : 0f;
            float gazeX = sv.headLookAtTransform != null ? NormalizeAngle(sv.headLookAtTransform.localEulerAngles.x) : 0f;
            float gazeY = sv.headLookAtTransform != null ? NormalizeAngle(sv.headLookAtTransform.localEulerAngles.y) : 0f;
            return new Vector2(upX * 2f - gazeX, sideY * 2f - gazeY);
        }

        var av = sv.playerAvatar;
        if (av == null) return Vector2.zero;
        float dt = Time.deltaTime;
        if (dt <= 0f) return _sway;

        // Pitch from the wearer's camera — vanilla's remote-body math (crouch scaling + clamps).
        float pitch = 0f;
        if (!av.isTumbling && !av.rotationDisabled && !av.rotationOverrideActive && av.localCamera != null)
            pitch = av.localCamera.GetOverrideTransform().eulerAngles.x;
        if (pitch > 90f) pitch -= 360f;
        if (av.isCrawling) pitch *= 0.4f;
        else if (av.isCrouching) pitch *= 0.75f;
        pitch = av.isCrouching ? Mathf.Clamp(pitch, -40f, 40f) : Mathf.Clamp(pitch, -75f, 85f);

        _swayUpSpring ??= new SpringFloat
        {
            damping = MiniVisuals != null && MiniVisuals.lookUpSpring != null ? MiniVisuals.lookUpSpring.damping : 0.5f,
            speed   = MiniVisuals != null && MiniVisuals.lookUpSpring != null ? MiniVisuals.lookUpSpring.speed   : 10f,
        };
        float smoothedPitch = SemiFunc.SpringFloatGet(_swayUpSpring, pitch, dt);

        // Side steer from the wearer's yaw turn — vanilla's turnDifference math (per-frame deg × 5).
        float yaw = av.transform.eulerAngles.y;
        if (!_swayYawInit) { _swayPrevYaw = yaw; _swayYawInit = true; }
        float diff = Mathf.DeltaAngle(_swayPrevYaw, yaw);
        _swayPrevYaw = yaw;
        float steerTarget = Mathf.Clamp(-diff * 5f, -100f, 100f);
        _swaySteer = Mathf.Lerp(_swaySteer, steerTarget, 20f * dt);

        return new Vector2(smoothedPitch, _swaySteer);
    }

    // ── Idle "look around" ──────────────────────────────────────────────────────────────────────────
    private float _glanceYaw, _glancePitch;             // current eased glance angles (deg, mini-local)
    private float _glanceYawTarget, _glancePitchTarget; // the angles we're easing toward
    private float _glanceTimer;                          // countdown to picking a new wander target

    // Wandering gaze while the wearer is idle. False = the wearer is actually looking at something (caller uses normal gaze). Drives the mini's own head + eyes + lean — a genuine independent glance.
    private bool ApplyIdleGlance()
    {
        var wearerEyes = WearerVisuals != null ? WearerVisuals.playerEyes : null;
        if (wearerEyes == null || wearerEyes.lookAtActive) return false;   // wearer IS fixating → not idle
        var head = MiniVisuals != null ? MiniVisuals.headLookAtTransform : null;
        if (head == null) return false;

        _glanceTimer -= Time.deltaTime;
        if (_glanceTimer <= 0f)
        {
            // ── Tunables (change these to taste) ──────────────────────────────────────────────────────
            // New random target every 4–15 s: mostly sideways, a little up/down.
            _glanceYawTarget   = Random.Range(-28f, 28f);
            _glancePitchTarget = Random.Range(-8f, 10f);
            _glanceTimer       = Random.Range(4f, 15f);
        }
        float k = 1f - Mathf.Exp(-3.5f * Time.deltaTime);
        _glanceYaw   = Mathf.Lerp(_glanceYaw,   _glanceYawTarget,   k);
        _glancePitch = Mathf.Lerp(_glancePitch, _glancePitchTarget, k);

        // A point in front of the mini rotated by the wander angles; reuse normal head/eye aiming so lean bones move too.
        Vector3 fwd = MiniVisuals!.transform.rotation * Quaternion.Euler(_glancePitch, _glanceYaw, 0f) * Vector3.forward;
        Vector3 point = head.position + fwd * 3f;
        if (!AimHeadAtPoint(point)) return false;

        DeriveSecondaryGazeBones();
        var miniEyes = MiniVisuals.playerEyes;
        if (miniEyes != null) miniEyes.OverrideForce(point, 0.3f, gameObject);
        return true;
    }

    // Aims the head at the SAME world point the wearer looks at, computed from the mini's own position (nearby targets → slightly different angle, like two beings looking at one thing). Vanilla head-look math (40° clamp, slerp 15). False = no resolvable target.
    private bool AimHeadAtWearerTarget()
    {
        var wearerEyes = WearerVisuals!.playerEyes;
        if (wearerEyes == null || wearerEyes.lookAt == null) return false;
        return AimHeadAtPoint(wearerEyes.lookAt.position);
    }

    // Aims the head-look bone at a world point from the mini's own position (40° clamp, slerp 15). Shared by in-game SameTarget and menu "Mouse". False = missing head bone or degenerate point.
    private bool AimHeadAtPoint(Vector3 point)
    {
        var head = MiniVisuals!.headLookAtTransform;
        if (head == null) return false;

        Vector3 dir = point - head.position;
        if (dir.sqrMagnitude < 0.0001f) return false;

        Vector3 fwd = MiniVisuals.transform.forward;   // mini's body forward = clamp reference
        dir = SemiFunc.ClampDirection(dir, fwd, 40f);
        head.rotation = Quaternion.Slerp(head.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 15f);
        return true;
    }

    // Derives lean bones from the mini's own head-look + this frame's sway (ratios: head 0.5, bodyTop 0.25). After AimHeadAt*.
    private void DeriveSecondaryGazeBones()
    {
        var v = MiniVisuals;
        var head = v != null ? v.headLookAtTransform : null;
        if (v == null || head == null) return;

        float pitch = NormalizeAngle(head.localEulerAngles.x) + _sway.x;
        float yaw   = NormalizeAngle(head.localEulerAngles.y) + _sway.y;

        SetLocalRot(v.headUpTransform,      Quaternion.Euler(pitch * 0.5f,  0f, 0f));
        SetLocalRot(v.bodyTopUpTransform,   Quaternion.Euler(pitch * 0.25f, 0f, 0f));
        SetLocalRot(v.headSideTransform,    Quaternion.Euler(0f, yaw * 0.5f,  0f));
        SetLocalRot(v.bodyTopSideTransform, Quaternion.Euler(0f, yaw * 0.25f, 0f));
    }

    private static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
    private static void SetLocalRot(Transform? t, Quaternion r) { if (t != null) t.localRotation = r; }

    // Aims the mini's own eyes at the wearer's target point via PlayerEyes.OverrideForce each frame. PlayerEyes runs in Update and we no longer overwrite the bones in LateUpdate, so its result survives.
    private void AimEyesAtWearerTarget()
    {
        var wearerEyes = WearerVisuals!.playerEyes;
        var miniEyes = MiniVisuals!.playerEyes;
        if (wearerEyes == null || wearerEyes.lookAt == null || miniEyes == null) return;
        miniEyes.OverrideForce(wearerEyes.lookAt.position, 0.3f, gameObject);
    }

    // Copies the wearer's head/body-top look + eye/pupil bones onto the mini (shared rig).
    private void CopyWearerGaze()
    {
        if (WearerVisuals == null || MiniVisuals == null) return;
        CopyLocalRot(WearerVisuals.headLookAtTransform, MiniVisuals.headLookAtTransform);
        CopyLocalRot(WearerVisuals.headUpTransform, MiniVisuals.headUpTransform);
        CopyLocalRot(WearerVisuals.headSideTransform, MiniVisuals.headSideTransform);
        CopyLocalRot(WearerVisuals.bodyTopUpTransform, MiniVisuals.bodyTopUpTransform);
        CopyLocalRot(WearerVisuals.bodyTopSideTransform, MiniVisuals.bodyTopSideTransform);
        CopyWearerEyes();
    }

    private void CopyWearerEyes()
    {
        var wearerEyes = WearerVisuals != null ? WearerVisuals.playerEyes : null;
        var miniEyes = MiniVisuals != null ? MiniVisuals.playerEyes : null;
        if (wearerEyes != null && miniEyes != null)
        {
            CopyLocalRot(wearerEyes.eyeLeft, miniEyes.eyeLeft);
            CopyLocalRot(wearerEyes.eyeRight, miniEyes.eyeRight);
            CopyLocalRot(wearerEyes.pupilLeft, miniEyes.pupilLeft);
            CopyLocalRot(wearerEyes.pupilRight, miniEyes.pupilRight);
        }
    }

    private static void CopyLocalRot(Transform? from, Transform? to)
    {
        if (from != null && to != null) to.localRotation = from.localRotation;
    }
}
