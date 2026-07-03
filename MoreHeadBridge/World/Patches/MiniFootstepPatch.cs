// Mini footsteps: driven by the MINI's OWN walk/run animation events (FootstepLight/Medium/Heavy on its PlayerAvatarVisuals), NOT the wearer's — so timing is independent (the mini has its own LegSpeed) and audible instead of masked by your co-located step. The vanilla body never plays a sound for it (playerAvatar null → guarded call no-ops), so we add a light, surface-aware step at the mini's feet. Toggle: Mini popup "Footstep Sounds" (local viewer preference).

using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

internal static class MiniFootstepEmitter
{
    internal static void Emit(PlayerAvatarVisuals visuals, Materials.SoundType soundType)
    {
        if (visuals == null) return;
        if (!MiniSemibotVisualPrefs.FootstepSounds) return;
        if (Materials.Instance == null || RecordingDirector.instance != null) return;

        // Only OUR mini's body (not the real player, not menu/preview minis).
        var follow = visuals.GetComponentInParent<MiniSemibotFollow>();
        if (follow == null || !ReferenceEquals(follow.MiniVisuals, visuals)) return;
        if (follow.BodyHidden) return;
        if (MiniSemibotSpawner.IsMenuOrPreviewWearer(follow.WearerVisuals)) return;
        if (follow.WearerAvatar == null) return;

        // Always Light (mini is small); surface-aware via the wearer's MaterialTrigger (the mini visuals exposes none); no particles, no enemy investigate (purely cosmetic), 0.5× volume like a remote player. GetMaterial re-raycasts from the mini's feet each call.
        Materials.Instance.Impulse(visuals.transform.position, Vector3.down,
            Materials.SoundType.Light, footstep: true, footstepParticles: false,
            follow.WearerAvatar.MaterialTrigger, Materials.HostType.OtherPlayer);
    }
}

[HarmonyPatch(typeof(PlayerAvatarVisuals), nameof(PlayerAvatarVisuals.FootstepLight))]
internal static class MiniFootstepLightPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerAvatarVisuals __instance)
        => MiniFootstepEmitter.Emit(__instance, Materials.SoundType.Light);
}

[HarmonyPatch(typeof(PlayerAvatarVisuals), nameof(PlayerAvatarVisuals.FootstepMedium))]
internal static class MiniFootstepMediumPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerAvatarVisuals __instance)
        => MiniFootstepEmitter.Emit(__instance, Materials.SoundType.Medium);
}

[HarmonyPatch(typeof(PlayerAvatarVisuals), nameof(PlayerAvatarVisuals.FootstepHeavy))]
internal static class MiniFootstepHeavyPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerAvatarVisuals __instance)
        => MiniFootstepEmitter.Emit(__instance, Materials.SoundType.Heavy);
}
