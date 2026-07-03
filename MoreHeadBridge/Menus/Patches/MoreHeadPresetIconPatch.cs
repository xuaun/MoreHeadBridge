// Keeps MoreHead-menu decorations out of a preset's saved preview image. GetIcon spawns a throwaway
// avatar that MoreHead also decorates; we hide its decorations before the icon is captured a frame
// later (GetIconCoroutine). Local only — preset icons are a per-machine PNG cache.

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticPreset), "GetIcon")]
internal static class MoreHeadPresetIconPatch
{
    private static readonly LazyFieldRef<MenuElementCosmeticPreset, GameObject> _spawnedAvatar =
        new("spawnedAvatar", "preset-icon decoration hiding");

    [HarmonyPostfix]
    private static void Postfix(MenuElementCosmeticPreset __instance)
    {
        if (!Plugin.ExcludeMoreHeadFromPresetIcons.Value) return;
        if (!_spawnedAvatar.TryGet(__instance, out var avatar)) return;
        if (avatar == null) return;   // cached-icon path: nothing spawned

        // everyFrame: capture is next frame, too soon for the throttled cadence.
        if (avatar.GetComponent<MoreHeadDecorationHider>() == null)
            avatar.AddComponent<MoreHeadDecorationHider>().Init(avatar.transform, everyFrame: true);
    }
}
