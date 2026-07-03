using BepInEx.Configuration;

namespace MoreHeadBridge;

public partial class Plugin
{
    // ── [General] ────────────────────────────────────────────────────────────
    public static ConfigEntry<bool> AutoUnlockBridgeCosmetics { get; private set; } = null!;
    public static ConfigEntry<bool> AllowMultipleCosmetics { get; private set; } = null!;
    public static ConfigEntry<bool> EnablePerCosmeticColors { get; private set; } = null!;
    public static ConfigEntry<bool> EnableMiniSemibot { get; private set; } = null!;
    public static ConfigEntry<string> SpecificFolders { get; private set; } = null!;

    // ── [CosmeticsMenu] ──────────────────────────────────────────────────────
    public static ConfigEntry<bool> EnableMenuEnhancements { get; private set; } = null!;
    public static ConfigEntry<bool> ShowToolsButton { get; private set; } = null!;
    public static ConfigEntry<bool> GroupCosmeticVariants { get; private set; } = null!;
    public static ConfigEntry<bool> HideMoreHeadButton { get; private set; } = null!;
    public static ConfigEntry<bool> HideMoreHeadDecorations { get; private set; } = null!;
    public static ConfigEntry<bool> ExcludeMoreHeadFromPresetIcons { get; private set; } = null!;
    public static ConfigEntry<SearchBarPosition> SearchFieldPosition { get; private set; } = null!;

    // ── [Blacklist] ──────────────────────────────────────────────────────────
    public static ConfigEntry<BlacklistLoadMode> BridgeBlacklistMode { get; private set; } = null!;
    public static ConfigEntry<bool> MirrorBlacklistToMoreHead { get; private set; } = null!;

    // ── [BridgeAppearance] ────────────────────────────────────────────────────
    public static ConfigEntry<bool> EnableBridgeTinting { get; private set; } = null!;
    public static ConfigEntry<bool> EnableBridgeColorAnimations { get; private set; } = null!;
    public static ConfigEntry<bool> SeeRemoteColorAnimations { get; private set; } = null!;
    public static ConfigEntry<bool> EnableBridgeCustomColors { get; private set; } = null!;
    public static ConfigEntry<bool> EnableWorldFollowSpring { get; private set; } = null!;
    public static ConfigEntry<bool> HighlightBridgeCosmetics { get; private set; } = null!;
    public static ConfigEntry<SemiFunc.Rarity> BridgeDefaultRarity { get; private set; } = null!;

    // ── [CosmeticCustomizer] ─────────────────────────────────────────────────
    public static ConfigEntry<bool> EnableCosmeticCustomizer { get; private set; } = null!;
    public static ConfigEntry<bool> UseVanillaPositionFixes { get; private set; } = null!;
    public static ConfigEntry<bool> ImportOverrides { get; private set; } = null!;
    public static ConfigEntry<bool> ExportOverrides { get; private set; } = null!;

    // ── [VanillaCosmetics] ───────────────────────────────────────────────────
    public static ConfigEntry<bool> EnableVanillaCustomColors { get; private set; } = null!;

    // ── [OtherModdedCosmetics] ────────────────────────────────────────────────
    public static ConfigEntry<bool> HighlightModdedCosmetics { get; private set; } = null!;
    public static ConfigEntry<bool> AutoUnlockModdedCosmetics { get; private set; } = null!;
    public static ConfigEntry<bool> AllowModdedOverrides { get; private set; } = null!;
    public static ConfigEntry<bool> ResetModdedUnlocks { get; private set; } = null!;
    public static ConfigEntry<bool> EnableModdedCustomColors { get; private set; } = null!;

    // ── [Compatibility] ──────────────────────────────────────────────────────
    public static ConfigEntry<bool> FixCosmeticsMenuPerformance { get; private set; } = null!;
    public static ConfigEntry<bool> RemoveBridgePhysics { get; private set; } = null!;
    public static ConfigEntry<bool> LoopBridgeAnimation { get; private set; } = null!;
    public static ConfigEntry<VanillaEquipAnimationMode> BridgeEquipAnimationMode { get; private set; } = null!;

