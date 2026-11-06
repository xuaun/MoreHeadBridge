using UnityEngine;

namespace MoreHeadBridge;

// Fills asset.icon from the saved renders of cosmetic mods and, for cosmetics the game can't render (no SemiIconMaker), from the private PNG cache, then the prefab's texture, then the placeholder.
internal static class BridgeIconResolver
{
    internal static Sprite? Ensure(CosmeticAsset? asset)
    {
        if (asset == null) return null;
        if (asset.icon != null) return asset.icon;

        if (!BridgeIds.IsBridgeAsset(asset))
        {
            if (BridgeIds.IsModdedCosmetic(asset) && RenderedIconCache.Has(asset))
            {
                var saved = RenderedIconCache.Load(asset);
                if (saved != null) return asset.icon = saved;
            }

            // No PNG of ours: the game's own loading takes over. Checked first because the first read of Prefab loads it from disk, which every button would pay.
            if (!IconCapture.HasCache(asset)) return null;

            var prefab = asset.prefab?.Prefab;
            if (prefab != null && prefab.GetComponentInChildren<SemiIconMaker>(true) != null)
                return null;
        }

        if (IconCapture.HasCache(asset))
        {
            var cached = SemiFunc.LoadSpriteFromFile(IconCapture.CachePathFor(asset));
            if (cached != null) return asset.icon = cached;
            IconCapture.Forget(asset);
        }

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
