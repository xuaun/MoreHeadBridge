using UnityEngine;

namespace MoreHeadBridge;

// Fills asset.icon for cosmetics the game can't render (no SemiIconMaker): private PNG cache, then the prefab's texture, then the placeholder.
internal static class BridgeIconResolver
{
    internal static Sprite? Ensure(CosmeticAsset? asset)
    {
        if (asset == null) return null;
        if (asset.icon != null) return asset.icon;

        var prefab = asset.prefab?.Prefab;
        if (prefab != null && prefab.GetComponentInChildren<SemiIconMaker>(true) != null)
            return null;

        if (IconCapture.HasCache(asset))
            return asset.icon = SemiFunc.LoadSpriteFromFile(IconCapture.CachePathFor(asset));

        if (!BridgeIds.IsBridgeAsset(asset)) return null;

        if (Plugin.UseTextureAsPlaceholder.Value &&
            HhhCosmeticLoader.BridgeIconTextures.TryGetValue(asset.assetId, out var tex) && tex != null)
        {
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f);
            sprite.name = $"BridgeIcon_{asset.name}";
            return asset.icon = sprite;
        }

        return asset.icon = PlaceholderIcon.Get();
    }
}
