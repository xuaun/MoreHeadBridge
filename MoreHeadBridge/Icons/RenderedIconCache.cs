// Small PNGs of the icons the game renders for cosmetic mods, so later sessions load them instead of rendering. Kept per profile and emptied when its .repobundle files or the game version change.

using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace MoreHeadBridge;

internal static class RenderedIconCache
{
    // Same size as the .hhh icons. The game's 512x512 takes about 340 KB of memory against 20 KB, for an icon that fills 34 UI units on screen.
    internal const int IconSize = 128;

    private const int FormatVersion = 1;
    private const double ReadbackTimeout = 5.0;

    private static readonly string Dir = Path.Combine(Paths.CachePath, "MoreHeadBridge", "RenderedIcons");
    private static readonly string SignatureFile = Path.Combine(Dir, "signature.txt");

    private static readonly Dictionary<CosmeticAsset, string> _paths = new();
    private static HashSet<string>? _saved;

    internal static bool Has(CosmeticAsset asset) => Saved().Contains(PathFor(asset));

    internal static bool HasAny() => Saved().Count > 0;

    internal static Sprite? Load(CosmeticAsset asset)
    {
        string path = PathFor(asset);
        try { return SpriteFromPng(path, File.ReadAllBytes(path)); }
        catch (System.Exception ex) { return Forget(path, ex.Message); }
    }

    internal static async Task<Sprite?> LoadAsync(CosmeticAsset asset)
    {
        string path = PathFor(asset);
        try { return SpriteFromPng(path, await File.ReadAllBytesAsync(path)); }
        catch (System.Exception ex) { return Forget(path, ex.Message); }
    }

    // Stands in for SemiIconMaker.CreateIconAsync on cosmetic mods: shrinks the pixels on a worker instead of compressing 512x512 on the main thread.
    internal static async Task<Sprite> RenderAsync(SemiIconMaker maker, CosmeticAsset asset, System.Func<bool> isCurrent)
    {
        if (!isCurrent()) return null!;

        var readback = maker.CreateIconReadback();
        if (readback != null)
        {
            // Keeps waiting if the button goes away: the PNG is still worth saving.
            double deadline = Time.realtimeSinceStartupAsDouble + ReadbackTimeout;
            while (!readback.done && Time.realtimeSinceStartupAsDouble < deadline)
                await Task.Yield();

            if (readback.done && !readback.failed && CanShrink(readback))
            {
                byte[] pixels = readback.pixels;
                int size = readback.width;
                byte[] small = await Task.Run(() => Shrink(pixels, size));
                Save(asset, small);
                return isCurrent() ? SpriteFromPixels(small) : null!;
            }

            if (readback.done && !readback.failed && isCurrent())
            {
                var sprite = readback.CreateSprite(null);
                if (sprite != null) return sprite;
            }
        }

        return maker != null && isCurrent() ? maker.CreateIconFromRenderTexture(null) : null!;
    }

    // Deletes every saved icon and asks the open menu's buttons for new renders. Returns how many files were deleted.
    internal static int Clear()
    {
        int deleted = DeleteSavedFiles();
        _saved?.Clear();

        var meta = MetaManager.instance;
        if (meta == null) return deleted;

        foreach (var asset in meta.cosmeticAssets)
        {
            if (!BridgeIds.IsModdedCosmetic(asset) || asset.icon == null) continue;

            var texture = asset.icon.texture;
            if (texture == null || texture.width > IconSize) continue;

            // Immediately: the buttons below check their icon right away.
            Object.DestroyImmediate(asset.icon);
            Object.DestroyImmediate(texture);
            asset.icon = null;
        }

        var page = Object.FindFirstObjectByType<MenuPageCosmetics>();
        if (page == null) return deleted;

        foreach (var button in page.GetComponentsInChildren<MenuElementCosmeticButton>())
        {
            if (!BridgeIds.IsModdedCosmetic(button.cosmeticAsset) || button.cosmeticAsset.icon != null) continue;
            button.LoadIconAsync();
            button.UpdateIcon();
        }
        return deleted;
    }

    private static string PathFor(CosmeticAsset asset)
    {
        if (!_paths.TryGetValue(asset, out var path))
            _paths[asset] = path = Path.Combine(Dir, Hash128.Compute(asset.assetId) + ".png");
        return path;
    }