    // ── [BridgeIcons] ────────────────────────────────────────────────────────
    public static ConfigEntry<bool> UseIsolatedIconRender { get; private set; } = null!;
    public static ConfigEntry<bool> UseTextureAsPlaceholder { get; private set; } = null!;
    public static ConfigEntry<bool> AutoCaptureIcons { get; private set; } = null!;
    public static ConfigEntry<bool> GenerateAllIcons { get; private set; } = null!;
    public static ConfigEntry<bool> HideClothesWhileGenerating { get; private set; } = null!;
    public static ConfigEntry<bool> ResetBodyColorWhileGenerating { get; private set; } = null!;
    public static ConfigEntry<bool> HideAvatarWhileGenerating { get; private set; } = null!;

    // ── [Reset] ──────────────────────────────────────────────────────────────
    public static ConfigEntry<bool> ResetBridgeUnlocks { get; private set; } = null!;
    public static ConfigEntry<bool> ResetCosmeticCustomizer { get; private set; } = null!;
    public static ConfigEntry<bool> DeleteIconCache { get; private set; } = null!;
    public static ConfigEntry<string> DeleteIconsMatching { get; private set; } = null!;

    // ── [Debug] ──────────────────────────────────────────────────────────────
    public static ConfigEntry<bool> ShowBridgeDebugLogs { get; private set; } = null!;

