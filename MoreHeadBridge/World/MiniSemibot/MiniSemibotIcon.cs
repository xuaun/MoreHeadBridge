using UnityEngine;

namespace MoreHeadBridge;

// Procedural Mini-Semibot icon (person silhouette on a rounded panel) — no asset/timing dependencies, reads clearly at small sizes.
internal static class MiniSemibotIcon
{
    private static Sprite? _cached;

    internal static Sprite Create()
    {
        if (_cached != null) return _cached;

        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color[S * S];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0f, 0f, 0f, 0f);
        tex.SetPixels(pixels);

        // Soft dark rounded panel so the silhouette reads on any button background.
        FillRoundedRect(tex, 12, 12, S - 12, S - 12, 22, new Color(0.10f, 0.12f, 0.16f, 0.85f));

        // Light "mini person": body disc (lower, larger) + head disc (upper, smaller).
        var silhouette = new Color(0.85f, 0.93f, 1f, 1f);
        FillDisc(tex, S / 2, 46, 30, silhouette);
        FillDisc(tex, S / 2, 90, 20, silhouette);

        tex.Apply();

        _cached = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
        _cached.name = "MHB_MiniSemibotIcon";
        return _cached;
    }

    private static void FillDisc(Texture2D tex, int cx, int cy, int r, Color c)
    {
        int r2 = r * r;
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= tex.height) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= tex.width) continue;
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r2) tex.SetPixel(x, y, c);
            }
        }
    }

    // Filled rectangle [x0,x1)×[y0,y1) with rounded corners of the given radius.
    private static void FillRoundedRect(Texture2D tex, int x0, int y0, int x1, int y1, int radius, Color c)
    {
        int r2 = radius * radius;
        for (int y = y0; y < y1; y++)
        {
            if (y < 0 || y >= tex.height) continue;
            for (int x = x0; x < x1; x++)
            {
                if (x < 0 || x >= tex.width) continue;

                // Skip the four rounded corners.
                int cornerX = (x < x0 + radius) ? x0 + radius : (x >= x1 - radius) ? x1 - radius - 1 : x;
                int cornerY = (y < y0 + radius) ? y0 + radius : (y >= y1 - radius) ? y1 - radius - 1 : y;
                int dx = x - cornerX, dy = y - cornerY;
                if (dx * dx + dy * dy > r2) continue;

                tex.SetPixel(x, y, c);
            }
        }
    }
}
