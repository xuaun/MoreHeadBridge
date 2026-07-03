using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Copy of the wearer's map in the mini's hand while held (Orb/OrbLight only). A STRIPPED static prop — no second live minimap camera.
// Parented to the mini's grabber point so it follows the raised hand; placement is a tunable local offset (the "mirror the wearer frame" math landed it behind the head).
internal sealed class MiniMapHold : MonoBehaviour
{
    internal PlayerAvatar? WearerAvatar;
    internal PlayerAvatarRightArm? MiniArm;
    internal MiniSemibotFollow? Follow;   // mini is hidden (e.g. on the kart) → hide the held-map clone too

    // ── Tunable placement (relative to the mini's grabber hand point) ──────────────────────────────
    private static readonly Vector3 MapLocalPos   = new(0f, 0f, 0f);   // nudge in the hand's local space
    private static readonly Vector3 MapLocalEuler = new(90f, 90f, 0f); // fallback rotation, degrees
    // ───────────────────────────────────────────────────────────────────────────────────────────────

    private GameObject? _mapClone;
    private Transform? _src;     // the live map's visual transform (for current world scale)
    private bool _built;

    // The mini's held-map clone while showing, else null — MiniSemibotFollow aims the mini's gaze at it so it looks down at ITS OWN map like the wearer looks at theirs.
    internal Transform? ActiveCloneTransform
        => _mapClone != null && _mapClone.activeSelf ? _mapClone.transform : null;

    private void LateUpdate()
    {
        var cfg = MiniSemibotSync.Resolve(WearerAvatar);
        var map = WearerAvatar != null ? WearerAvatar.mapToolController : null;
        var hand = MiniArm != null ? MiniArm.grabberTransform : null;
        bool want = cfg.Grabber != MiniSemibotGrabberVisual.CleanArm
                    // Menu/preview/expression minis never hold the map clone (their wearer's playerAvatar can point at a real player — same parked-preview leak as the grab beam).
                    && (Follow == null || !MiniSemibotSpawner.IsMenuOrPreviewWearer(Follow.WearerVisuals))
                    && (Follow == null || !Follow.BodyHidden)   // mini hidden on the kart → drop the map too
                    && map != null && map.Active && map.VisualTransform != null && hand != null;

        if (!want)
        {
            if (_mapClone != null && _mapClone.activeSelf) _mapClone.SetActive(false);
            return;
        }

        if (!_built) BuildClone(map!);
        if (_mapClone == null) return;
        if (!_mapClone.activeSelf) _mapClone.SetActive(true);

        // Driven in WORLD space, NOT parented to grabberTransform: GrabberLogic overwrites its localScale every frame, crushing a parented clone. At scene root localScale == lossyScale, so the world size × MiniScale is set directly.
        var t = _mapClone.transform;
        t.position = hand!.TransformPoint(MapLocalPos);

        // Rotation MIRRORS the wearer's live map (its rotation relative to the wearer's body, re-applied in the mini's body frame), so the clone tilts/sways like the real map. Position stays hand-anchored.
        var wearerVis = Follow != null ? Follow.WearerVisuals : null;
        var miniVis   = Follow != null ? Follow.MiniVisuals : null;
        if (_src != null && wearerVis != null && miniVis != null)
            t.rotation = miniVis.transform.rotation
                       * (Quaternion.Inverse(wearerVis.transform.rotation) * _src.rotation);
        else
            t.rotation = hand.rotation * Quaternion.Euler(MapLocalEuler);

        if (_src != null) t.localScale = _src.lossyScale * cfg.Scale;
    }

    private void BuildClone(MapToolController map)
    {
        _built = true;   // attempt once; if it fails we don't spam
        if (map.VisualTransform == null) return;

        try
        {
            _src = map.VisualTransform;
            var clone = Object.Instantiate(_src.gameObject);   // scene root — NOT parented to the hand
            clone.name = "MHB_MiniMap";

            // Strip anything that would render a second minimap or run map logic — keep only the meshes.
            foreach (var cam in clone.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var l in clone.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var al in clone.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;

            // The LOCAL player's map renders overlay-style (layer + DisplayMaterial drawn over geometry — would show through walls and the mini). Vanilla gives REMOTE maps the "Triggers" layer + DisplayMaterialClient for occluded rendering; mirror both on the clone.
            int triggersLayer = LayerMask.NameToLayer("Triggers");
            if (triggersLayer >= 0)
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = triggersLayer;
            if (map.DisplayMesh != null && map.DisplayMaterialClient != null)
            {
                // The clone is of VisualTransform — locate the cloned DisplayMesh by name.
                var srcName = map.DisplayMesh.gameObject.name;
                foreach (var mr in clone.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.gameObject.name == srcName) { mr.material = map.DisplayMaterialClient; break; }
            }

            _mapClone = clone;
            _mapClone.SetActive(true);
        }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"Mini-Semibot map clone failed: {ex.Message}");
            _mapClone = null;
        }
    }

    private void OnDestroy()
    {
        if (_mapClone != null) Object.Destroy(_mapClone);
    }
}
