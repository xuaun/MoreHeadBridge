using UnityEngine;

namespace MoreHeadBridge;

// Destroys an instanced Material with its GameObject so per-element clones (e.g. TMP outline materials) don't leak. Attach to the owner GO and set the field.
internal sealed class MaterialDestroyer : MonoBehaviour
{
    internal Material? material;
    private void OnDestroy() { if (material != null) Object.Destroy(material); }
}
