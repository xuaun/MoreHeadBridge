namespace MoreHeadBridge;

/// Controls where the search bar appears in the cosmetics menu.
public enum SearchBarPosition { Bottom, Top }

/// Which held key modifies a cosmetics-menu click (one per action in the config).
public enum MenuClickModifier { Ctrl, Alt, Shift }

/// Which gamepad button runs a cosmetics-menu action (one per action in the config). Named by position: labels differ per controller family.
public enum MenuGamepadButton { Off, TopButton, LeftButton, LeftTrigger, RightTrigger, LeftStickClick, RightStickClick }

/// What a cosmetics-menu click or gamepad shortcut does; Equip is the plain click.
public enum CosmeticClickAction { Equip, Favorite, Hide, Customize }
