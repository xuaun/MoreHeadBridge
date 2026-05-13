using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("REPOLib")]
[BepInDependency("space.customizing.console", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Mhz.REPOMoreHead", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; } = null!;
    public new static ManualLogSource Logger { get; private set; } = null!;

    public static ConfigEntry<bool> UnlockAll { get; private set; } = null!;

    public static ConfigEntry<bool> ResetUnlocks { get; private set; } = null!;

    // [MenuCapture] — use cosmetic texture as icon overlay on the placeholder.
    public static ConfigEntry<bool> UseTextureAsPlaceholder { get; private set; } = null!;

    // [MenuCapture] — reactive hover capture.
    public static ConfigEntry<bool> AutoCaptureIcons { get; private set; } = null!;

    // [MenuCapture] — one-shot batch.
    public static ConfigEntry<bool> GenerateAllIcons { get; private set; } = null!;

    // [MenuCapture] Cache deletion.
    public static ConfigEntry<bool> DeleteIconCache { get; private set; } = null!;
    public static ConfigEntry<string> DeleteIconsMatching { get; private set; } = null!;

    public static ConfigEntry<bool> HideMoreHeadButton { get; private set; } = null!;


    // Rarity assigned to bridge cosmetics in the vanilla cosmetics shop.
    // Common is the default — Uncommon/Rare/UltraRare make them appear in higher tiers.
    public static ConfigEntry<SemiFunc.Rarity> DefaultRarity { get; private set; } = null!;

    // Comma-separated list of subfolder names (under BepInEx/plugins) to scan for .hhh files.
    // Empty = scan ALL plugin folders (default). Use this to select only wanted folders.
    public static ConfigEntry<string> SpecificFolders { get; private set; } = null!;

    public static ConfigEntry<bool> ShowBridgeDebugLogs { get; private set; } = null!;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

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

        HideMoreHeadButton = Config.Bind(
            section: "General",
            key: "HideMoreHeadButton",
            defaultValue: false,
            description: "If true, removes the MoreHead button from all menus so you can use only the vanilla cosmetics UI. Requires restart."
        );

        DefaultRarity = Config.Bind(
            section: "General",
            key: "DefaultRarity",
            defaultValue: SemiFunc.Rarity.Common,
            description: "Rarity tier assigned to bridge cosmetics in the vanilla shop. Values: Common, Uncommon, Rare, UltraRare."
        );

        SpecificFolders = Config.Bind(
            section: "General",
            key: "SpecificFolders",
            defaultValue: "",
            description: "Comma-separated subfolder names under BepInEx/plugins to scan for .hhh files. Empty = scan all. " +
                          "Example: 'Some-MoreHeadPack,Another-CosmeticsPack'. Matching is case-insensitive and uses path contains."
        );

        // [MenuCapture] BEGIN — icon-from-menu config.
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
                          "  %userprofile%\\AppData\\LocalLow\\semiwork\\REPO\\MoreHeadBridge_Icons\\\n" +
                          "Delete that folder to wipe all generated icons.\n" +
                          "(We store icons OUTSIDE the vanilla cosmetics cache because REPOLib\n" +
                          " wipes that one on every launch for any non-vanilla cosmetic.)"
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

        DeleteIconCache = Config.Bind(
            section: "Icons",
            key: "DeleteIconCache",
            defaultValue: false,
            description: "ONE-SHOT trigger. When TRUE on launch, delete cached bridge icon PNGs from:\n" +
                          "  %userprofile%\\AppData\\LocalLow\\semiwork\\REPO\\MoreHeadBridge_Icons\\\n" +
                          "Use DeleteIconsMatching to filter which ones to delete.\n" +
                          "Auto-resets to FALSE after running."
        );

        DeleteIconsMatching = Config.Bind(
            section: "Icons",
            key: "DeleteIconsMatching",
            defaultValue: "",
            description: "Optional comma-separated filter for DeleteIconCache. Case-insensitive\n" +
                          "substring match against the icon filename (which is the cosmetic's internal name).\n" +
                          "Empty = delete ALL bridge icons.\n" +
                          "Example: 'PirateHat,Waluigi' deletes only icons whose name contains either."
        );
        // [MenuCapture] END

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

        ShowBridgeDebugLogs = Config.Bind(
            section: "Debug",
            key: "ShowBridgeDebugLogs",
            defaultValue: false,
            description: "If true, do NOT suppress NullReferenceExceptions for bridge cosmetics.\n" +
                          "Use this to diagnose bridge-only issues (will spam logs if the base game is noisy)."
        );

        PrintBanner();
        HhhCosmeticLoader.LoadAll();
        IconCacheCleaner.Run();          // honor DeleteIconCache flag if set
        _harmony.PatchAll();

        if (HideMoreHeadButton.Value)
            TryHideMoreHeadUI();

        PartShrinkerSuppressor.TryApply(_harmony);
        ModdedRpcRetrigger.TryApply(_harmony);
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
