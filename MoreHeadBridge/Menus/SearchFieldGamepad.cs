// Lets a controller reach the SEARCH field, like the game's own text inputs (MenuTextInput): confirm on it opens Steam's gamepad keyboard, or focuses the field when Steam can't show one. Lives on a child because TMP_InputField is already the Selectable of its GameObject.

using Steamworks;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class SearchFieldGamepad : MonoBehaviour
{
    private const string KeyboardPlaceholder = "Type to Search...";

    private TMP_InputField _field = null!;
    private MenuElementHover _hover = null!;
    private bool _keyboardOpen;
    private InputDeviceType? _placeholderDevice;

    internal MenuSelectableElement Element { get; private set; } = null!;

    internal static void Attach(TMP_InputField field)
    {
        var go = new GameObject("MHB_SearchNav", typeof(RectTransform));
        go.transform.SetParent(field.transform, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var element = go.AddComponent<MenuSelectableElement>();
        var hover = go.AddComponent<MenuElementHover>();

        var self = go.AddComponent<SearchFieldGamepad>();
        self._field = field;
        self._hover = hover;
        self.Element = element;
    }

    private void OnEnable() => SteamUtils.OnGamepadTextInputDismissed += OnKeyboardDismissed;

    private void OnDisable()
    {
        SteamUtils.OnGamepadTextInputDismissed -= OnKeyboardDismissed;
        _keyboardOpen = false;
    }

    private void Update()
    {
        RefreshPlaceholder();

        if (SemiFunc.InputMode() != InputModes.Gamepad) return;

        if (_keyboardOpen)
        {
            InputManager.instance.GamepadTextInputBlock();
            return;
        }

        if (!_hover.isHovering)
        {
            if (_field.isFocused) _field.DeactivateInputField();
            return;
        }

        if (_field.isFocused) MenuManager.instance.TextInputActive();
        else if (SemiFunc.InputDown(InputKey.UIClick)) Open();
    }

    private void Open()
    {
        if (SteamClient.IsValid)
            _keyboardOpen = SteamUtils.ShowGamepadTextInput(GamepadTextInputMode.Normal, GamepadTextInputLineMode.SingleLine,
                "Search cosmetics", _field.characterLimit, _field.text);

        if (_keyboardOpen) InputManager.instance.GamepadTextInputBlock();
        else _field.ActivateInputField();
    }

    private void OnKeyboardDismissed(bool submitted)
    {
        if (!_keyboardOpen) return;
        _keyboardOpen = false;
        if (submitted) _field.text = SteamUtils.GetEnteredGamepadText() ?? "";
    }

    // Shows the confirm button's icon on a controller, since nothing can be typed there directly.
    private void RefreshPlaceholder()
    {
        var device = InputGlyphs.CurrentDevice;
        if (_placeholderDevice == device) return;
        _placeholderDevice = device;

        if (_field.placeholder is TMP_Text placeholder)
            placeholder.text = device == InputDeviceType.KeyboardMouse
                ? KeyboardPlaceholder
                : $"{InputGlyphs.GetSpriteTag(InputKey.UIClick)} Search";
    }
}
