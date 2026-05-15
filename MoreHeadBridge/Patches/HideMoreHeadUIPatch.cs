namespace MoreHeadBridge;

// Prefix that suppresses MoreHeadUI.Initialize() when HideMoreHeadButton=true.
internal static class HideMoreHeadUIPatch
{
    internal static bool SkipInitialize() => false;
}