    // Known files, read once per session after checking the signature, so no File.Exists per button.
    private static HashSet<string> Saved()
    {
        if (_saved != null) return _saved;

        _saved = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(Dir);

            string signature = CurrentSignature();
            if (!File.Exists(SignatureFile) || File.ReadAllText(SignatureFile) != signature)
            {
                DeleteSavedFiles();
                File.WriteAllText(SignatureFile, signature);
            }

            _saved.UnionWith(Directory.GetFiles(Dir, "*.png"));
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"RenderedIconCache: could not read the cache: {ex.Message}");
        }
        return _saved;
    }

    // Changes when a cosmetic bundle is added, removed or updated.
    private static string CurrentSignature()
    {
        var text = new StringBuilder($"{FormatVersion}|{IconSize}|{Application.version}");
        var bundles = Directory.GetFiles(Paths.PluginPath, "*.repobundle", SearchOption.AllDirectories)
            .OrderBy(path => path, System.StringComparer.OrdinalIgnoreCase);

        foreach (string bundle in bundles)
        {
            var info = new FileInfo(bundle);
            text.Append('|').Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
        }
        return Hash128.Compute(text.ToString()).ToString();
    }

    private static int DeleteSavedFiles()
    {
        if (!Directory.Exists(Dir)) return 0;

        int deleted = 0;
        foreach (string file in Directory.GetFiles(Dir))
        {
            if (file == SignatureFile) continue;
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (System.Exception ex)
            {
                BridgeLog.Trace($"RenderedIconCache: could not delete '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
        return deleted;
    }

    // Other render sizes keep the game's path, unsaved.
    private static bool CanShrink(SemiIconMaker.IconReadback readback)
        => readback.pixels != null
           && readback.width == readback.height
           && readback.width >= IconSize
           && readback.width % IconSize == 0
           && readback.pixels.Length == readback.width * readback.height * 4;

    // Averages weighted by alpha, so transparent pixels don't darken the edges.
    private static byte[] Shrink(byte[] source, int sourceSize)
    {
        int factor = sourceSize / IconSize;
        int samples = factor * factor;
        var result = new byte[IconSize * IconSize * 4];

        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int row = 0; row < factor; row++)
                {
                    int i = ((y * factor + row) * sourceSize + x * factor) * 4;
                    for (int column = 0; column < factor; column++, i += 4)
                    {
                        int alpha = source[i + 3];
                        r += source[i] * alpha;
                        g += source[i + 1] * alpha;
                        b += source[i + 2] * alpha;
                        a += alpha;
                    }
                }

                if (a == 0) continue;

                int o = (y * IconSize + x) * 4;
                result[o] = (byte)(r / a);
                result[o + 1] = (byte)(g / a);
                result[o + 2] = (byte)(b / a);
                result[o + 3] = (byte)(a / samples);
            }
        }
        return result;
    }

    private static void Save(CosmeticAsset asset, byte[] pixels)
    {
        string path = PathFor(asset);
        Saved().Add(path);

        _ = Task.Run(() =>
        {
            string temp = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_SRGB, IconSize, IconSize));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (System.Exception ex)
            {
                BridgeLog.Trace($"RenderedIconCache: could not save '{Path.GetFileName(path)}': {ex.Message}");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        });
    }

    private static Sprite SpriteFromPixels(byte[] pixels)
    {
        var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, mipChain: true);
        texture.SetPixelData(pixels, 0);
        texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        return FinishSprite(texture);
    }

    private static Sprite? SpriteFromPng(string path, byte[] png)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
        if (!texture.LoadImage(png, markNonReadable: false))
        {
            Object.Destroy(texture);
            return Forget(path, "invalid PNG");
        }

        texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        return FinishSprite(texture);
    }

    // Same setup as the game's rendered icons.
    private static Sprite FinishSprite(Texture2D texture)
    {
        texture.Compress(highQuality: false);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    // A file that can't be loaded is rendered and saved again.
    private static Sprite? Forget(string path, string reason)
    {
        Saved().Remove(path);
        BridgeLog.Trace($"RenderedIconCache: could not load '{Path.GetFileName(path)}': {reason}");
        return null;
    }
}
