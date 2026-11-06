// Hint texts for the cosmetics-menu actions, rendered like the game's own prompts: mouse and gamepad as icons, keyboard keys as underlined bold text.

using HarmonyLib;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace MoreHeadBridge;

internal static class MenuActionPrompts
{
    private const string MouseLeftPath = "<Mouse>/leftButton";

    // TagForPath is private; if a game update renames it, the prompts fall back to plain text.
    private static readonly System.Func<string, InputDeviceType, InputKey, string>? TagForPath = FindTagForPath();

    private static string _hint = "";
    private static (InputDeviceType device,
                    (MenuClickModifier, MenuClickModifier, MenuClickModifier) keys,
                    (MenuGamepadButton, MenuGamepadButton, MenuGamepadButton) pads,
                    bool customizer)? _hintKey;

    private static bool OnGamepad => InputGlyphs.CurrentDevice != InputDeviceType.KeyboardMouse;

    // The idle hint for the current device, rebuilt only when something it shows changes. Empty if no gamepad shortcut is available.
    internal static string Hint()
    {
        var key = (InputGlyphs.CurrentDevice, MenuClickModifiers.Effective(), MenuGamepadButtons.Effective(), Plugin.EnableCosmeticCustomizer.Value);
        if (_hintKey == key) return _hint;
        _hintKey = key;

        var lines = new List<string>(3);
        AddLine(lines, CosmeticClickAction.Favorite, "Fav");
        AddLine(lines, CosmeticClickAction.Hide, "Hide");
        if (Plugin.EnableCosmeticCustomizer.Value)
            AddLine(lines, CosmeticClickAction.Customize, "Edit");
        return _hint = string.Join("\n", lines);
    }

    // Game keybinds are edited on another page: re-check them the next time the menu opens.
    internal static void Invalidate() => _hintKey = null;

    // The message of an empty FAV or HIDE tab, naming the current device's input (the keyboard one when the gamepad shortcut is off).
    internal static string EmptyMessage(CosmeticClickAction action)
    {
        string prompt = (OnGamepad ? GamepadPrompt(action) : null) ?? KeyboardPrompt(action);
        return action == CosmeticClickAction.Favorite
            ? $"Add a favorite with {prompt} :)"
            : $"Hide cosmetics with {prompt} :P";
    }

    private static void AddLine(List<string> lines, CosmeticClickAction action, string label)
    {
        string? prompt = OnGamepad ? GamepadPrompt(action) : KeyboardPrompt(action);
        if (prompt != null) lines.Add($"{prompt} {label}");
    }

    private static string KeyboardPrompt(CosmeticClickAction action)
    {
        string key = MenuClickModifiers.For(action).ToString().ToUpperInvariant();
        return $"<u><b>{key}</b></u> + {Tag(MouseLeftPath, InputDeviceType.KeyboardMouse)}";
    }

    private static string? GamepadPrompt(CosmeticClickAction action)
    {
        var button = MenuGamepadButtons.For(action);
        return MenuGamepadButtons.IsFree(button) ? Tag(MenuGamepadButtons.Path(button)!, InputGlyphs.CurrentDevice) : null;
    }

    // Same rule as InputGlyphs.ReplaceTags: icons stay as they are, text gets underlined and bold.
    private static string Tag(string path, InputDeviceType device)
    {
        string tag = TagForPath != null
            ? TagForPath(path, device, InputKey.UIClick)
            : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant();
        return tag.Contains("<sprite") ? tag : $"<u><b>{tag}</b></u>";
    }

    private static System.Func<string, InputDeviceType, InputKey, string>? FindTagForPath()
    {
        try
        {
            var method = AccessTools.Method(typeof(InputGlyphs), "TagForPath");
            return method == null ? null : AccessTools.MethodDelegate<System.Func<string, InputDeviceType, InputKey, string>>(method);
        }
        catch (System.Exception ex)
        {
            BridgeLog.Trace($"MenuActionPrompts: TagForPath unavailable — {ex.Message}");
            return null;
        }
    }
}
