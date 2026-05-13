using UnityEngine;

namespace MoreHeadBridge;

// Builds a procedural 64x64 Sprite once and caches it. Used as a last-resort fallback
// when a bridge cosmetic has neither a SemiIconMaker, nor a cached PNG, nor any usable
// texture extracted from its prefab. Better than a blank cell.
//
// IMPORTANT: Unity Texture2D coordinates have (0,0) at the BOTTOM-LEFT. Sprites preserve
// that orientation when rendered. So "top of visible M" = HIGH y in the array.
internal static class PlaceholderIcon
{
    private const int Size = 64;
    private static Sprite? _cached;

    internal static Sprite Get()
    {
        if (_cached != null) return _cached;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.name = "MoreHeadBridge_Placeholder";
        tex.filterMode = FilterMode.Point;

        Color border  = new Color(1.00f, 0.80f, 0.00f, 1f);   // yellow
        Color bgDark  = new Color(0.13f, 0.13f, 0.18f, 1f);
        Color bgLight = new Color(0.22f, 0.22f, 0.28f, 1f);
        Color stripe  = new Color(1.00f, 0.80f, 0.00f, 0.35f);

        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                bool isBorder = x < 3 || x >= Size - 3 || y < 3 || y >= Size - 3;
                bool onStripe = ((x + y) / 4) % 2 == 0;

                Color c;
                if (isBorder)       c = border;
                else if (onStripe)  c = Color.Lerp(bgDark, stripe, 0.5f);
                else                c = bgLight;

                pixels[y * Size + x] = c;
            }
        }

        DrawM(pixels, Size, border);

        tex.SetPixels(pixels);
        tex.Apply();

        _cached = Sprite.Create(
            tex,
            new Rect(0, 0, Size, Size),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 64f);
        _cached.name = "MoreHeadBridge_Placeholder";
        return _cached;
    }

    // Draws a stylized M: two vertical bars + two diagonals meeting in the middle bottom.
    // Coordinates are in TEXTURE space: y=0 is bottom, y=Size-1 is top.
    private static void DrawM(Color[] pixels, int size, Color color)
    {
        const int leftX  = 18;
        const int rightX = 45;
        const int topY    = 50;  // visual top of M (high y)
        const int bottomY = 14;  // visual bottom of M (low y)
        const int midX    = (leftX + rightX) / 2;
        const int midY    = bottomY + (topY - bottomY) / 3; // V of M dips ~1/3 from bottom

        // Vertical bars (left + right), 2px thick
        for (int y = bottomY; y <= topY; y++)
        {
            Plot(pixels, size, leftX,     y, color);
            Plot(pixels, size, leftX + 1, y, color);
            Plot(pixels, size, rightX,    y, color);
            Plot(pixels, size, rightX - 1, y, color);
        }

        // Diagonals from top corners DOWN to (midX, midY).
        int steps = topY - midY;
        for (int i = 0; i <= steps; i++)
        {
            int y  = topY - i;
            int xL = leftX  + i * (midX - leftX) / steps;
            int xR = rightX - i * (rightX - midX) / steps;
            Plot(pixels, size, xL,     y, color);
            Plot(pixels, size, xL + 1, y, color);
            Plot(pixels, size, xR,     y, color);
            Plot(pixels, size, xR - 1, y, color);
        }
    }

    private static void Plot(Color[] pixels, int size, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= size || y >= size) return;
        pixels[y * size + x] = color;
    }
}
