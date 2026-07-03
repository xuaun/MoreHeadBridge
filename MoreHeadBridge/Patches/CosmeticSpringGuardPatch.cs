using HarmonyLib;

namespace MoreHeadBridge;

// Prevents the JumpImpulse NRE for springs whose transforms didn't resolve — common with .hhh cosmetics referencing bones absent from the REPO rig.
//
// Vanilla's own guard only runs under Debug.isDebugBuild — replicate it for release: after Awake, disable any spring with a missing target/transform (JumpImpulse early-returns on `disabled`).
[HarmonyPatch(typeof(CosmeticSprings), "Awake")]
internal static class CosmeticSpringGuardPatch
{
    [HarmonyPostfix]
    private static void Postfix(CosmeticSprings __instance)
    {
        if (__instance.springs == null) return;

        foreach (var spring in __instance.springs)
        {
            if (spring == null) continue;
            var sys = spring.springSystem;
            if (sys == null || sys.target == null || sys.transform == null)
                spring.disabled = true;
        }
    }
}

// Skips JumpImpulse when the spring's target went null after Awake — it derefs springSystem.target (NRE), and via
// the death tumble's TumbleSetRPC the throw escapes Photon as a TargetInvocationException on other clients.
[HarmonyPatch(typeof(CosmeticSprings.CosmeticSpring), "JumpImpulse")]
internal static class CosmeticSpringJumpImpulseGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CosmeticSprings.CosmeticSpring __instance)
        => __instance?.springSystem?.target != null;
}
