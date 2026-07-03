// ============================================================================
// Loads the star/hide embedded PNGs, makes the near-white background transparent, and caches the Sprites for FavHideMarkerHelper.
// ============================================================================

using System;
using UnityEngine;

namespace MoreHeadBridge;

internal static class FavHideIcons
{
    // Matches the default MSBuild logical name <AssemblyName>.<FolderPath>.<FileName>; PNGs live in Icons\Resources\ → MoreHeadBridge.Icons.Resources.<file>.
    private const string ResourcePrefix = "MoreHeadBridge.Icons.Resources.";

    private static Sprite? _star;
    private static Sprite? _hide;

    internal static Sprite? StarSprite => _star ??= LoadSprite("star.png");
    internal static Sprite? HideSprite => _hide ??= LoadSprite("hide.png");

    private static Sprite? LoadSprite(string fileName)
    {
        try
        {
            var resourceName = ResourcePrefix + fileName;
            // typeof(FavHideIcons).Assembly is always this assembly even when called from another — more explicit than Assembly.GetExecutingAssembly().
            using var stream = typeof(FavHideIcons).Assembly
                                                   .GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                BceConsole.LogWarning(
                    $"FavHideIcons: embedded resource '{resourceName}' not found");
                return null;
            }

            var bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length)
                read += stream.Read(bytes, read, bytes.Length - read);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.name        = $"MoreHeadBridge_{fileName}";
            tex.filterMode  = FilterMode.Bilinear;

            if (!tex.LoadImage(bytes))
            {
                BceConsole.LogWarning(
                    $"FavHideIcons: Texture2D.LoadImage failed for '{resourceName}'");
                return null;
            }

            MaskWhitePixels(tex);

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: tex.width);
            sprite.name = $"MoreHeadBridge_{fileName}";
            return sprite;
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning(
                $"FavHideIcons: error loading '{fileName}': {ex.Message}");
            return null;
        }
    }

    // All three channels above 220 → background, made fully transparent; coloured/dark foreground keeps its colour and alpha.
    private static void MaskWhitePixels(Texture2D tex)
    {
        var pixels = tex.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p.r > 220 && p.g > 220 && p.b > 220)
                pixels[i].a = 0;
        }
        tex.SetPixels32(pixels);
        tex.Apply();
    }
}
