using HarmonyLib;
using System.Threading.Tasks;
using UnityEngine;

namespace MoreHeadBridge;

// Resolves the icon before the button's "icon == null → load it" checks and before every redraw, so an icon cleared by IconCapture gets rebuilt.
[HarmonyPatch(typeof(MenuElementCosmeticButton))]
internal static class BridgeIconPrefillPatch
{
    [HarmonyPatch("Start"), HarmonyPrefix]
    private static void StartPrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);

    [HarmonyPatch("OnEnable"), HarmonyPrefix]
    private static void OnEnablePrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);

    [HarmonyPatch("UpdateIcon"), HarmonyPrefix]
    private static void UpdateIconPrefix(MenuElementCosmeticButton __instance)
        => BridgeIconResolver.Ensure(__instance.cosmeticAsset);
}

// Renders at most one icon per frame, what's on screen first: all the buttons of a section ask at once, and each icon is a synchronous prefab spawn and camera render. Skips buttons scrolled away (OnEnable asks again) and cosmetics without a SemiIconMaker, which can't produce an icon.
[HarmonyPatch(typeof(CosmeticAsset), nameof(CosmeticAsset.GenerateIconAsync))]
internal static class IconRenderQueuePatch
{
    private static readonly Vector3[] _corners = new Vector3[4];

    private static bool _rendering;
    private static int _lastRenderFrame = -1;
    private static int _lastOnScreenFrame = -1;
    private static int _viewportFrame = -1;
    private static float _viewportBottom, _viewportTop, _halfButton;

    [HarmonyPrefix]
    private static bool Prefix(CosmeticAsset __instance, Object _owner, ref Task<Sprite> __result)
    {
        if (_rendering) return true;
        __result = RenderInTurn(__instance, _owner);
        return false;
    }

    private static async Task<Sprite> RenderInTurn(CosmeticAsset asset, Object owner)
    {
        double onScreenSince = -1;   // TEMP-TEST

        // Checked while waiting: a button scrolled away leaves the queue without using up the frame's turn.
        while (true)
        {
            if (!owner || owner is Behaviour { isActiveAndEnabled: false } || asset.icon != null) return null!;

            int frame = Time.frameCount;
            bool onScreen = IsOnScreen(owner);
            if (onScreen) _lastOnScreenFrame = frame;
            if (onScreen && onScreenSince < 0) onScreenSince = Time.realtimeSinceStartupAsDouble;   // TEMP-TEST

            // Off-screen buttons wait while anything on screen asked in the previous frame.
            if (_lastRenderFrame != frame && (onScreen || frame - _lastOnScreenFrame > 1)) break;
            await Task.Yield();
        }
        _lastRenderFrame = Time.frameCount;

        var prefab = asset.prefab?.Prefab;
        if (prefab == null || prefab.GetComponentInChildren<SemiIconMaker>(true) == null) return null!;

        Task<Sprite> render;
        _rendering = true;
        try { render = asset.GenerateIconAsync(owner); }
        finally { _rendering = false; }

        var icon = await render;
        if (onScreenSince >= 0) PerfProbe.NoteIconWait(Time.realtimeSinceStartupAsDouble - onScreenSince);   // TEMP-TEST
        return icon;
    }

    // Anything other than a cosmetic button in the scroll list counts as on screen.
    private static bool IsOnScreen(Object owner)
    {
        if (owner is not MenuElementCosmeticButton button || button.cosmeticSection == null) return true;

        var viewport = button.cosmeticSection.menuPageCosmetics?.stickyHeader?.viewport;
        if (viewport == null) return true;

        if (_viewportFrame != Time.frameCount)
        {
            _viewportFrame = Time.frameCount;
            viewport.GetWorldCorners(_corners);
            _viewportBottom = _corners[0].y;
            _viewportTop = _corners[1].y;

            var rect = (RectTransform)button.transform;
            _halfButton = rect.rect.height * rect.lossyScale.y * 0.5f;
        }

        float y = button.transform.position.y;
        return y > _viewportBottom - _halfButton && y < _viewportTop + _halfButton;
    }
}

// Cosmetic mods go through RenderedIconCache: a smaller icon, saved for later sessions. Items, presets and vanilla cosmetics keep the game's path.
[HarmonyPatch(typeof(SemiIconMaker), nameof(SemiIconMaker.CreateIconAsync))]
internal static class RenderedIconPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SemiIconMaker __instance, System.Func<bool> _isCurrent, ref Task<Sprite> __result)
    {
        var cosmetic = __instance.GetComponentInParent<Cosmetic>(true);
        if (cosmetic == null || !BridgeIds.IsModdedCosmetic(cosmetic.cosmeticAsset)) return true;

        __result = RenderedIconCache.RenderAsync(__instance, cosmetic.cosmeticAsset, _isCurrent);
        return false;
    }
}

// Keeps the saved-size icons of cosmetic mods for the whole session: vanilla destroys them with the button, which re-rendered the whole tab on every visit.
[HarmonyPatch(typeof(MenuElementCosmeticButton), "OnDestroy")]
internal static class KeepRenderedIconPatch
{
    private static readonly LazyFieldRef<MenuElementCosmeticButton, Sprite> _generatedIcon = new("generatedIcon", "icon reuse");

    [HarmonyPrefix]
    private static void Prefix(MenuElementCosmeticButton __instance)
    {
        var asset = __instance.cosmeticAsset;
        if (asset == null || !BridgeIds.IsModdedCosmetic(asset)) return;
        if (!_generatedIcon.TryGet(__instance, out var icon) || icon == null || asset.icon != icon) return;

        if (icon.texture != null && icon.texture.width <= RenderedIconCache.IconSize)
            _generatedIcon.TrySet(__instance, null!);
    }
}

// The game spawns every missing icon at the same spot, and each one stays there while its GPU readback is pending — the next icon camera would film them too. Moves a cosmetic out of the way once it has been rendered.
[HarmonyPatch(typeof(SemiIconMaker), nameof(SemiIconMaker.CreateIconReadback))]
internal static class IconSpawnOverlapPatch
{
    private static readonly Vector3 RenderedSpawnPosition = new(-1000f, -6000f, -1000f);

    [HarmonyPostfix]
    private static void Postfix(SemiIconMaker __instance)
    {
        var cosmetic = __instance.GetComponentInParent<Cosmetic>(true);
        if (cosmetic != null && cosmetic.iconCreation)
            cosmetic.transform.position = RenderedSpawnPosition;
    }
}
