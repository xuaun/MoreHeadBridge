// Preset-style photo for the Mini-Semibot icon: spawns the icon-maker avatar prefab the Presets tab uses (own SemiIconMaker camera/RT, never the live menu avatar), dresses it BARE, renders to the icon cache PNG.
//   Initial auto-capture (menu open, nothing cached) resets colours; Recapture Icon (mini popup) retakes with the player's current body colours.

using System.Collections;
using UnityEngine;

namespace MoreHeadBridge;

internal static class MiniSemibotIconCapture
{
    // Set once we've tried this session, so a failed capture (prefab missing, etc.) doesn't retry every menu open. Reset by ForceRecapture.
    private static bool _attempted;

    internal static void TryStart(MonoBehaviour host)
    {
        if (host == null) return;
        if (!Plugin.EnableMiniSemibot.Value) return;

        var asset = MiniSemibotCosmetic.Asset;
        if (asset == null) return;

        // Already have a photo on disk → make sure it's the live icon and stop.
        if (IconCapture.HasCache(asset))
        {
            var sprite = SemiFunc.LoadSpriteFromFile(IconCapture.CachePathFor(asset));
            if (sprite != null) asset.icon = sprite;
            return;
        }

        if (_attempted) return;
        _attempted = true;
        host.StartCoroutine(Run(asset, useWearerColors: false));
    }

    // Deletes the cached photo and retakes it with the player's current body colours (Recapture Icon button).
    internal static void ForceRecapture(MonoBehaviour host)
    {
        var asset = MiniSemibotCosmetic.Asset;
        if (asset == null || host == null) return;

        IconCapture.DeleteCache(asset);   // removes PNG + clears asset.icon
        asset.icon = MiniSemibotIcon.Create(); // placeholder until the new photo lands
        _attempted = true;                // we run the coroutine ourselves
        host.StartCoroutine(Run(asset, useWearerColors: true));
    }

    private static IEnumerator Run(CosmeticAsset asset, bool useWearerColors)
    {
        var meta = MetaManager.instance;
        if (meta == null) yield break;

        var iconAvatarPrefab = FindIconAvatarPrefab();
        if (iconAvatarPrefab == null)
        {
            BceConsole.LogWarning("Mini-Semibot: preset icon-maker prefab not found — keeping the drawn icon");
            yield break;
        }

        // Same far-away spot vanilla moves the avatar to during an icon render.
        var spawned = Object.Instantiate(
            iconAvatarPrefab, new Vector3(-1000f, -1000f, -1000f), Quaternion.identity);
        var pc = spawned.GetComponentInChildren<PlayerCosmetics>();
        var iconMaker = spawned.GetComponentInChildren<PlayerAvatarMenu>()?.cameraAndStuff
            ?.GetComponentInChildren<SemiIconMaker>(includeInactive: true);
        if (pc == null || iconMaker == null)
        {
            Object.Destroy(spawned);
            BceConsole.LogWarning("Mini-Semibot: icon-maker avatar incomplete — keeping the drawn icon");
            yield break;
        }

        // Mirror the preset buttons: keep only the avatar's own SemiIconMaker active.
        foreach (var other in spawned.GetComponentsInChildren<SemiIconMaker>())
            if (other != iconMaker) other.gameObject.SetActive(false);
        // OnEnable creates the camera's renderTextureInstance we read below.
        if (!iconMaker.gameObject.activeSelf) iconMaker.gameObject.SetActive(true);

        // Bare outfit; all-zero palette indices = the "reset all" colours, or the player's own.
        int colorCount = meta.colorsEquipped?.Length ?? 0;
        int[] colors = useWearerColors && meta.colorsEquipped != null
            ? (int[])meta.colorsEquipped.Clone()
            : new int[colorCount];
        pc.SetupCosmeticsLogic(System.Array.Empty<int>(), _forced: false);
        pc.SetupColorsLogic(colors);

        yield return null;   // one frame for the rig/camera — same as the preset buttons

        // NOT vanilla CreateIconFromRenderTexture: it compresses + makes the texture unreadable, and the tall portrait letterboxes in the square buttons. Read pixels ourselves, trim to content, pad square.
        Texture2D? shot;
        try { shot = RenderIconTexture(iconMaker); }
        finally { Object.Destroy(spawned); }

        Sprite? sprite = null;
        if (shot != null)
        {
            sprite = IconCapture.SaveSquareContent(shot, IconCapture.CachePathFor(asset));
            Object.Destroy(shot);
        }

        if (sprite != null)
        {
            asset.icon = sprite;
            CosmeticsMenuState.ActivePage?.RefreshScrollContent();
            BceConsole.LogInfo("Mini-Semibot: captured preset-style icon", System.ConsoleColor.Cyan);
        }
        else
        {
            BceConsole.LogWarning("Mini-Semibot: icon capture failed — keeping the drawn icon");
        }
    }

    // Renders the icon camera once (fog off, the maker's ambient light, like vanilla CreateIconFromRenderTexture) and reads the RT back as a READABLE texture.
    private static Texture2D? RenderIconTexture(SemiIconMaker iconMaker)
    {
        var rt = HarmonyLib.AccessTools.Field(typeof(SemiIconMaker), "renderTextureInstance")
            ?.GetValue(iconMaker) as RenderTexture;
        if (rt == null || iconMaker.iconCamera == null) return null;

        bool fog = RenderSettings.fog;
        Color ambient = RenderSettings.ambientLight;
        try
        {
            RenderSettings.fog = false;
            RenderSettings.ambientLight = iconMaker.ambientLight;
            iconMaker.iconCamera.Render();
        }
        finally
        {
            RenderSettings.fog = fog;
            RenderSettings.ambientLight = ambient;
        }

        var prev = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false, false);
            return tex;
        }
        finally { RenderTexture.active = prev; }
    }

    // The icon-maker avatar prefab rides on the preset BUTTON prefab (serialized field) — readable straight off the prefab asset, no instantiation.
    private static GameObject? FindIconAvatarPrefab()
    {
        var page = CosmeticsMenuState.ActivePage ?? Object.FindObjectOfType<MenuPageCosmetics>(true);
        var presetButton = page != null ? page.presetButtonPrefab : null;
        var presetComp = presetButton != null ? presetButton.GetComponent<MenuElementCosmeticPreset>() : null;
        return presetComp != null ? presetComp.playerAvatarIconPrefab : null;
    }
}
