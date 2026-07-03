using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Tracks the mesh-SWITCH swap renderers PartShrinkerBridge disabled to keep a hidden body part hidden
// when its base mesh has been swapped (HiddenParts only knows the fixed-name vanilla base meshes).
// Lives on the avatar; Restore() re-enables exactly what we turned off so a re-shown part comes back.
internal sealed class BridgeSwapMeshHider : MonoBehaviour
{
    private readonly List<Renderer> _disabled = new();

    internal void Track(Renderer r)
    {
        r.enabled = false;
        _disabled.Add(r);
    }

    internal void Restore()
    {
        foreach (var r in _disabled)
            if (r != null) r.enabled = true;
        _disabled.Clear();
    }
}
