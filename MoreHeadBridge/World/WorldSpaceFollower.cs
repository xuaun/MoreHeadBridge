using UnityEngine;

namespace MoreHeadBridge;

// Keeps the WorldDecorationFollower node at the player's world position
// while inheriting only the Y-axis rotation, so world cosmetics float
// at a fixed height and face the same direction as the player.
internal sealed class WorldSpaceFollower : MonoBehaviour
{
    private Transform? _root;
    private Vector3 _initialOffset;

    private void Start()
    {
        _root = transform.parent;
        if (_root == null) return;

        _initialOffset = transform.position - _root.position;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private void LateUpdate()
    {
        if (_root == null) return;

        transform.position = _root.position + _initialOffset;
        transform.rotation = Quaternion.Euler(0f, _root.eulerAngles.y, 0f);
    }
}
