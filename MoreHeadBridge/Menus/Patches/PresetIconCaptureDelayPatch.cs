// Takes a preset's icon one frame later, after its icon avatar has run Start and a first Update. Icons taken in the frame the avatar was
// dressed, as when the cosmetics menu opens, showed the vanilla cosmetics but none of the bridge's.

using HarmonyLib;
using System.Collections;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticPreset), "GetIconCoroutine")]
internal static class PresetIconCaptureDelayPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerator __result) => __result = AfterOneFrame(__result);

    // The game's coroutine starts the capture on its first step, so delaying that step delays the whole capture.
    private static IEnumerator AfterOneFrame(IEnumerator capture)
    {
        yield return null;
        while (capture.MoveNext())
            yield return capture.Current;
    }
}
