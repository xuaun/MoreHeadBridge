using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

public enum SearchBarPosition { Bottom, Top }

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("REPOLib")]
[BepInDependency("space.customizing.console", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Mhz.REPOMoreHead", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; } = null!;
    public new static ManualLogSource Logger { get; private set; } = null!;

    // ── [General] ────────────────────────────────────────────────────────────
    public static ConfigEntry<bool>            UnlockAll              { get; private set; } = null!;
    public static ConfigEntry<bool>            AllowMultipleCosmetics { get; private set; } = null!;
    public static ConfigEntry<bool>            EnableMenuEnhancements { get; private set; } = null!;
    public static ConfigEntry<bool>            HideMoreHeadButton     { get; private set; } = null!;
    public static ConfigEntry<string>          SpecificFolders        { get; private set; } = null!;
    public static ConfigEntry<SearchBarPosition> SearchFieldPosition  { get; private set; } = null!;

    // ── [Appearance] ─────────────────────────────────────────────────────────
    public static ConfigEntry<bool>            HighlightModdedCosmetics { get; private set; } = null!;
    public static ConfigEntry<SemiFunc.Rarity> DefaultRarity            { get; private set; } = null!;

    // ── [Icons] ──────────────────────────────────────────────────────────────
    public static ConfigEntry<bool>            UseTextureAsPlaceholder { get; private set; } = null!;
    public static ConfigEntry<bool>            AutoCaptureIcons        { get; private set; } = null!;
    public static ConfigEntry<bool>            GenerateAllIcons        { get; private set; } = null!;

    // ── [CosmeticCustomizer] ─────────────────────────────────────────────────
    public static ConfigEntry<bool>            EnableCosmeticOverrideUI { get; private set; } = null!;

    // ── [Compatibility] ──────────────────────────────────────────────────────
    public static ConfigEntry<bool>            FixBridgedCosmetics    { get; private set; } = null!;

    // ── [Reset] ──────────────────────────────────────────────────────────────
    public static ConfigEntry<bool>            ResetUnlocks         { get; private set; } = null!;
    public static ConfigEntry<bool>            ResetCosmeticCustomizer { get; private set; } = null!;
    public static ConfigEntry<bool>            DeleteIconCache      { get; private set; } = null!;
    public static ConfigEntry<string>          DeleteIconsMatching  { get; private set; } = null!;

    // ── [Debug] ──────────────────────────────────────────────────────────────
    public static ConfigEntry<bool>            ShowBridgeDebugLogs  { get; private set; } = null!;

    // True if MenuLib was detected in the loaded plugin chain. Cached once in Awake.
    public static bool MenuLibAvailable { get; private set; }

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        // ── [General] ────────────────────────────────────────────────────────
        UnlockAll = Config.Bind(
            section: "General",
            key: "UnlockAll",
            defaultValue: true,
            description: "Auto-unlock NEW bridge cosmetics on every load.\n" +
                          "\n" +
                          "When TRUE  — every bridge cosmetic gets added to your inventory\n" +
                          "             on game start, so you never have to grind for them.\n" +
                          "When FALSE — bridge cosmetics behave like vanilla ones:\n" +
                          "             you have to earn them in-game.\n" +
                          "\n" +
                          "IMPORTANT: this flag only controls what happens going FORWARD.\n" +
                          "Cosmetics unlocked while UnlockAll was TRUE get saved permanently to\n" +
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

        EnableMenuEnhancements = Config.Bind(
            section: "General",
            key: "EnableMenuEnhancements",
            defaultValue: true,
            description: "When TRUE (default), enables the extended cosmetics menu:\n" +
                         "  • Virtual tabs: SEARCH, SELECTED, FAV, HIDE\n" +
                         "  • Ctrl+click to favorite, Alt+click to hide cosmetics\n" +
                         "  • Live search bar and cosmetic name hover tooltip\n" +
                         "Set to FALSE if you prefer the unmodified vanilla cosmetics menu."
        );

        HideMoreHeadButton = Config.Bind(
            section: "General",
            key: "HideMoreHeadButton",
            defaultValue: false,
            description: "If true, removes the MoreHead button from all menus so you can use only the vanilla cosmetics UI. Requires restart."
        );

        SpecificFolders = Config.Bind(
            section: "General",
            key: "SpecificFolders",
            defaultValue: "",
            description: "Comma-separated subfolder names under BepInEx/plugins to scan for .hhh files. Empty = scan all. " +
                          "Example: 'Some-MoreHeadPack,Another-CosmeticsPack'. Matching is case-insensitive and uses path contains."
        );

        SearchFieldPosition = Config.Bind(
            section: "General",
            key: "SearchFieldPosition",
            defaultValue: SearchBarPosition.Top,
            description: "Where the search bar appears in the cosmetics menu.\n" +
                         "Bottom = at the bottom of the Semibot.\n" +
                         "Top    = above the category strip (default)."
        );

        // ── [Appearance] ─────────────────────────────────────────────────────
        HighlightModdedCosmetics = Config.Bind(
            section: "Appearance",
            key: "HighlightModdedCosmetics",
            defaultValue: true,
            description: "When TRUE (default), bridge cosmetics show an orange border in the cosmetics menu,\n" +
                         "making them visually distinct from vanilla cosmetics at a glance.\n" +
                         "The sort position is unaffected — it is still controlled by DefaultRarity.\n" +
                         "When FALSE, bridge cosmetics use the standard rarity border color like any vanilla cosmetic.\n" +
                         "\n" +
                         "Per-cosmetic overrides set via the CosmeticCustomizer popup take priority over this setting."
        );

        DefaultRarity = Config.Bind(
            section: "Appearance",
            key: "DefaultRarity",
            defaultValue: SemiFunc.Rarity.Common,
            description: "Rarity tier assigned to bridge cosmetics in the vanilla shop. Values: Common, Uncommon, Rare, UltraRare.\n" +
                         "Controls sort position in the menu (UltraRare appears first, Common last).\n" +
                         "The visual border color is controlled separately by HighlightModdedCosmetics.\n" +
                         "\n" +
                         "Per-cosmetic rarity overrides set via the CosmeticCustomizer popup take priority over this setting."
        );

        // ── [Icons] ──────────────────────────────────────────────────────────
        UseTextureAsPlaceholder = Config.Bind(
            section: "Icons",
            key: "UseTextureAsPlaceholder",
            defaultValue: true,
            description: "When TRUE (default) — the cosmetic's texture is used as the icon, overlaid on the placeholder background.\n" +
                          "When FALSE — the texture is NOT applied to the placeholder; the slot keeps the plain placeholder icon\n" +
                          "             until a captured icon (AutoCaptureIcons / GenerateAllIcons) replaces it."
        );

        AutoCaptureIcons = Config.Bind(
            section: "Icons",
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
            section: "Icons",
            key: "GenerateAllIcons",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE, the next time you open the cosmetics menu\n" +
                          "the mod will cycle through EVERY bridge cosmetic without a cached icon,\n" +
                          "preview-equipping each one, snapshotting the avatar, and saving the PNG.\n" +
                          "\n" +
                          "Effects while running:\n" +
                          "  * The avatar will visibly rotate through cosmetics — that IS the progress.\n" +
                          "  * Console logs progress every 50 items.\n" +
                          "  * Expect ~1-3 minutes for 1600+ cosmetics.\n" +
                          "  * Whatever you had previewing/equipped is restored at the end.\n" +
                          "  * This flag auto-resets to FALSE so it doesn't fire again.\n" +
                          "\n" +
                          "Use this if you want all icons generated in one go instead of as you browse.\n" +
                          "Requires AutoCaptureIcons logic — keeps working even if AutoCaptureIcons=false."
        );

        // ── [CosmeticCustomizer] ─────────────────────────────────────────────
        EnableCosmeticOverrideUI = Config.Bind(
            section: "CosmeticCustomizer",
            key: "EnableCosmeticOverrideUI",
            defaultValue: false,
            description: "When TRUE, Shift+click on any bridge cosmetic in the menu opens a popup\n" +
                         "that lets you override its rarity tier and category (Hat, BodyTop, World, …)\n" +
                         "individually. Overrides are saved and applied on every launch.\n" +
                         "\n" +
                         "Requires MenuLib to be installed."
        );

        // ── [Compatibility] ──────────────────────────────────────────────────
        FixBridgedCosmetics = Config.Bind(
            section: "Compatibility",
            key: "FixBridgedCosmetics",
            defaultValue: true,
            description: "When TRUE (default), automatically fixes common asset issues on bridge (.hhh)\n" +
                         "cosmetics at load time (applied directly to the prefab, so every instance\n" +
                         "— including MoreHead's own rendering — inherits the fix):\n" +
                         "  • Removes Collider and Rigidbody components (prevents physics interference\n" +
                         "    and the character-rotation bug in the cosmetics preview menu)\n" +
                         "  • Forces Animation and Animator clips to loop (prevents one-shot animations)\n" +
                         "Takes effect on the next game launch. Set to FALSE only if a specific\n" +
                         "cosmetic breaks due to these fixes."
        );

        // ── [Reset] ──────────────────────────────────────────────────────────
        ResetUnlocks = Config.Bind(
            section: "Reset",
            key: "ResetUnlocks",
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
                          "If UnlockAll=true, cosmetics are wiped and immediately re-unlocked on the same launch.\n" +
                          "Set UnlockAll=false FIRST if you want to keep them locked after the reset.\n" +
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
                          "  2. Deletes MoreHeadBridge_CosmeticOverrides.json from BepInEx/config\n" +
                          "  3. Auto-flips this flag back to FALSE\n" +
                          "\n" +
                          "Bridge cosmetics will revert to the global DefaultRarity and their\n" +
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

        MenuLibAvailable = BepInEx.Bootstrap.Chainloader.PluginInfos
                                  .ContainsKey("nickklmao.menulib");
        if (MenuLibAvailable)
            Logger.LogDebug("MenuLib detected — CosmeticCustomizer UI enabled.");

        PrintBanner();
        PerCosmeticOverrides.Load();     // must run before LoadAll so overrides apply at registration

        if (ResetCosmeticCustomizer.Value)
        {
            PerCosmeticOverrides.ResetAll();
            ResetCosmeticCustomizer.Value = false;
            Logger.LogInfo("CosmeticCustomizer: all per-cosmetic overrides cleared.");
        }

        HhhCosmeticLoader.LoadAll();
        PerCosmeticColors.Load();
        IconCacheCleaner.Run();          // honor DeleteIconCache flag if set
        _harmony.PatchAll();

        AllowMultipleCosmetics.SettingChanged += OnAllowMultipleCosmeticsChanged;

        if (HideMoreHeadButton.Value)
            TryHideMoreHeadUI();

        PartShrinkerSuppressor.TryApply(_harmony);
        SetupCosmeticsModdedRpcPatch.TryApply(_harmony);
    }

    // Called when AllowMultipleCosmetics is toggled at runtime.
    // When turning OFF: trims cosmeticEquipped to at most one per type so remote
    // players receive the correct single-equip state in the next RPC.
    // When turning ON: no trimming needed — just re-sync so remote sees the current list.
    private static void OnAllowMultipleCosmeticsChanged(object sender, System.EventArgs e)
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        if (!AllowMultipleCosmetics.Value)
        {
            // Keep only the first equipped cosmetic of each type; remove the extras.
            var seen     = new System.Collections.Generic.HashSet<SemiFunc.CosmeticType>();
            var toRemove = new System.Collections.Generic.List<int>();

            foreach (int idx in meta.cosmeticEquipped)
            {
                if (idx < 0 || idx >= meta.cosmeticAssets.Count) continue;
                var asset = meta.cosmeticAssets[idx];
                if (asset == null) continue;
                if (!seen.Add(asset.type))
                    toRemove.Add(idx);
            }

            foreach (int idx in toRemove)
                meta.cosmeticEquipped.Remove(idx);
        }

        // Re-sync cosmetics to remote players (no-op in singleplayer).
        meta.CosmeticPlayerUpdateLocal(_synced: SemiFunc.IsMultiplayer());
    }

    private void TryHideMoreHeadUI()
    {
        try
        {
            var uiType = AccessTools.TypeByName("MoreHead.MoreHeadUI");
            if (uiType == null)
            {
                Logger.LogDebug("HideMoreHeadButton=true but MoreHead is not loaded — skipping.");
                return;
            }

            var initMethod = AccessTools.Method(uiType, "Initialize");
            if (initMethod == null) return;

            var prefix = typeof(HideMoreHeadUIPatch).GetMethod(
                nameof(HideMoreHeadUIPatch.SkipInitialize),
                BindingFlags.Static | BindingFlags.NonPublic);

            _harmony.Patch(initMethod, prefix: new HarmonyMethod(prefix));
            Logger.LogInfo("MoreHead UI hidden (HideMoreHeadButton=true).");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not hide MoreHead UI: {ex.Message}");
        }
    }

    private static void PrintBanner()
    {
        if (BceConsole.IsAvailable)
        {
            BceConsole.WriteLine("══════════════════════════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
            BceConsole.Write("[Info   :  MoreHead Bridge] ", ConsoleColor.Cyan);
            BceConsole.WriteLine("► MoreHead Bridge v" + MyPluginInfo.PLUGIN_VERSION + " by Xuaun", ConsoleColor.DarkCyan);
            BceConsole.Write("[Info   :  MoreHead Bridge] ", ConsoleColor.Cyan);
            BceConsole.WriteLine("  Translating .hhh cosmetics into vanilla REPO", ConsoleColor.DarkCyan);
            BceConsole.WriteLine("══════════════════════════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        }
        else
        {
            Logger.LogInfo("MoreHead Bridge v" + MyPluginInfo.PLUGIN_VERSION + " by Xuaun");
        }
    }
}
