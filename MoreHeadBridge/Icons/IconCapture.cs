// ============================================================================
// [MenuCapture] — captures the existing cosmetics-menu avatar render texture
// and saves it as a PNG icon for a given CosmeticAsset.
//
// Used by:
//   - CosmeticHoverPatch.cs  (capture when the user hovers a button)
//   - BatchIconGenerator.cs  (one-shot batch cycling all cosmetics)
// ============================================================================

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static class IconCapture
{
    private const int OutSize = 128;

    // Private cache directory. REPOLib's MetaManagerPatch.AwakePatch wipes any PNG in
    // %persistentDataPath%\Cache\Icons\Cosmetics\ that doesn't match a vanilla cosmetic
    // name — including ours — at every launch. By storing icons OUTSIDE that path we
    // keep them around. Our GetIconPatch loads from here directly, so we never need
    // vanilla's cache to know about them.
    internal static string CacheDir =>
        Path.Combine(Application.persistentDataPath, "MoreHeadBridge_Icons");

    internal static string CachePathFor(CosmeticAsset asset)
    {
        string name = asset.name.Replace("(Clone)", "").ToLowerInvariant();
        return Path.Combine(CacheDir, name + ".png");
    }

    internal static bool HasCache(CosmeticAsset asset) => File.Exists(CachePathFor(asset));
    
    private static RenderTexture? FindActiveAvatarRT()
    {
        var avatar = UnityEngine.Object.FindObjectOfType<PlayerAvatarMenuHover>();
        if (avatar == null) return null;
        
        if (avatar.renderTextureInstance != null) return avatar.renderTextureInstance;
        var rawImage = avatar.GetComponent<RawImage>();
        return rawImage != null ? rawImage.texture as RenderTexture : null;
    }

    // Reads the current avatar render texture and saves a PNG for this asset.
    // Returns true on success. Skips if a cached file already exists.
    internal static bool TryCapture(CosmeticAsset asset)
        => TryCapture(asset, asset?.type ?? SemiFunc.CosmeticType.Hat);

    // type lets us crop the avatar shot to just the relevant body part.
    internal static bool TryCapture(CosmeticAsset asset, SemiFunc.CosmeticType type)
    {
        if (asset == null) return false;
        if (HasCache(asset)) return false;

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

            // Crop to the body region for this cosmetic type, then resize to OutSize.
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

            File.WriteAllBytes(CachePathFor(asset), scaled.EncodeToPNG());

            // Clear the in-memory icon cache for this asset so the next GetIcon() call
            // reloads from the freshly-written PNG. Then nudge any visible buttons that
            // are showing the placeholder so they pick up the new icon immediately.
            asset.icon = null;
            RefreshVisibleButtons(asset);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[MoreHeadBridge] Icon capture failed for '{asset.name}': {ex.Message}");
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
    
    private static Rect GetCropRect(SemiFunc.CosmeticType type)
    {
        switch (type)
        {
            // Head region (hat, face, eyewear, ears) — top portion of the avatar.
            case SemiFunc.CosmeticType.Hat:
            case SemiFunc.CosmeticType.HeadTopMesh:
            case SemiFunc.CosmeticType.HeadBottom:
            case SemiFunc.CosmeticType.HeadBottomMesh:
            case SemiFunc.CosmeticType.HeadTopOverlay:
            case SemiFunc.CosmeticType.HeadBottomOverlay:
            case SemiFunc.CosmeticType.FaceTop:
            case SemiFunc.CosmeticType.FaceBottom:
            case SemiFunc.CosmeticType.Eyewear:
            case SemiFunc.CosmeticType.Ears:
            case SemiFunc.CosmeticType.EyeLidRightMesh:
            case SemiFunc.CosmeticType.EyeLidLeftMesh:
                return new Rect(0.22f, 0.62f, 0.56f, 0.35f);

            // Torso — middle band.
            case SemiFunc.CosmeticType.BodyTop:
            case SemiFunc.CosmeticType.BodyTopMesh:
            case SemiFunc.CosmeticType.BodyBottom:
            case SemiFunc.CosmeticType.BodyBottomMesh:
            case SemiFunc.CosmeticType.BodyBottomOverlay:
            case SemiFunc.CosmeticType.BodyTopOverlay:
                return new Rect(0.18f, 0.34f, 0.64f, 0.36f);

            // Right arm (character's right) → LEFT half of frame.
            case SemiFunc.CosmeticType.ArmRight:
            case SemiFunc.CosmeticType.ArmRightMesh:
            case SemiFunc.CosmeticType.ArmRightOverlay:
            case SemiFunc.CosmeticType.GrabberMesh:
                return new Rect(0.05f, 0.30f, 0.50f, 0.40f);

            // Left arm (character's left) → RIGHT half of frame.
            case SemiFunc.CosmeticType.ArmLeft:
            case SemiFunc.CosmeticType.ArmLeftMesh:
            case SemiFunc.CosmeticType.ArmLeftOverlay:
                return new Rect(0.45f, 0.30f, 0.50f, 0.40f);

            // Right leg / foot → LEFT half of bottom region.
            case SemiFunc.CosmeticType.LegRight:
            case SemiFunc.CosmeticType.LegRightMesh:
            case SemiFunc.CosmeticType.LegRightOverlay:
            case SemiFunc.CosmeticType.FootRight:
                return new Rect(0.10f, 0.00f, 0.45f, 0.45f);

            // Left leg / foot → RIGHT half of bottom region.
            case SemiFunc.CosmeticType.LegLeft:
            case SemiFunc.CosmeticType.LegLeftMesh:
            case SemiFunc.CosmeticType.LegLeftOverlay:
            case SemiFunc.CosmeticType.FootLeft:
                return new Rect(0.45f, 0.00f, 0.45f, 0.45f);

            default:
                return new Rect(0f, 0f, 1f, 1f);
        }
    }

    private static void RefreshVisibleButtons(CosmeticAsset asset)
    {
        try
        {
            var buttons = UnityEngine.Object.FindObjectsOfType<MenuElementCosmeticButton>();
            foreach (var btn in buttons)
            {
                if (btn != null && btn.cosmeticAsset == asset)
                    btn.UpdateIcon(false);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[MoreHeadBridge] Button refresh failed: {ex.Message}");
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
