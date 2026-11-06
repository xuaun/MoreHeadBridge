// Which held key triggers each cosmetics-menu click action, from the config. Two actions on the same key revert all three to the defaults, with a one-time warning.

using UnityEngine;

namespace MoreHeadBridge;

internal static class MenuClickModifiers
{
    private static bool _warnedConflict;

    internal static (MenuClickModifier fav, MenuClickModifier hide, MenuClickModifier cust) Effective()
    {
        var f = Plugin.FavoriteClickModifier.Value;
        var h = Plugin.HideClickModifier.Value;
        var c = Plugin.CustomizerClickModifier.Value;

        if (f != h && f != c && h != c)
        {
            _warnedConflict = false;   // config fixed: a later conflict may warn again
            return (f, h, c);
        }

        if (!_warnedConflict)
        {
            _warnedConflict = true;
            BridgeLog.UserWarning("Two menu actions share the same click modifier in the config — using the defaults (Ctrl = Favorite, Alt = Hide, Shift = Customize).");
        }
        return (MenuClickModifier.Ctrl, MenuClickModifier.Alt, MenuClickModifier.Shift);
    }

    internal static MenuClickModifier For(CosmeticClickAction action)
    {
        var (fav, hide, cust) = Effective();
        return action switch
        {
            CosmeticClickAction.Favorite => fav,
            CosmeticClickAction.Hide     => hide,
            _                            => cust,
        };
    }

    internal static bool Held(MenuClickModifier modifier) => modifier switch
    {
        MenuClickModifier.Ctrl  => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
        MenuClickModifier.Alt   => Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt),
        MenuClickModifier.Shift => Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift),
        _                       => false,
    };
}
