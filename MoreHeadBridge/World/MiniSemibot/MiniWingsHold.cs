// Mirrors the wearer's tumble-wings (flight upgrade / zero-gravity / pink Heart Hugger) onto the mini. The menu-avatar clone has no ItemUpgradePlayerTumbleWingsLogic, so clone the wearer's wing visual once and drive it: body-relative pose mirror (wings ARE body-anchored, unlike the camera-anchored flashlight, so correct local AND remote), flap copied from the wearer's wing bones, and base/fresnel colour followed so the pink variant comes through. Under the "State Effects" toggle.

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class MiniWingsHold : MonoBehaviour
{
    // upgradeTumbleWingsVisualsActive is internal on PlayerAvatar → reflect once.
    private static readonly FieldInfo? VisualsActiveField =
        AccessTools.Field(typeof(PlayerAvatar), "upgradeTumbleWingsVisualsActive");

    private static readonly int BaseColorId    = Shader.PropertyToID("_BaseColor");
    private static readonly int FresnelColorId = Shader.PropertyToID("_FresnelColor");

    internal PlayerAvatar? WearerAvatar;
    internal MiniSemibotFollow? Follow;

    private GameObject? _clone;
    private bool _buildFailed;

    private Transform? _srcWings, _srcWingL, _srcWingR;
    private Transform? _cloneWingL, _cloneWingR;
    private MeshRenderer? _srcMeshL, _srcMeshR, _cloneMeshL, _cloneMeshR;

    private void LateUpdate()
    {
        var logic = WearerAvatar != null ? WearerAvatar.upgradeTumbleWingsLogic : null;
        var wv = Follow != null ? Follow.WearerVisuals : null;
        var mv = Follow != null ? Follow.MiniVisuals : null;

        bool visualsActive = WearerAvatar != null && VisualsActiveField != null
            && VisualsActiveField.GetValue(WearerAvatar) is true;

        bool want = MiniSemibotVisualPrefs.StateEffects
                    && (Follow == null || !MiniSemibotSpawner.IsMenuOrPreviewWearer(Follow.WearerVisuals))
                    && (Follow == null || !Follow.BodyHidden)
                    && logic != null && logic.transformWings != null
                    && visualsActive && wv != null && mv != null;

        if (!want)
        {
            if (_clone != null && _clone.activeSelf) _clone.SetActive(false);
            return;
        }

        if (_clone == null && !_buildFailed) BuildClone(logic!);
        if (_clone == null) return;
        if (!_clone.activeSelf) _clone.SetActive(true);

        float scale = MiniSemibotSync.Resolve(WearerAvatar).Scale;

        // Body-relative pose: the wings' world pose in the wearer's visuals frame, re-applied in the mini's frame (recomputed each frame so it follows the moving mini body).
        var t = _clone.transform;
        t.position = mv!.transform.TransformPoint(wv!.transform.InverseTransformPoint(_srcWings!.position));
        t.rotation = mv.transform.rotation * (Quaternion.Inverse(wv.transform.rotation) * _srcWings.rotation);
        t.localScale = _srcWings.lossyScale * scale;

        // Flap: copy the live wing-bone local rotations.
        if (_cloneWingL != null && _srcWingL != null) _cloneWingL.localRotation = _srcWingL.localRotation;
        if (_cloneWingR != null && _srcWingR != null) _cloneWingR.localRotation = _srcWingR.localRotation;

        // Colour follow (original ↔ pink Heart Hugger), read from the wearer's wing meshes.
        CopyWingColor(_srcMeshL, _cloneMeshL);
        CopyWingColor(_srcMeshR, _cloneMeshR);
    }

    private static void CopyWingColor(MeshRenderer? src, MeshRenderer? dst)
    {
        if (src == null || dst == null) return;
        var sm = src.material;
        var dm = dst.material;
        if (sm.HasProperty(BaseColorId)) dm.SetColor(BaseColorId, sm.GetColor(BaseColorId));
        if (sm.HasProperty(FresnelColorId)) dm.SetColor(FresnelColorId, sm.GetColor(FresnelColorId));
    }

    private void BuildClone(ItemUpgradePlayerTumbleWingsLogic logic)
    {
        try
        {
            _srcWings = logic.transformWings;
            _srcWingL = logic.transformWingLeft;
            _srcWingR = logic.transformWingRight;
            _srcMeshL = _srcWingL != null ? _srcWingL.GetComponentInChildren<MeshRenderer>(true) : null;
            _srcMeshR = _srcWingR != null ? _srcWingR.GetComponentInChildren<MeshRenderer>(true) : null;

            var clone = Object.Instantiate(_srcWings.gameObject);
            clone.name = "MHB_MiniWings";

            // Static prop: no logic, no audio, no extra lights leaking into the world.
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
            foreach (var a in clone.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
            foreach (var l in clone.GetComponentsInChildren<Light>(true)) l.enabled = false;

            int triggersLayer = LayerMask.NameToLayer("Triggers");
            if (triggersLayer >= 0)
                foreach (var tr in clone.GetComponentsInChildren<Transform>(true))
                    tr.gameObject.layer = triggersLayer;

            // Locate the clone's wing bones by name (the clone mirrors the source subtree).
            _cloneWingL = _srcWingL != null ? FindByName(clone.transform, _srcWingL.name) : null;
            _cloneWingR = _srcWingR != null ? FindByName(clone.transform, _srcWingR.name) : null;
            _cloneMeshL = _cloneWingL != null ? _cloneWingL.GetComponentInChildren<MeshRenderer>(true) : null;
            _cloneMeshR = _cloneWingR != null ? _cloneWingR.GetComponentInChildren<MeshRenderer>(true) : null;

            _clone = clone;   // driven in world space; NOT parented (scene root)
        }
        catch (System.Exception ex)
        {
            _buildFailed = true;
            BceConsole.LogWarning($"Mini-Semibot wings clone failed: {ex.Message}");
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
