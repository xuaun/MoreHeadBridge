// ============================================================================
// Soft-dependency detection by BepInEx GUID — no assembly references. CustomGrabColor gates the beam-colour option (colour itself comes from MetaManager); Mimic gates the "Mimic Clips" mouth mode (reads its recorded .wavs).
// ============================================================================

using BepInEx.Bootstrap;

namespace MoreHeadBridge;

internal static class MiniSemibotModCompat
{
    private const string CustomGrabColorGuid = "games.enchanted.CustomGrabColour";
    private const string MimicGuid = "Mimics";

    private static bool? _hasCgc;
    private static bool? _hasMimic;

    // True when CustomGrabColor is installed. Gates the Mini-Semibot beam-colour popup option.
    internal static bool HasCustomGrabColor
        => _hasCgc ??= Chainloader.PluginInfos.ContainsKey(CustomGrabColorGuid);

    // True when the Mimic mod is installed. Gates the "Mimic Clips" mouth mode.
    internal static bool HasMimic
        => _hasMimic ??= Chainloader.PluginInfos.ContainsKey(MimicGuid);
}
