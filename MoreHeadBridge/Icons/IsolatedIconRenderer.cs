// Isolated icon renderer (gated by UseIsolatedIconRender): .hhh cosmetics lack vanilla's "SemiIcon Maker" rig, so reproduce it generically — instantiate the prefab far off-scene, frame with a 26° camera + key/fill lights, render to a transparent RT. One synchronous call; the temp camera/lights never touch the game's own rendering.

using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class IsolatedIconRenderer
{
    private const int RenderSize = 256;                 // supersample, then IconCapture downscales
    private const float Fov = 26f;                      // matches the vanilla SemiIcon Maker camera
    private static readonly Vector3 StageOrigin = new(0f, 100000f, 0f); // far from any scene geometry

    // Renders the cosmetic in isolation, returning a square transparent texture (caller owns it and must Destroy it). Null when the prefab is missing or has no renderers to frame.
    internal static Texture2D? Render(CosmeticAsset asset)
    {
        var prefab = asset?.prefab?.Prefab;
        if (prefab == null) return null;

        GameObject? root = null;
        RenderTexture? rt = null;
        var temps = new List<GameObject>();
        var prevActive = RenderTexture.active;

        try
        {
            root = Object.Instantiate(prefab, StageOrigin, Quaternion.identity);
            root.name = "BridgeIconRender";

            // Freeze any behaviour that could move/scale/hide the mesh (equip animation, springs, hide conditions) — a static mesh render only needs the renderers.
            foreach (var b in root.GetComponentsInChildren<Behaviour>(includeInactive: true))
                if (b is not Camera) b.enabled = false;

            if (!TryGetBounds(root, out var bounds)) return null;

            float radius = Mathf.Max(0.0001f, bounds.extents.magnitude);
            float dist = radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) * 1.2f;
            // 3/4 front view (front + a little side + top) — a generic flattering framing.
            Vector3 dir = new Vector3(0.35f, 0.25f, 1f).normalized;
            Vector3 camPos = bounds.center + dir * dist;

            // ── Camera ───────────────────────────────────────────────────────
            var camGO = new GameObject("BridgeIconCam");
            temps.Add(camGO);
            camGO.transform.position = camPos;
            camGO.transform.LookAt(bounds.center, Vector3.up);

            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent
            cam.fieldOfView = Fov;
            cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
            cam.farClipPlane = dist + radius * 4f;
            cam.cullingMask = ~0;                 // nothing else is at the far stage origin
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.enabled = false;                  // we drive it manually with Render()

            // ── Lights (key + fill), parented near the stage so they only matter this frame ──
            AddDirLight(temps, camPos, bounds.center, Quaternion.Euler(35f, -25f, 0f), 1.2f);
            AddDirLight(temps, camPos, bounds.center, Quaternion.Euler(15f, 160f, 0f), 0.55f);

            // ── Render ─────────────────────────────────────────────────────────
            rt = new RenderTexture(RenderSize, RenderSize, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 2,
            };
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            RenderTexture.active = rt;
            var tex = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
            tex.Apply();
            return tex;
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"IsolatedIconRenderer: render failed for '{asset?.name}': {ex.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (root != null) Object.Destroy(root);
            foreach (var go in temps) if (go != null) Object.Destroy(go);
        }
    }

    private static void AddDirLight(List<GameObject> temps, Vector3 near, Vector3 lookAt,
                                    Quaternion rotation, float intensity)
    {
        var go = new GameObject("BridgeIconLight");
        temps.Add(go);
        go.transform.position = near;            // position is irrelevant for directional lights
        go.transform.rotation = rotation;
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = Color.white;
        light.shadows = LightShadows.None;
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }
}
