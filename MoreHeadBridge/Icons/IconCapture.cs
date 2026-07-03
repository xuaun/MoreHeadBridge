// ============================================================================
// [MenuCapture] — captures the cosmetics-menu avatar render texture and saves it as a PNG icon for a CosmeticAsset. Used by CosmeticHoverPatch (capture on hover) and BatchIconGenerator (one-shot batch).
// ============================================================================

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class IconCapture
{
    private const int OutSize = 128;

    // Private cache dir: %persistentDataPath%\Cache\Icons\CosmeticsModded\MoreHeadBridge_CosmeticsIcons\. REPOLib's MetaManagerPatch wipes Cache\Icons\Cosmetics\ of non-vanilla PNGs, but our folder is a SIBLING under CosmeticsModded\ so it's untouched; GetIconPatch loads from here directly. Legacy path (< fix/folder-paths) %persistentDataPath%\MoreHeadBridge_Icons\ is migrated automatically on first run.
    private static string? _cacheDir;
    internal static string CacheDir
    {
        get
        {
            if (_cacheDir != null) return _cacheDir;
            _cacheDir = Path.Combine(Application.persistentDataPath,
                                     "Cache", "Icons", "CosmeticsModded", "MoreHeadBridge_CosmeticsIcons");
            MigrateLegacyCache(_cacheDir);
            return _cacheDir;
        }
    }

    // Migrates PNGs from the old root-level cache folder; runs once when CacheDir is first evaluated. Non-fatal — icons can be regenerated.
    private static void MigrateLegacyCache(string newDir)
    {
        string oldDir = Path.Combine(Application.persistentDataPath, "MoreHeadBridge_Icons");
        if (!Directory.Exists(oldDir)) return;

        BceConsole.LogInfo($"IconCapture: migrating icon cache from legacy location...");
        try
        {
            Directory.CreateDirectory(newDir);
            int moved = 0, failed = 0;

            foreach (string file in Directory.GetFiles(oldDir, "*.png"))
            {
                string dest = Path.Combine(newDir, Path.GetFileName(file));
                try
                {
                    if (!File.Exists(dest))
                        File.Move(file, dest);
                    else
                        File.Delete(file); // already migrated on a previous partial run
                    moved++;
                }
                catch (Exception ex)
                {
                    failed++;
                    BceConsole.LogWarning(
                        $"IconCapture: could not migrate '{Path.GetFileName(file)}': {ex.Message}");
                }
            }

            try
            {
                if (Directory.GetFiles(oldDir).Length == 0)
                    Directory.Delete(oldDir, recursive: false);
            }
            catch { /* non-fatal — leave the empty folder, it causes no harm */ }

            BceConsole.LogInfo(
                $"IconCapture: cache migration done — {moved} moved, {failed} failed");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"IconCapture: cache migration failed: {ex.Message}");
        }
    }

    internal static string CachePathFor(CosmeticAsset asset)
    {
        string name = asset.name.Replace("(Clone)", "").Trim().ToLowerInvariant();
        return Path.Combine(CacheDir, MakeSafeFileName(name) + ".png");
    }

    // Strips path separators and ".." so a cosmetic name can't escape the cache directory.
    private static string MakeSafeFileName(string name)
    {
        name = name.Replace('/', '_').Replace('\\', '_').Replace("..", "__");
        return name.Length == 0 ? "_unnamed" : name;
    }

    // In-memory set of known-cached paths — GetIcon fires for every visible button during RefreshScrollContent, so no File.Exists per call. Seeded lazily, updated on each capture write.
    private static HashSet<string>? _knownCached;

    private static void EnsureCacheSeeded()
    {
        if (_knownCached != null) return;
        _knownCached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(CacheDir))
            {
                foreach (string f in Directory.GetFiles(CacheDir, "*.png"))
                    _knownCached.Add(f);
            }
        }
        catch { /* non-fatal — worst case we miss a file and regenerate it */ }
    }

    /// Marks a path as cached in the in-memory set.
    /// Call after successfully writing a PNG to the cache directory.
    internal static void MarkCached(string path)
    {
        EnsureCacheSeeded();
        _knownCached!.Add(path);
    }

    /// Returns true if a cached PNG exists for this asset (in-memory O(1) check).
    internal static bool HasCache(CosmeticAsset asset)
    {
        EnsureCacheSeeded();
        return _knownCached!.Contains(CachePathFor(asset));
    }

    /// Registers an externally written PNG (e.g. the Mini-Semibot preset-style icon) in the
    /// in-memory cache so HasCache() is true without waiting for a disk reseed.
    internal static void MarkCached(CosmeticAsset asset)
    {
        EnsureCacheSeeded();
        _knownCached!.Add(CachePathFor(asset));
    }

    /// Deletes the cached PNG for the given asset from disk and from the in-memory
    /// set, then clears the Sprite so GetIconPatch re-evaluates on the next call.
    internal static void DeleteCache(CosmeticAsset asset)
    {
        EnsureCacheSeeded();
        string path = CachePathFor(asset);

        _knownCached!.Remove(path);

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"IconCapture: could not delete '{Path.GetFileName(path)}': {ex.Message}");
        }

        if (asset.icon != null)
        {
            UnityEngine.Object.Destroy(asset.icon);
            asset.icon = null;
        }

        // Allow the hover-capture coroutine to fire again on the next hover — without this, _scheduled still holds the assetId and blocks re-capture.
        CosmeticHoverPatch.Invalidate(asset);

        // Re-enable the Generate Icons toolbar button now that there is something to generate.
        CosmeticsMenuStartPatch.RefreshToolsButtons?.Invoke();

        BridgeLog.Trace($"IconCapture: deleted cached icon for '{asset.name}'");
    }

    private static FieldInfo? _renderTextureInstanceField;

    private static RenderTexture? FindActiveAvatarRT()
    {
        var avatar = UnityEngine.Object.FindObjectOfType<PlayerAvatarMenuHover>();
        if (avatar == null) return null;

        _renderTextureInstanceField ??= AccessTools.Field(typeof(PlayerAvatarMenuHover), "renderTextureInstance");
        if (_renderTextureInstanceField == null)
            BceConsole.LogWarning("IconCapture: PlayerAvatarMenuHover.renderTextureInstance not found — update MoreHeadBridge");
        else
        {
            var rt = _renderTextureInstanceField.GetValue(avatar) as RenderTexture;
            if (rt != null) return rt;
        }

        var rawImage = avatar.GetComponent<RawImage>();
        return rawImage != null ? rawImage.texture as RenderTexture : null;
    }

    internal static bool TryCapture(CosmeticAsset asset)
        => TryCapture(
            asset,
            HhhCosmeticLoader.IsWorldAsset(asset)
                ? (SemiFunc.CosmeticType)(-1)
                : asset?.type ?? SemiFunc.CosmeticType.Hat);

    // type lets us crop the avatar shot to just the relevant body part.
    internal static bool TryCapture(CosmeticAsset asset, SemiFunc.CosmeticType type)
    {
        if (asset == null) return false;
        if (HasCache(asset)) return false;

        // Isolated render (SemiIconMaker-style) instead of cropping the live avatar; per-cosmetic override falls back to UseIsolatedIconRender. Bridge only (.hhh prefab is a self-contained mesh) — never Mini-Semibot, whose placeholder prefab has no mesh.
        if (BridgeIds.IsBridgeAsset(asset) && asset.assetId != MiniSemibotCosmetic.AssetId
            && CustomizerStore.GetEffectiveIsolatedIcon(asset.assetId))
            return TryCaptureIsolated(asset);

        Texture2D? full = null;
        Texture2D? cropped = null;
        Texture2D? scaled = null;
        var prevActive = RenderTexture.active;

        try
        {
            var rt = FindActiveAvatarRT();
            if (rt == null) return false;

            Directory.CreateDirectory(CacheDir);

            RenderTexture.active = rt;
            full = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            full.Apply();

            Rect cropNorm = GetCropRect(type);
            int cropX = Mathf.RoundToInt(cropNorm.x * rt.width);
            int cropY = Mathf.RoundToInt(cropNorm.y * rt.height);
            int cropW = Mathf.RoundToInt(cropNorm.width * rt.width);
            int cropH = Mathf.RoundToInt(cropNorm.height * rt.height);
            cropW = Mathf.Max(1, Mathf.Min(cropW, rt.width - cropX));
            cropH = Mathf.Max(1, Mathf.Min(cropH, rt.height - cropY));

            var cropPixels = full.GetPixels(cropX, cropY, cropW, cropH);
            cropped = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
            cropped.SetPixels(cropPixels);
            cropped.Apply();

            scaled = ResizeBilinear(cropped, OutSize, OutSize);

            string cachePath = CachePathFor(asset);
            File.WriteAllBytes(cachePath, scaled.EncodeToPNG());
            MarkCached(cachePath);

            // RefreshVisibleButtons below triggers UpdateIcon → GetIconPatch, which recreates the icon from the freshly-written PNG.
            if (asset.icon != null)
            {
                UnityEngine.Object.Destroy(asset.icon);
                asset.icon = null;
            }
            RefreshVisibleButtons(asset);
            CosmeticsMenuStartPatch.RefreshToolsButtons?.Invoke();

            return true;
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"Icon capture failed for '{asset.name}': {ex.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (full != null) UnityEngine.Object.Destroy(full);
            if (cropped != null) UnityEngine.Object.Destroy(cropped);
            if (scaled != null) UnityEngine.Object.Destroy(scaled);
        }
    }

    // Renders the cosmetic in isolation (SemiIconMaker-style) and saves the PNG, sharing the cache + button-refresh tail with the avatar-crop path. Returns true on success.
    private static bool TryCaptureIsolated(CosmeticAsset asset)
    {
        Texture2D? rendered = null;
        Texture2D? scaled = null;
        try
        {
            rendered = IsolatedIconRenderer.Render(asset);
            if (rendered == null) return false;

            Directory.CreateDirectory(CacheDir);
            scaled = ResizeBilinear(rendered, OutSize, OutSize);

            string cachePath = CachePathFor(asset);
            File.WriteAllBytes(cachePath, scaled.EncodeToPNG());
            MarkCached(cachePath);

            if (asset.icon != null)
            {
                UnityEngine.Object.Destroy(asset.icon);
                asset.icon = null;
            }
            RefreshVisibleButtons(asset);
            CosmeticsMenuStartPatch.RefreshToolsButtons?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"Isolated icon render failed for '{asset.name}': {ex.Message}");
            return false;
        }
        finally
        {
            if (rendered != null) UnityEngine.Object.Destroy(rendered);
            if (scaled != null) UnityEngine.Object.Destroy(scaled);
        }
    }

    // Crop regions in normalized UV space, calibrated empirically against the avatar preview RT. If the rig/camera/resolution changes after a game update, re-tune and delete the icon cache.
    private static readonly Rect CropHead = new(0.22f, 0.62f, 0.56f, 0.35f); // head, face, ears, eyewear
    private static readonly Rect CropNeck = new(0.22f, 0.50f, 0.56f, 0.38f); // neck / lower-face (HeadBottom) — starts below head, still above torso
    private static readonly Rect CropBody = new(0.18f, 0.34f, 0.64f, 0.36f); // torso
    private static readonly Rect CropArmR = new(0.05f, 0.30f, 0.50f, 0.40f); // right arm → left side of frame
    private static readonly Rect CropArmL = new(0.45f, 0.30f, 0.50f, 0.40f); // left arm  → right side of frame
    private static readonly Rect CropLegR = new(0.10f, 0.00f, 0.45f, 0.45f); // right leg/foot → left side
    private static readonly Rect CropLegL = new(0.45f, 0.00f, 0.45f, 0.45f); // left leg/foot  → right side
    private static readonly Rect CropFull = new(0f, 0f, 1f, 1f);     // world / unknown → full frame

    private static Rect GetCropRect(SemiFunc.CosmeticType type)
    {
        switch (type)
        {
            case SemiFunc.CosmeticType.Hat:
            case SemiFunc.CosmeticType.HeadTopMesh:
            case SemiFunc.CosmeticType.HeadTopOverlay:
            case SemiFunc.CosmeticType.FaceTop:
            case SemiFunc.CosmeticType.FaceBottom:
            case SemiFunc.CosmeticType.Eyewear:
            case SemiFunc.CosmeticType.Ears:
            case SemiFunc.CosmeticType.EyeLidRightMesh:
            case SemiFunc.CosmeticType.EyeLidLeftMesh:
                return CropHead;

            case SemiFunc.CosmeticType.HeadBottom:
            case SemiFunc.CosmeticType.HeadBottomMesh:
            case SemiFunc.CosmeticType.HeadBottomOverlay:
                return CropNeck;

            case SemiFunc.CosmeticType.BodyTop:
            case SemiFunc.CosmeticType.BodyTopMesh:
            case SemiFunc.CosmeticType.BodyBottom:
            case SemiFunc.CosmeticType.BodyBottomMesh:
            case SemiFunc.CosmeticType.BodyBottomOverlay:
            case SemiFunc.CosmeticType.BodyTopOverlay:
                return CropBody;

            case SemiFunc.CosmeticType.ArmRight:
            case SemiFunc.CosmeticType.ArmRightMesh:
            case SemiFunc.CosmeticType.ArmRightOverlay:
            case SemiFunc.CosmeticType.GrabberMesh:
                return CropArmR;

            case SemiFunc.CosmeticType.ArmLeft:
            case SemiFunc.CosmeticType.ArmLeftMesh:
            case SemiFunc.CosmeticType.ArmLeftOverlay:
                return CropArmL;

            case SemiFunc.CosmeticType.LegRight:
            case SemiFunc.CosmeticType.LegRightMesh:
            case SemiFunc.CosmeticType.LegRightOverlay:
            case SemiFunc.CosmeticType.FootRight:
                return CropLegR;

            case SemiFunc.CosmeticType.LegLeft:
            case SemiFunc.CosmeticType.LegLeftMesh:
            case SemiFunc.CosmeticType.LegLeftOverlay:
            case SemiFunc.CosmeticType.FootLeft:
                return CropLegL;

            default:
                return CropFull;
        }
    }

    // Clears the in-memory cache + destroys loaded sprites so icons regenerate. Call after a bulk file deletion (IconCacheCleaner) to keep memory and disk consistent.
    internal static void InvalidateAll()
    {
        _knownCached = null; // force re-seed from disk on next HasCache call

        var meta = MetaManager.instance;
        if (meta == null) return;

        foreach (var id in HhhCosmeticLoader.RegisteredAssetIds)
        {
            var asset = meta.cosmeticAssets.Find(a => a != null && a.assetId == id);
            if (asset == null) continue;
            if (asset.icon != null)
            {
                UnityEngine.Object.Destroy(asset.icon);
                asset.icon = null;
            }
            CosmeticHoverPatch.Invalidate(asset);
        }

        var menuPage = UnityEngine.Object.FindObjectOfType<MenuPageCosmetics>();
        if (menuPage == null) return;
        foreach (var btn in menuPage.GetComponentsInChildren<MenuElementCosmeticButton>(true))
        {
            if (btn?.cosmeticAsset != null && BridgeIds.IsBridgeAsset(btn.cosmeticAsset))
                btn.UpdateIcon(false);
        }
    }

    // Trims src to its non-transparent content, pads to a square with a small margin, scales to OutSize and writes the PNG to cachePath; returns the saved icon's sprite (null on failure). Used by the Mini-Semibot photo: the preset icon-maker RT is a tall portrait (208×416, robot filling ~1/3) that would otherwise letterbox inside the square cosmetic buttons.
    internal static Sprite? SaveSquareContent(Texture2D? src, string cachePath, float marginFrac = 0.10f)
    {
        if (src == null) return null;
        Texture2D? square = null;
        Texture2D? scaled = null;
        try
        {
            var px = src.GetPixels32();
            int w = src.width, h = src.height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a <= 10) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) { minX = 0; minY = 0; maxX = w - 1; maxY = h - 1; } // fully transparent — keep everything

            int contentW = maxX - minX + 1;
            int contentH = maxY - minY + 1;
            int side = Mathf.CeilToInt(Mathf.Max(contentW, contentH) * (1f + 2f * marginFrac));

            var sq = new Color32[side * side]; // zeroed = fully transparent
            int dstX = (side - contentW) / 2;
            int dstY = (side - contentH) / 2;
            for (int y = 0; y < contentH; y++)
                System.Array.Copy(px, (minY + y) * w + minX, sq, (dstY + y) * side + dstX, contentW);

            square = new Texture2D(side, side, TextureFormat.RGBA32, false);
            square.SetPixels32(sq);
            square.Apply();

            scaled = ResizeBilinear(square, OutSize, OutSize);

            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(cachePath, scaled.EncodeToPNG());
            MarkCached(cachePath);

            return SemiFunc.LoadSpriteFromFile(cachePath);
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"SaveSquareContent failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (square != null) UnityEngine.Object.Destroy(square);
            if (scaled != null) UnityEngine.Object.Destroy(scaled);
        }
    }

    internal static void RefreshVisibleButtons(CosmeticAsset asset)
    {
        try
        {
            // Prefer searching under the open menu page (cheap singular find + children) over sweeping every object in the scene.
            var menuPage = UnityEngine.Object.FindObjectOfType<MenuPageCosmetics>();
            MenuElementCosmeticButton[] buttons = menuPage != null
                ? menuPage.GetComponentsInChildren<MenuElementCosmeticButton>(true)
                : UnityEngine.Object.FindObjectsOfType<MenuElementCosmeticButton>();

            foreach (var btn in buttons)
            {
                if (btn != null && btn.cosmeticAsset == asset)
                    btn.UpdateIcon(false);
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"Button refresh failed: {ex.Message}");
        }
    }

    private static Texture2D ResizeBilinear(Texture2D src, int w, int h)
    {
        var tmp = RenderTexture.GetTemporary(w, h);
        try
        {
            Graphics.Blit(src, tmp);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            return dst;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(tmp);
        }
    }
}
