// Gives the mini its own flashlight automatically whenever the wearer's is out — clone it to the mini's left-hand bone (like the wearer's FlashlightController.FollowTransformClient) and drive the left-arm bone into the cloned flashlightPose while lit. Toggle: Mini popup "Flashlight".

using UnityEngine;

namespace MoreHeadBridge;

internal sealed class MiniFlashlightHold : MonoBehaviour
{
    // The mini's beam is dimmer than the player's full flashlight — a tiny companion light, not a torch.
    private const float IntensityScale = 0.35f;
    private const float ArmPoseSpeed   = 6f;   // how fast the left arm raises/lowers into the pose
    private const float ForwardNudge   = 0.1f; // nudge the held light slightly ahead of the hand anchor

    internal PlayerAvatar? WearerAvatar;
    internal MiniSemibotFollow? Follow;

    private FlashlightController? _src;
    private float _searchTimer;

    private GameObject? _clone;
    private Light? _cloneLight;
    private bool _buildFailed;

    // Mini left-arm pose (read from the cloned PlayerAvatarLeftArm so we never hardcode the rig).
    private Transform? _leftArm;
    private Vector3 _basePose, _flashPose;
    private bool _armResolved;
    private float _armLerp;   // 0 = animation/base pose, 1 = full flashlight pose

    private void LateUpdate()
    {
        var mv = Follow != null ? Follow.MiniVisuals : null;

        // Re-resolve the wearer's flashlight at 1 Hz until found (it spawns with the level).
        if (_src == null || _src.PlayerAvatar != WearerAvatar)
        {
            _src = null;
            _searchTimer -= Time.deltaTime;
            if (_searchTimer <= 0f)
            {
                _searchTimer = 1f;
                foreach (var fc in Object.FindObjectsOfType<FlashlightController>())
                    if (fc != null && fc.PlayerAvatar == WearerAvatar) { _src = fc; break; }
            }
        }

        // Show when: toggle on, not a menu/preview mini, body shown, and the WEARER's flashlight mesh is enabled (vanilla hides it on crouch/tumble/disabled).
        bool want = MiniSemibotVisualPrefs.MiniFlashlight
                    && (Follow == null || !MiniSemibotSpawner.IsMenuOrPreviewWearer(Follow.WearerVisuals))
                    && (Follow == null || !Follow.BodyHidden)
                    && _src != null && _src.mesh != null && _src.mesh.enabled
                    && mv != null;

        DriveArm(want, mv);

        if (!want)
        {
            if (_clone != null && _clone.activeSelf) _clone.SetActive(false);
            return;
        }

        if (_clone == null && !_buildFailed) BuildClone(_src!, mv!);
        if (_clone == null) return;
        if (!_clone.activeSelf) _clone.SetActive(true);

        // Beam tracks the wearer's actual spotlight (on/off + intensity), dimmed + range-scaled for the mini.
        if (_cloneLight != null)
        {
            float scale = MiniSemibotSync.Resolve(WearerAvatar).Scale;
            var srcLight = _src!.spotlight;
            bool lightOn = srcLight != null && srcLight.enabled && srcLight.intensity > 0.01f;
            _cloneLight.enabled = lightOn;
            if (lightOn)
            {
                _cloneLight.intensity = srcLight!.intensity * IntensityScale;
                _cloneLight.range = srcLight.range * Mathf.Clamp(scale, 0.2f, 1f);
                _cloneLight.spotAngle = srcLight.spotAngle;
            }
        }
    }

    // Raise/lower the mini's left arm into the flashlight pose (LateUpdate overrides the animator).
    private void DriveArm(bool want, PlayerAvatarVisuals? mv)
    {
        ResolveArm(mv);
        if (_leftArm == null) return;

        _armLerp = Mathf.MoveTowards(_armLerp, want ? 1f : 0f, ArmPoseSpeed * Time.deltaTime);
        if (_armLerp <= 0.001f) return;   // fully lowered → let the animator drive the arm again

        var pose = Vector3.Lerp(_basePose, _flashPose, _armLerp);
        _leftArm.localEulerAngles = pose;
    }

    private void ResolveArm(PlayerAvatarVisuals? mv)
    {
        if (_armResolved || mv == null) return;
        var arm = mv.GetComponentInChildren<PlayerAvatarLeftArm>(true);
        if (arm == null || arm.leftArmTransform == null) return;
        _leftArm = arm.leftArmTransform;
        _basePose = arm.basePose;
        _flashPose = arm.flashlightPose;
        _armResolved = true;
    }

    private void BuildClone(FlashlightController src, PlayerAvatarVisuals mv)
    {
        try
        {
            // Hand anchor: the same-named transform the wearer's flashlight follows (FollowTransformClient, in the left-hand rig). Fall back to the head if missing.
            Transform? hand = null;
            if (src.FollowTransformClient != null)
                hand = FindByName(mv.transform, src.FollowTransformClient.name);
            if (hand == null) hand = mv.headLookAtTransform;
            if (hand == null) { _buildFailed = true; return; }

            var clone = Object.Instantiate(src.gameObject);
            clone.name = "MHB_MiniFlashlight";

            // Drop the shadow mesh: the disabled controller leaves meshShadows frozen at a stale first-person spot (a huge ghost AABB). We don't need shadows.
            var cloneFc = clone.GetComponentInChildren<FlashlightController>(true);
            if (cloneFc != null && cloneFc.meshShadows != null)
            {
                cloneFc.meshShadows.enabled = false;
                cloneFc.meshShadows.gameObject.SetActive(false);
            }

            // Static prop: no controller logic, no halo, no second audio.
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;
            foreach (var a in clone.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;

            // Normal occluded rendering (the local player's flashlight renders overlay-style).
            int triggersLayer = LayerMask.NameToLayer("Triggers");
            if (triggersLayer >= 0)
                foreach (var tr in clone.GetComponentsInChildren<Transform>(true))
                    tr.gameObject.layer = triggersLayer;

            // Sit AT the hand bone (vanilla parks the remote flashlight at FollowTransformClient with no offset) → inherits the mini's scale and raised-arm motion.
            var t = clone.transform;
            t.SetParent(hand, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            // We cloned the LOCAL flashlight, whose frozen first-person chain (Tilt/Bob/Sprint/…) offsets the Mesh toward body-centre — counter-translate the root so the Mesh lands ON the hand anchor (orientation kept; drift removed).
            if (cloneFc != null && cloneFc.mesh != null)
                t.localPosition = -hand.InverseTransformPoint(cloneFc.mesh.transform.position)
                                  + Vector3.forward * ForwardNudge;   // small push along the hand's aim

            _cloneLight = clone.GetComponentInChildren<Light>(true);
            if (_cloneLight != null) _cloneLight.enabled = false;

            _clone = clone;
        }
        catch (System.Exception ex)
        {
            _buildFailed = true;   // attempt once; if it fails we don't spam
            BceConsole.LogWarning($"Mini-Semibot flashlight clone failed: {ex.Message}");
            _clone = null;
        }
    }

    private static Transform? FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var f = FindByName(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    private void OnDestroy()
    {
        if (_clone != null) Object.Destroy(_clone);
    }
}
