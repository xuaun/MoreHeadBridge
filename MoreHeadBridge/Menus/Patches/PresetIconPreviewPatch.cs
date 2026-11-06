// Builds a preset's saved icon from the preset itself. While the menu preview is on, the game dresses the icon avatar in the preview instead,
// and the icon batch keeps the preview on while it runs, so an icon made then showed whatever the batch was capturing.

using HarmonyLib;

namespace MoreHeadBridge;

[HarmonyPatch(typeof(MenuElementCosmeticPreset), "GetIcon")]
internal static class PresetIconPreviewPatch
{
    private static bool _previewWasOn;

    [HarmonyPrefix]
    private static void Prefix()
    {
        var meta = MetaManager.instance;
        _previewWasOn = meta != null && meta.cosmeticPreviewEnabled;
        if (_previewWasOn) meta!.cosmeticPreviewEnabled = false;
    }

    // Runs even if GetIcon throws, so the preview is never left off.
    [HarmonyFinalizer]
    private static void Finalizer()
    {
        if (!_previewWasOn) return;
        _previewWasOn = false;

        var meta = MetaManager.instance;
        if (meta != null) meta.cosmeticPreviewEnabled = true;
    }
}
