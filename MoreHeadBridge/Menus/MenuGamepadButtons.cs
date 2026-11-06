// Which gamepad button runs each cosmetics-menu action, from the config. Buttons are positions, like the game's own bindings; their labels come from InputGlyphs per controller family.

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MoreHeadBridge;

internal static class MenuGamepadButtons
{
    // Menu keys can be rebound in the game's settings; a shortcut on the same button yields to them.
    private static readonly InputKey[] MenuKeys = { InputKey.UIClick, InputKey.Back, InputKey.Menu, InputKey.UITabLeft, InputKey.UITabRight };

    private static bool _warnedConflict;

    // Two actions on the same button revert all three to the defaults, with a one-time warning. Off may repeat.
    internal static (MenuGamepadButton fav, MenuGamepadButton hide, MenuGamepadButton cust) Effective()
    {
        var f = Plugin.FavoriteGamepadButton.Value;
        var h = Plugin.HideGamepadButton.Value;
        var c = Plugin.CustomizerGamepadButton.Value;

        bool conflict = (f != MenuGamepadButton.Off && (f == h || f == c))
                     || (h != MenuGamepadButton.Off && h == c);
        if (!conflict)
        {
            _warnedConflict = false;   // config fixed: a later conflict may warn again
            return (f, h, c);
        }

        if (!_warnedConflict)
        {
            _warnedConflict = true;
            BridgeLog.UserWarning("Two menu actions share the same gamepad button in the config — using the defaults (LeftButton = Favorite, RightTrigger = Hide, TopButton = Customize).");
        }
        return (MenuGamepadButton.LeftButton, MenuGamepadButton.RightTrigger, MenuGamepadButton.TopButton);
    }

    internal static MenuGamepadButton For(CosmeticClickAction action)
    {
        var (fav, hide, cust) = Effective();
        return action switch
        {
            CosmeticClickAction.Favorite => fav,
            CosmeticClickAction.Hide     => hide,
            _                            => cust,
        };
    }

    internal static string? Path(MenuGamepadButton button) => button switch
    {
        MenuGamepadButton.TopButton       => "<Gamepad>/buttonNorth",
        MenuGamepadButton.LeftButton      => "<Gamepad>/buttonWest",
        MenuGamepadButton.LeftTrigger     => "<Gamepad>/leftTrigger",
        MenuGamepadButton.RightTrigger    => "<Gamepad>/rightTrigger",
        MenuGamepadButton.LeftStickClick  => "<Gamepad>/leftStickPress",
        MenuGamepadButton.RightStickClick => "<Gamepad>/rightStickPress",
        _                                 => null,
    };

    internal static bool Pressed(Gamepad pad, MenuGamepadButton button)
    {
        ButtonControl? control = button switch
        {
            MenuGamepadButton.TopButton       => pad.buttonNorth,
            MenuGamepadButton.LeftButton      => pad.buttonWest,
            MenuGamepadButton.LeftTrigger     => pad.leftTrigger,
            MenuGamepadButton.RightTrigger    => pad.rightTrigger,
            MenuGamepadButton.LeftStickClick  => pad.leftStickButton,
            MenuGamepadButton.RightStickClick => pad.rightStickButton,
            _                                 => null,
        };
        return control != null && control.wasPressedThisFrame && IsFree(button);
    }

    // False for Off, or when a rebound menu key already uses this button.
    internal static bool IsFree(MenuGamepadButton button)
    {
        string? path = Path(button);
        if (path == null || InputManager.instance == null) return false;

        foreach (var key in MenuKeys)
        {
            var action = InputManager.instance.GetAction(key);
            if (action == null) continue;
            foreach (var binding in action.bindings)
                if (string.Equals(binding.effectivePath, path, System.StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
