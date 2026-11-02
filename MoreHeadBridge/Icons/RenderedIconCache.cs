// Small copies of the icons the game renders for cosmetic mods, saved as compressed texture data so later sessions load them without rendering or decoding. Kept per profile and emptied when its .repobundle files or the game version change.

using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

internal static class RenderedIconCache
{
    // The menu shows icons at about 64 pixels, so the game's 512x512 only ever reaches the screen through mipmaps this size holds too. About 22 KB of memory against 340 KB.
    internal const int IconSize = 128;

    private const int FormatVersion = 3;
    private const double ReadbackTimeout = 5.0;

    private static readonly string Dir = Path.Combine(Paths.CachePath, "MoreHeadBridge", "RenderedIcons");
    private static readonly string SignatureFile = Path.Combine(Dir, "signature.txt");

    private static readonly Dictionary<CosmeticAsset, string> _paths = new();
    private static HashSet<string>? _saved;

    internal static bool Has(CosmeticAsset asset) => Plugin.SaveModdedIconsToDisk.Value && Saved().Contains(PathFor(asset));

    internal static bool HasAny() => Saved().Count > 0;

    internal static Sprite? Load(CosmeticAsset asset)
    {
        string path = PathFor(asset);
        try { return SpriteFromSaved(path, File.ReadAllBytes(path)); }
        catch (System.Exception ex) { return Forget(path, ex.Message); }
    }

    internal static async Task<Sprite?> LoadAsync(CosmeticAsset asset)
    {
        string path = PathFor(asset);
        try { return SpriteFromSaved(path, await File.ReadAllBytesAsync(path)); }
        catch (System.Exception ex) { return Forget(path, ex.Message); }
    }

    // Stands in for SemiIconMaker.CreateIconAsync on cosmetic mods: shrinks the pixels on a worker before compressing them.
    internal static async Task<Sprite> RenderAsync(SemiIconMaker maker, CosmeticAsset asset, System.Func<bool> isCurrent)
    {
        if (!isCurrent()) return null!;

        var readback = maker.CreateIconReadback();
        if (readback != null)
        {
            // Keeps waiting if the button goes away: the icon is still worth saving.
            double deadline = Time.realtimeSinceStartupAsDouble + ReadbackTimeout;
            while (!readback.done && Time.realtimeSinceStartupAsDouble < deadline)
                await Task.Yield();

            if (readback.done && !readback.failed && CanShrink(readback))
            {
                byte[] pixels = readback.pixels;
                int size = readback.width;
                byte[] small = await Task.Run(() => Shrink(pixels, size, IconSize));

                var texture = CompressedTexture(small, IconSize);
                if (texture.format == TextureFormat.DXT5) Save(asset, texture.GetRawTextureData());

                if (isCurrent()) return FinishSprite(texture);
                Object.Destroy(texture);
                return null!;
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
            _paths[asset] = path = Path.Combine(Dir, Hash128.Compute(asset.assetId) + ".icon");
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

            _saved.UnionWith(Directory.GetFiles(Dir, "*.icon"));
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

    // A plain average, like the game's mipmaps: the icon camera clears to gray with zero alpha, and that gray mixed into the edges is what keeps thin details like hair visible.
    private static byte[] Shrink(byte[] source, int sourceSize, int size)
    {
        int factor = sourceSize / size;
        int samples = factor * factor;
        int half = samples / 2;
        var result = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int row = 0; row < factor; row++)
                {
                    int i = ((y * factor + row) * sourceSize + x * factor) * 4;
                    for (int column = 0; column < factor; column++, i += 4)
                    {
                        r += source[i];
                        g += source[i + 1];
                        b += source[i + 2];
                        a += source[i + 3];
                    }
                }

                int o = (y * size + x) * 4;
                result[o] = (byte)((r + half) / samples);
                result[o + 1] = (byte)((g + half) / samples);
                result[o + 2] = (byte)((b + half) / samples);
                result[o + 3] = (byte)((a + half) / samples);
            }
        }
        return result;
    }

    private static void Save(CosmeticAsset asset, byte[] data)
    {
        if (!Plugin.SaveModdedIconsToDisk.Value) return;

        string path = PathFor(asset);
        Saved().Add(path);

        _ = Task.Run(() =>
        {
            string temp = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, data);
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

    // Same setup as the game's rendered icons.
    private static Texture2D CompressedTexture(byte[] pixels, int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true);
        texture.SetPixelData(pixels, 0);
        texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        texture.Compress(highQuality: false);
        return texture;
    }

    private static Sprite? SpriteFromSaved(string path, byte[] data)
    {
        var texture = new Texture2D(IconSize, IconSize, TextureFormat.DXT5, mipChain: true);
        try
        {
            texture.LoadRawTextureData(data);
        }
        catch (System.Exception ex)
        {
            Object.Destroy(texture);
            return Forget(path, ex.Message);
        }
        return FinishSprite(texture);
    }

    private static Sprite FinishSprite(Texture2D texture)
    {
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