    private void BindConfig()
    {
        // ── [General] ────────────────────────────────────────────────────────
        AutoUnlockBridgeCosmetics = Config.Bind(
            section: "General",
            key: "AutoUnlockBridgeCosmetics",
            defaultValue: true,
            description: "Auto-unlock NEW bridge cosmetics on every load.\n" +
                          "\n" +
                          "When TRUE  — every bridge cosmetic gets added to your inventory\n" +
                          "             on game start, so you never have to grind for them.\n" +
                          "When FALSE — bridge cosmetics behave like vanilla ones:\n" +
                          "             you have to earn them in-game.\n" +
                          "\n" +
                          "IMPORTANT: this flag only controls what happens going FORWARD.\n" +
                          "Cosmetics unlocked while AutoUnlockBridgeCosmetics was TRUE get saved permanently to\n" +
                          "the REPOLib modded save file. Flipping this to FALSE later does NOT\n" +
                          "remove them — REPOLib re-reads the save on every launch.\n" +
                          "If you want to wipe existing unlocks, see the [Reset] section below."
        );

        AllowMultipleCosmetics = Config.Bind(
            section: "General",
            key: "AllowMultipleCosmetics",
            defaultValue: true,
            description: "When true, you can equip multiple cosmetics of the same type at once\n" +
                         "(e.g. two hats, three body pieces, several worlds).\n" +
                         "Applies to: Hat, HeadBottom, FaceTop, FaceBottom, Eyewear, Ears,\n" +
                         "           BodyTop, BodyBottom, ArmRight, ArmLeft,\n" +
                         "           LegRight, FootRight, LegLeft, FootLeft, World."
        );

        EnablePerCosmeticColors = Config.Bind(
            section: "General",
            key: "EnablePerCosmeticColors",
            defaultValue: true,
            description: "Master switch for the per-cosmetic color SYSTEM (all cosmetic kinds):\n" +
                        "per-cosmetic palette colors, custom RGB (bridge/vanilla/modded), animated\n" +
                        "colors, and the color sync to other players.\n" +
                        "When FALSE: the mod leaves all cosmetic colors to the vanilla per-type\n" +
                        "palette — no per-cosmetic overrides are applied, sent, or received.\n" +
                        "Applies live. Bridge tinting specifically is controlled by EnableBridgeTinting."
        );

        EnableMiniSemibot = Config.Bind(
            section: "General",
            key: "EnableMiniSemibot",
            defaultValue: true,
            description: "Adds the 'Mini-Semibot' cosmetic to the WORLD tab — a small copy of your avatar,\n" +
                         "dressed in your current outfit, that follows you around and is visible to other\n" +
                         "players. Shift+click it in the menu to tweak how it looks and behaves.\n" +
                         "Takes effect immediately: OFF unequips it, ON registers/restores the cosmetic."
        );

        SpecificFolders = Config.Bind(
            section: "General",
            key: "SpecificFolders",
            defaultValue: "",
            description: "Comma-separated subfolder names under BepInEx/plugins to scan for .hhh files. Empty = scan all. " +
                          "Example: 'Some-MoreHeadPack,Another-CosmeticsPack'. Matching is case-insensitive and uses path contains."
        );

        // ── [CosmeticsMenu] ──────────────────────────────────────────────────
        EnableMenuEnhancements = Config.Bind(
            section: "CosmeticsMenu",
            key: "EnableMenuEnhancements",
            defaultValue: true,
            description: "When TRUE (default), enables the extended cosmetics menu:\n" +
                         "  • Virtual tabs: SEARCH, SELECTED, FAV, HIDE\n" +
                         "  • Ctrl+click to favorite, Alt+click to hide cosmetics\n" +
                         "  • Live search bar and cosmetic name hover tooltip\n" +
                         "Set to FALSE if you prefer the unmodified vanilla cosmetics menu."
        );

        ShowToolsButton = Config.Bind(
            section: "CosmeticsMenu",
            key: "ShowToolsButton",
            defaultValue: true,
            description: "When TRUE (default), shows the Tools dropdown button in the cosmetics menu.\n" +
                         "The button provides: Generate Icons, Clear All Icons, and Cosmetic Settings.\n" +
                         "Set to FALSE to hide it if you don't use those features."
        );

        GroupCosmeticVariants = Config.Bind(
            section: "CosmeticsMenu",
            key: "GroupCosmeticVariants",
            defaultValue: false,
            description: "When TRUE, collapses families of cosmetics that differ only by a variant\n" +
                         "into a single menu button to de-clutter the list: pride flags of the same RepoPride\n" +
                         "item, and color variants of the same MoreHead pack item (e.g. BASICS). Click the\n" +
                         "button to open a popup and pick the variant. Requires MenuLib.\n" +
                         "Set to FALSE to show every variant as its own button (vanilla behaviour)."
        );

        HideMoreHeadButton = Config.Bind(
            section: "CosmeticsMenu",
            key: "HideMoreHeadButton",
            defaultValue: false,
            description: "If true, hides the MoreHead button from all menus so you can use only the vanilla cosmetics UI.\n" +
                         "Applies automatically when changed — no restart required."
        );

        HideMoreHeadDecorations = Config.Bind(
            section: "CosmeticsMenu",
            key: "HideMoreHeadDecorations",
            defaultValue: false,
            description: "If true, hides the decorations you equipped through the MoreHead menu on\n" +
                         "player avatars. Owner-authoritative and synced: while on, YOUR MoreHead decorations\n" +
                         "are hidden for everyone (a temporary 'no decorations' look), and other players running\n" +
                         "this mod won't render them either. Bridge and vanilla cosmetics are unaffected.\n" +
                         "Applies automatically when changed — no restart required."
        );

        BridgeBlacklistMode = Config.Bind(
            section: "Blacklist",
            key: "BlacklistMode",
            defaultValue: BlacklistLoadMode.NotLoadIngame,
            description: "What the bridge does with cosmetics on its blacklist.\n" +
                         "NotLoadIngame    = skip them entirely (saves memory; matches MoreHead's behaviour).\n" +
                         "LoadOnHiddenMenu = still register them, but start them hidden in the menu (HIDE tab).\n" +
                         "Load-time — changes apply on the next game launch."
        );

        MirrorBlacklistToMoreHead = Config.Bind(
            section: "Blacklist",
            key: "BlacklistOnMoreHead",
            defaultValue: false,
            description: "If true, the bridge also writes its blacklisted cosmetic names into MoreHead's own\n" +
                         "blacklist (merge-only — it never removes entries you added yourself), so MoreHead stops\n" +
                         "loading those decorations too. Load-time — applies on the next launch."
        );

        ExcludeMoreHeadFromPresetIcons = Config.Bind(
            section: "CosmeticsMenu",
            key: "ExcludeMoreHeadFromPresetIcons",
            defaultValue: false,
            description: "When TRUE, MoreHead-menu decorations are left out of the saved preset PREVIEW IMAGE,\n" +
                         "so a preset's thumbnail shows only the cosmetics that preset actually stores.\n" +
                         "Local only — preset thumbnails are a per-machine PNG cache; nothing to sync.\n" +
                         "Re-save a preset (or clear its cached icon) to refresh an existing thumbnail."
        );

        SearchFieldPosition = Config.Bind(
            section: "CosmeticsMenu",
            key: "SearchFieldPosition",
            defaultValue: SearchBarPosition.Top,
            description: "Where the search bar appears in the cosmetics menu.\n" +
                         "Bottom = at the bottom of the Semibot.\n" +
                         "Top    = above the category strip (default)."
        );

        // ── [BridgeAppearance] ────────────────────────────────────────────────
        EnableBridgeTinting = Config.Bind(
            section: "BridgeAppearance",
            key: "EnableBridgeTinting",
            defaultValue: true,
            description: "Enables tinting for BRIDGE (.hhh) cosmetics specifically.\n" +
                         "When TRUE (default): bridge cosmetics with a supported color channel can be\n" +
                         "tinted via the in-game color picker (per-cosmetic and per-slot).\n" +
                         "When FALSE: bridge cosmetics keep their original author colors and ignore\n" +
                         "section paints — vanilla/modded cosmetics are unaffected (see\n" +
                         "EnablePerCosmeticColors for the system-wide switch).\n" +
                         "\n" +
                         "Per-cosmetic Tintable overrides (Shift+click → CosmeticCustomizer) always\n" +
                         "take priority over this global setting."
        );

        EnableBridgeCustomColors = Config.Bind(
            section: "BridgeAppearance",
            key: "EnableBridgeCustomColors",
            defaultValue: false,
            description: "When TRUE, a \"C\" button appears in the color picker for tintable bridge\n" +
                         "cosmetics, opening RGB sliders to paint the cosmetic any custom color\n" +
                         "(not limited to the game's palette). Custom colors are synced to other\n" +
                         "players. When FALSE (default), the button is hidden."
        );

        EnableBridgeColorAnimations = Config.Bind(
            section: "BridgeAppearance",
            key: "EnableBridgeColorAnimations",
            defaultValue: false,
            description: "When TRUE, an \"A\" button appears in the color picker for tintable bridge\n" +
                         "cosmetics, letting you set an animated color (Cycle / Rainbow). Animations are\n" +
                         "synced to other players. When FALSE (default), the feature is fully off — no\n" +
                         "button and no animations run (including remote players')."
        );

        SeeRemoteColorAnimations = Config.Bind(
            section: "BridgeAppearance",
            key: "SeeRemoteColorAnimations",
            defaultValue: false,
            description: "When TRUE, you see animated colors that other players have set on their cosmetics.\n" +
                         "When FALSE (default), remote players' animated colors are hidden on your screen —\n" +
                         "they still animate on their own screen and on any client that has this enabled.\n" +
                         "Does not affect your own animations. Only relevant when EnableBridgeColorAnimations is TRUE."
        );

        EnableWorldFollowSpring = Config.Bind(
            section: "BridgeAppearance",
            key: "EnableWorldFollowSpring",
            defaultValue: false,
            description: "When TRUE, a \"Follow Smoothing\" slider appears in each WORLD cosmetic's Shift+click\n" +
                         "popup, letting that cosmetic trail you with a soft lag or a bouncy overshoot instead\n" +
                         "of being rigidly glued to you. Each world cosmetic keeps its own choice\n" +
                         "(Off / Soft / Bouncy). Purely a local visual feel; not synced. When FALSE (default),\n" +
                         "the slider is hidden and world cosmetics stay rigidly attached."
        );

        HighlightBridgeCosmetics = Config.Bind(
            section: "BridgeAppearance",
            key: "HighlightBridgeCosmetics",
            defaultValue: true,
            description: "When TRUE (default), bridge cosmetics show an orange border in the cosmetics menu,\n" +
                         "making them visually distinct from vanilla cosmetics at a glance.\n" +
                         "The sort position is unaffected — it is still controlled by BridgeDefaultRarity.\n" +
                         "When FALSE, bridge cosmetics use the standard rarity border color like any vanilla cosmetic.\n" +
                         "\n" +
                         "Per-cosmetic overrides set via the CosmeticCustomizer popup take priority over this setting."
        );

        BridgeDefaultRarity = Config.Bind(
            section: "BridgeAppearance",
            key: "BridgeDefaultRarity",
            defaultValue: SemiFunc.Rarity.Common,
            description: "Rarity tier assigned to bridge cosmetics in the vanilla shop. Values: Common, Uncommon, Rare, UltraRare.\n" +
                         "Controls sort position in the menu (UltraRare appears first, Common last).\n" +
                         "The visual border color is controlled separately by HighlightBridgeCosmetics.\n" +
                         "\n" +
                         "Per-cosmetic rarity overrides set via the CosmeticCustomizer popup take priority over this setting."
        );

        // ── [CosmeticCustomizer] ─────────────────────────────────────────────
        EnableCosmeticCustomizer = Config.Bind(
            section: "CosmeticCustomizer",
            key: "EnableCosmeticCustomizer",
            defaultValue: false,
            description: "When TRUE, Shift+click on any bridge cosmetic in the menu opens a popup\n" +
                         "that lets you override its rarity tier and category (Hat, BodyTop, World, …)\n" +
                         "individually. Overrides are saved and applied on every launch.\n" +
                         "\n" +
                         "Requires MenuLib to be installed."
        );

        UseVanillaPositionFixes = Config.Bind(
            section: "CosmeticCustomizer",
            key: "UseVanillaPositionFixes",
            defaultValue: true,
            description: "When TRUE, bridge cosmetics get automatic vanilla-style position/scale fixes\n" +
                         "that adapt them to non-default body shapes (big/tiny/huge limbs, …) — the\n" +
                         "automatic counterpart of the 'Special Position Fixes' you can set per cosmetic.\n" +
                         "The Missing Right Side head fix is NOT auto-applied: opt in per\n" +
                         "cosmetic via Special Position Fixes ('Use'). Turn OFF to keep every bridge\n" +
                         "cosmetic at its authored size/position regardless of body shape.\n" +
                         "\n" +
                         "Per-cosmetic 'Vanilla Position Fixes' (Customizer popup) overrides this default."
        );

        ImportOverrides = Config.Bind(
            section: "CosmeticCustomizer",
            key: "ImportCosmeticCustomizer",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE, immediately reads\n" +
                          "  BepInEx/config/MoreHeadBridge/overrides_export.json\n" +
                          "and merges its contents into the local Cosmetic Customizer store.\n" +
                          "Local Cosmetic Customizer settings not in the file are kept (true merge, not replace).\n" +
                          "Auto-flips back to FALSE after running."
        );

        ExportOverrides = Config.Bind(
            section: "CosmeticCustomizer",
            key: "ExportCosmeticCustomizer",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE, immediately exports ALL current\n" +
                          "Cosmetic Customizer settings to:\n" +
                          "  BepInEx/config/MoreHeadBridge/overrides_export.json\n" +
                          "Merges with any existing file — entries already in the file are\n" +
                          "updated, others are kept as-is.\n" +
                          "Auto-flips back to FALSE after running."
        );

        // ── [VanillaCosmetics] ───────────────────────────────────────────────
        EnableVanillaCustomColors = Config.Bind(
            section: "VanillaCosmetics",
            key: "EnableVanillaCustomColors",
            defaultValue: false,
            description: "When TRUE, a \"C\" button appears in the color picker for vanilla cosmetics\n" +
                         "that support a custom color channel (Hurtable shader with _AlbedoColor),\n" +
                         "allowing you to paint them any RGB color beyond the game's palette.\n" +
                         "When FALSE (default), vanilla cosmetics can only use the standard palette."
        );

        // ── [OtherModdedCosmetics] ────────────────────────────────────────────
        AutoUnlockModdedCosmetics = Config.Bind(
            section: "OtherModdedCosmetics",
            key: "AutoUnlockModdedCosmetics",
            defaultValue: false,
            description: "When TRUE, automatically unlocks all cosmetics registered by other mods via\n" +
                          "REPOLib (non-bridge modded cosmetics) on every game start.\n" +
                          "\n" +
                          "Cosmetics unlocked this way are tracked in a separate file so that\n" +
                          "ResetModdedUnlocks (below) can remove exactly those without touching\n" +
                          "cosmetics you earned through normal gameplay."
        );

        EnableModdedCustomColors = Config.Bind(
            section: "OtherModdedCosmetics",
            key: "EnableModdedCustomColors",
            defaultValue: false,
            description: "When TRUE, a \"C\" button appears in the color picker for modded non-bridge\n" +
                         "cosmetics (registered via REPOLib by other mods) that support a custom color\n" +
                         "channel, allowing custom RGB painting beyond the game's palette.\n" +
                         "When FALSE (default), modded cosmetics can only use the standard palette."
        );

        HighlightModdedCosmetics = Config.Bind(
            section: "OtherModdedCosmetics",
            key: "HighlightModdedCosmetics",
            defaultValue: false,
            description: "When TRUE, modded NON-bridge cosmetics (registered by other REPOLib mods)\n" +
                         "show a purple border in the cosmetics menu, marking them as coming from\n" +
                         "another mod. When FALSE (default), they use their normal rarity border.\n" +
                         "\n" +
                         "Bridge (.hhh) cosmetics are controlled separately by HighlightBridgeCosmetics.\n" +
                         "Per-cosmetic overrides (Shift+click) take priority over this setting."
        );

        AllowModdedOverrides = Config.Bind(
            section: "OtherModdedCosmetics",
            key: "AllowModdedCosmeticCustomizer",
            defaultValue: false,
            description: "ADVANCED / opt-in. When TRUE, the Cosmetic Customizer popup\n" +
                         "(Shift+click) also opens for MODDED non-bridge cosmetics registered via\n" +
                         "REPOLib — letting you remap their category, add offset/hide conditions, etc.\n" +
                         "Modded cosmetics are authored against the vanilla anchors, so type remaps\n" +
                         "re-parent them onto those anchors. Leave OFF unless you know what you're doing.\n" +
                         "\n" +
                         "Requires EnableCosmeticCustomizer (and MenuLib) to be enabled."
        );

        ResetModdedUnlocks = Config.Bind(
            section: "OtherModdedCosmetics",
            key: "ResetModdedUnlocks",
            defaultValue: false,
            description: "⚠ DESTRUCTIVE ONE-SHOT TRIGGER ⚠\n" +
                          "\n" +
                          "Removes from your save file ONLY the non-bridge modded cosmetics that\n" +
                          "were unlocked by the AutoUnlockModdedCosmetics option.\n" +
                          "Cosmetics you earned through normal gameplay are NOT affected.\n" +
                          "Auto-flips back to FALSE after running."
        );

        // ── [Compatibility] ──────────────────────────────────────────────────
        FixCosmeticsMenuPerformance = Config.Bind(
            section: "Compatibility",
            key: "FixCosmeticsMenuPerformance",
            defaultValue: false,
            description: "Enable this if you have many cosmetics mods and the vanilla\n" +
                         "cosmetics tabs (HEAD/BODY/ARMS/LEGS) are stuttering or\n" +
                         "taking too long to load. Default = FALSE."
        );

        RemoveBridgePhysics = Config.Bind(
            section: "Compatibility",
            key: "RemoveBridgePhysics",
            defaultValue: true,
            description: "When TRUE (default), removes physics components (collider and rigidbody) from bridge\n" +
                         "cosmetic prefabs at load time. Prevents physics interference and the\n" +
                         "character-rotation bug in the cosmetics preview menu.\n" +
                         "Per-cosmetic override (Shift+click) takes priority over this global setting.\n" +
                         "Takes effect on the next game launch."
        );

        LoopBridgeAnimation = Config.Bind(
            section: "Compatibility",
            key: "LoopBridgeAnimation",
            defaultValue: true,
            description: "When TRUE (default), forces all Animation clips and Animator states on bridge\n" +
                         "(.hhh) cosmetic prefabs to loop. Useful for ambient idle animations.\n" +
                         "Set to FALSE for cosmetics with intentional one-shot animations.\n" +
                         "Per-cosmetic override (Shift+click) takes priority over this global setting.\n" +
                         "Takes effect on the next game launch."
        );

        BridgeEquipAnimationMode = Config.Bind(
            section: "Compatibility",
            key: "BridgeEquipAnimationMode",
            defaultValue: VanillaEquipAnimationMode.Fixed,
            description: "Controls how vanilla equip animation behaves for bridge cosmetics.\n" +
                         "Fixed   = keep a small non-zero scale on spawn (prevents world animation collapse).\n" +
                         "Normal  = vanilla behavior (scale to zero then pop out).\n" +
                         "Disabled= skip vanilla equip animation (spawn at final scale).\n" +
                         "Per-cosmetic override (Shift+click) takes priority."
        );

        // ── [BridgeIcons] ────────────────────────────────────────────────────
        UseTextureAsPlaceholder = Config.Bind(
            section: "BridgeIcons",
            key: "UseTextureAsPlaceholder",
            defaultValue: true,
            description: "When TRUE (default) — the cosmetic's texture is used as the icon, overlaid on the placeholder background.\n" +
                          "When FALSE — the texture is NOT applied to the placeholder; the slot keeps the plain placeholder icon\n" +
                          "             until a captured icon (AutoCaptureIcons / GenerateAllIcons) replaces it."
        );

        UseIsolatedIconRender = Config.Bind(
            section: "BridgeIcons",
            key: "UseIsolatedIconRender",
            defaultValue: true,
            description: "When TRUE (default), bridge cosmetic icons are rendered in ISOLATION\n" +
                         "using a dedicated camera + lights rig (mirroring vanilla's SemiIconMaker),\n" +
                         "instead of cropping a region from the live menu-avatar preview. Isolated\n" +
                         "renders are cleaner and independent of what else is equipped / the avatar pose.\n" +
                         "Set FALSE to use the avatar-crop capture instead. Delete the icon cache\n" +
                         "(Reset → DeleteIconCache) after toggling so icons regenerate with the chosen method."
        );

        AutoCaptureIcons = Config.Bind(
            section: "BridgeIcons",
            key: "AutoCaptureIcons",
            defaultValue: true,
            description: "Reactively capture icons while you browse the cosmetics menu.\n" +
                          "\n" +
                          "When TRUE  — every time you HOVER a bridge cosmetic in the menu,\n" +
                          "             the game's existing avatar preview is snapshotted and\n" +
                          "             saved as a PNG icon for that cosmetic. Next time the UI\n" +
                          "             asks for that icon it loads the PNG (instant).\n" +
                          "             Icons fill in gradually as you explore the menu.\n" +
                          "When FALSE — no captures. Bridge cosmetics keep the texture/placeholder\n" +
                          "             fallback icons.\n" +
                          "\n" +
                          "PNG cache lives in:\n" +
                          "  %userprofile%\\AppData\\LocalLow\\semiwork\\REPO\\Cache\\Icons\\CosmeticsModded\\MoreHeadBridge_CosmeticsIcons\\\n" +
                          "Delete that folder to wipe all generated icons.\n" +
                          "(Icons are stored in Cache\\Icons\\CosmeticsModded\\ — a sibling of the vanilla\n" +
                          " Cache\\Icons\\Cosmetics\\ that REPOLib wipes, so ours are never touched.)"
        );

        GenerateAllIcons = Config.Bind(
            section: "BridgeIcons",
            key: "GenerateAllIcons",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE, the next time you open the cosmetics menu\n" +
                          "the mod will cycle through EVERY bridge cosmetic without a cached icon,\n" +
                          "preview-equipping each one, snapshotting the avatar, and saving the PNG.\n" +
                          "\n" +
                          "Effects while running:\n" +
                          "  * Equipped cosmetics are hidden by default (HideClothesWhileGenerating).\n" +
                          "  * Body color is reset to default by default (ResetBodyColorWhileGenerating).\n" +
                          "  * The avatar display is hidden by default (HideAvatarWhileGenerating).\n" +
                          "  * Progress is shown on-screen where the avatar was.\n" +
                          "  * Console logs progress every 50 items.\n" +
                          "  * Expect ~1-3 minutes for 1600+ cosmetics.\n" +
                          "  * Whatever you had previewing/equipped is restored at the end.\n" +
                          "  * This flag auto-resets to FALSE so it doesn't fire again.\n" +
                          "\n" +
                          "Use this if you want all icons generated in one go instead of as you browse.\n" +
                          "Requires AutoCaptureIcons logic — keeps working even if AutoCaptureIcons=false."
        );

        HideAvatarWhileGenerating = Config.Bind(
            section: "BridgeIcons",
            key: "HideAvatarWhileGenerating",
            defaultValue: true,
            description: "When TRUE (default), hides the avatar preview display while\n" +
                         "GenerateAllIcons is running, so the rapid cosmetic cycling\n" +
                         "is not visible on screen. The avatar camera still renders\n" +
                         "internally (icons are still captured correctly) — only the\n" +
                         "on-screen preview image is hidden.\n" +
                         "Set to FALSE if you want to watch the batch progress visually."
        );

        HideClothesWhileGenerating = Config.Bind(
            section: "BridgeIcons",
            key: "HideClothesWhileGenerating",
            defaultValue: true,
            description: "When TRUE (default), only the cosmetic being captured is shown on the avatar\n" +
                         "during GenerateAllIcons — all other equipped cosmetics are hidden.\n" +
                         "This gives clean, isolated icons for each cosmetic.\n" +
                         "When FALSE, your full equipped loadout is kept visible alongside the\n" +
                         "cosmetic being captured."
        );

        ResetBodyColorWhileGenerating = Config.Bind(
            section: "BridgeIcons",
            key: "ResetBodyColorWhileGenerating",
            defaultValue: true,
            description: "When TRUE (default), the avatar body color is temporarily set to its default\n" +
                         "(index 0 for every color slot) while GenerateAllIcons is running,\n" +
                         "so each icon is captured on a neutral-colored avatar.\n" +
                         "Your actual body colors are not changed — they are restored\n" +
                         "automatically after generation ends (or is interrupted)."
        );

        // ── [Reset] ──────────────────────────────────────────────────────────
        ResetBridgeUnlocks = Config.Bind(
            section: "Reset",
            key: "ResetBridgeUnlocks",
            defaultValue: false,
            description: "⚠ DESTRUCTIVE ONE-SHOT TRIGGER ⚠\n" +
                          "\n" +
                          "Setting this to TRUE causes the NEXT game launch to:\n" +
                          "  1. Remove EVERY bridge cosmetic from your unlocks list\n" +
                          "  2. Remove them from any saved outfit/preset you have equipped\n" +
                          "  3. Remove them from your history\n" +
                          "  4. Rewrite the REPOLib modded save file\n" +
                          "  5. Auto-flip this flag back to FALSE so it doesn't fire again\n" +
                          "\n" +
                          "Use this if you want to start over with bridge cosmetics.\n" +
                          "If AutoUnlockBridgeCosmetics=true, cosmetics are wiped and immediately re-unlocked on the same launch.\n" +
                          "Set AutoUnlockBridgeCosmetics=false FIRST if you want to keep them locked after the reset.\n" +
                          "\n" +
                          "This does NOT touch vanilla cosmetics or cosmetics from other mods.\n" +
                          "This does NOT delete the .hhh files — only the unlock state."
        );

        ResetCosmeticCustomizer = Config.Bind(
            section: "Reset",
            key: "ResetCosmeticCustomizer",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE on the next launch:\n" +
                          "  1. Clears ALL per-cosmetic overrides (rarity, category, modded flag)\n" +
                          "     set via the Cosmetic Customizer popup\n" +
                          "  2. Deletes CosmeticOverrides.json from BepInEx/config/MoreHeadBridge\n" +
                          "  3. Auto-flips this flag back to FALSE\n" +
                          "\n" +
                          "Bridge cosmetics will revert to the global BridgeDefaultRarity and their\n" +
                          "original .hhh file category on the same launch."
        );

        DeleteIconCache = Config.Bind(
            section: "Reset",
            key: "DeleteIconCache",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE on launch, delete cached bridge icon PNGs from:\n" +
                          "  %userprofile%\\AppData\\LocalLow\\semiwork\\REPO\\Cache\\Icons\\CosmeticsModded\\MoreHeadBridge_CosmeticsIcons\\\n" +
                          "Use DeleteIconsMatching to filter which ones to delete.\n" +
                          "Auto-resets to FALSE after running."
        );

        DeleteIconsMatching = Config.Bind(
            section: "Reset",
            key: "DeleteIconsMatching",
            defaultValue: "",
            description: "Optional comma-separated filter for DeleteIconCache. Case-insensitive\n" +
                          "substring match against the icon filename (which is the cosmetic's internal name).\n" +
                          "Empty = delete ALL bridge icons.\n" +
                          "Example: 'PirateHat,Waluigi' deletes only icons whose name contains either."
        );

        // ── [Debug] ──────────────────────────────────────────────────────────
        ShowBridgeDebugLogs = Config.Bind(
            section: "Debug",
            key: "ShowBridgeDebugLogs",
            defaultValue: false,
            description: "If true, do NOT suppress NullReferenceExceptions for bridge cosmetics.\n" +
                          "Use this to diagnose bridge-only issues (will spam logs if the base game is noisy)."
        );
    }
}
