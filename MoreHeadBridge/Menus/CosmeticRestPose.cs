using UnityEngine;

namespace MoreHeadBridge;

// The local transform a cosmetic's root rests in under its anchor: the prefab's, which many .hhh keep far from the anchor, scaled or turned.
// Offsets and impact poses are stored as absolute local transforms, the way the game's CosmeticOffsetCondition reads them, so their popups show them relative to this: a new entry starts where the cosmetic already is.
internal readonly struct CosmeticRestPose
{
    internal static readonly CosmeticRestPose OnAnchor = new(Vector3.zero, Quaternion.identity, Vector3.one, Vector3.zero);

    // A root this far from the anchor was left where the model was exported, not placed on the cosmetic.
    private const float StrayRootDistance = 0.25f;

    private readonly Vector3 _position;
    private readonly Quaternion _rotation;
    private readonly Vector3 _scale;
    private readonly Vector3 _pivot;   // what rotation and scale turn around, in the root's frame

    private CosmeticRestPose(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 pivot)
    {
        _position = position;
        _rotation = rotation;
        _scale = scale;
        _pivot = pivot;
    }

    // resetTransform: the anchor's flag, with which the game puts the cosmetic right on the anchor.
    internal static CosmeticRestPose Of(CosmeticAsset? asset, bool resetTransform)
    {
        var prefab = !resetTransform && asset != null ? asset.prefab?.Prefab : null;
        if (prefab == null) return OnAnchor;

        var root = prefab.transform;
        return new CosmeticRestPose(root.localPosition, root.localRotation, root.localScale, PivotOf(prefab));
    }

    // The root, as the author placed it. A stray root outside the mesh is empty space the mesh would swing around, so the mesh's center takes its place.
    private static Vector3 PivotOf(GameObject prefab)
    {
        if (prefab.transform.localPosition.magnitude < StrayRootDistance) return Vector3.zero;
        if (!MoreHeadCosmeticMountPatch.TryGetMeshBoundsRootLocal(prefab, out var bounds)) return Vector3.zero;
        return bounds.center.magnitude > bounds.extents.magnitude ? bounds.center : Vector3.zero;
    }

    internal void ApplyTo(Transform transform)
    {
        transform.localPosition = _position;
        transform.localRotation = _rotation;
        transform.localScale = _scale;
    }

    // Solves the root's position so the pivot stays where it rests, plus the position slider. Same math as the fit offsets in InjectOffsetConditions.
    internal (Vector3 position, Vector3 rotation, Vector3 scale) ToStored(Vector3 position, Vector3 rotation, Vector3 scale)
    {
        var storedRotation = _rotation * Quaternion.Euler(rotation);
        var storedScale = Vector3.Scale(_scale, scale);
        return (RootHoldingPivot(storedRotation, storedScale) + position,
                IsTurned ? storedRotation.eulerAngles : rotation,
                storedScale);
    }

    internal (Vector3 position, Vector3 rotation, Vector3 scale) ToShown(Vector3 position, Vector3 rotation, Vector3 scale)
    {
        var storedRotation = Quaternion.Euler(rotation);
        return (position - RootHoldingPivot(storedRotation, scale),
                IsTurned ? Wrap180((Quaternion.Inverse(_rotation) * storedRotation).eulerAngles) : rotation,
                new Vector3(Ratio(scale.x, _scale.x), Ratio(scale.y, _scale.y), Ratio(scale.z, _scale.z)));
    }

    private Vector3 RootHoldingPivot(Quaternion rotation, Vector3 scale)
        => _position + _rotation * Vector3.Scale(_scale, _pivot) - rotation * Vector3.Scale(scale, _pivot);

    private bool IsTurned => Quaternion.Angle(_rotation, Quaternion.identity) > 0.01f;

    private static float Ratio(float value, float rest) => Mathf.Approximately(rest, 0f) ? value : value / rest;

    private static Vector3 Wrap180(Vector3 euler)
        => new(Mathf.DeltaAngle(0f, euler.x), Mathf.DeltaAngle(0f, euler.y), Mathf.DeltaAngle(0f, euler.z));
}
