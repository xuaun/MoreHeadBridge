using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using UnityEngine;

namespace MoreHeadBridge;


[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("REPOLib")]
[BepInDependency("space.customizing.console", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Mhz.REPOMoreHead", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; } = null!;
    public new static ManualLogSource Logger { get; private set; } = null!;

    public static bool MenuLibAvailable { get; private set; }

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;

        BridgePaths.Init();

        BindConfig();

        MenuLibAvailable = BepInEx.Bootstrap.Chainloader.PluginInfos
                                  .ContainsKey("nickklmao.menulib");
        if (MenuLibAvailable)
            BridgeLog.Trace("MenuLib detected — CosmeticCustomizer UI enabled");

        PrintBanner();
        PartShrinkerSuppressor.InstallWarningFilter();
        PartShrinkerSuppressor.InstallNativeWarningFilter(_harmony);

        CustomizerStore.Load();

        if (ResetCosmeticCustomizer.Value)
        {
            CustomizerStore.ResetAll();
            ResetCosmeticCustomizer.Value = false;
            BceConsole.LogInfo("CosmeticCustomizer: all per-cosmetic overrides cleared", ConsoleColor.Magenta);
        }

        if (ImportOverrides.Value)
        {
            CustomizerIO.ImportMerge();
            ImportOverrides.Value = false;
        }

        HhhCosmeticLoader.LoadAll();
        // Emit a single [Warning] + per-GO [Debug] lines for the PartShrinker "missing script" warnings suppressed during LoadAll().
        PartShrinkerSuppressor.FlushSuppressedLog();

        // Register the synthetic "Mini-Semibot" world cosmetic (not from a .hhh) right after the real ones so it joins the WORLD tab and syncs through the normal pipeline.
        MiniSemibotCosmetic.Register();

        if (ExportOverrides.Value)
        {
            CustomizerIO.ExportAll();
            ExportOverrides.Value = false;
        }
        PerCosmeticColors.Load();
        IconCacheCleaner.Run();
        BridgePatcher.ApplyAll(_harmony);

        WireConfigHandlers();

        // Sync channels (overrides, mini, colours, mimic audio) ride BridgeNetMux — it hooks Photon at RunManager.Awake via BridgeNetMuxSubscribePatch (BridgePatcher), NOT here (too early breaks the lobby).

        PartShrinkerSuppressor.TryApply(_harmony);
        SetupCosmeticsModdedRpcPatch.TryApply(_harmony);
        REPOLibRpcOrderPatch.TryApply(_harmony);
        CustomGrabColorCompat.TryApply(_harmony);
        if (MenuLibAvailable)
            HideMoreHeadButtonCreatePatch.TryApply(_harmony);
    }

    // Subscribes the live-apply reactions for every config toggle that takes effect without a restart.
    private void WireConfigHandlers()
    {
        AllowMultipleCosmetics.SettingChanged += OnAllowMultipleCosmeticsChanged;
        EnableMiniSemibot.SettingChanged += (_, _) => OnEnableMiniSemibotChanged();
        HideMoreHeadButton.SettingChanged += (_, _) => RuntimeConfigApplier.HideMoreHeadButtonsSoon();
        HideMoreHeadDecorations.SettingChanged += (_, _) => MoreHeadHideSync.ApplyLocalAndBroadcast();
        UseTextureAsPlaceholder.SettingChanged += (_, _) => ClearBridgeIconsWithoutCapture();
        EnablePerCosmeticColors.SettingChanged += (_, _) =>
        {
            RuntimeConfigApplier.RefreshColorAnimations();          // strip/attach animators + invalidate local mini death heads
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();  // fresh materials → ON re-applies overrides, OFF reverts to vanilla palette
            RuntimeConfigApplier.RefreshCosmeticsMenu();
            // Unconditional snapshot: BroadcastAll no-ops when the master is OFF, but the OFF state must
            // still reach remotes (the colour Build*Section methods send "" = explicit clear).
            if (SemiFunc.IsMultiplayer()) BridgeNetMux.BroadcastSnapshot();
        };
        EnableBridgeTinting.SettingChanged += (_, _) =>
        {
            HhhCosmeticLoader.RefreshTintableFlags();
            // Full re-instantiation: BridgeTintMaterial is injected at equip time, so toggling tinting requires cosmetics to be re-spawned to pick up (or drop) the component.
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
            // The toggle changes the Overrides section (BuildSection sends Tintable=false per equipped
            // bridge cosmetic while OFF) — broadcast so remotes re-render with the owner's choice.
            if (SemiFunc.IsMultiplayer()) BridgeNetMux.BroadcastSnapshot();
        };
        EnableBridgeColorAnimations.SettingChanged += (_, _) =>
        {
            RuntimeConfigApplier.RefreshColorAnimations();
            RuntimeConfigApplier.ReapplyLocalCosmeticColors();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        SeeRemoteColorAnimations.SettingChanged += (_, _) =>
            RuntimeConfigApplier.RefreshRemoteColorAnimations();
        void OnCustomColorToggle()
        {
            RuntimeConfigApplier.ReapplyLocalCosmeticColors();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
            PerCosmeticColorNetworkSync.BroadcastAll();
        }
        EnableBridgeCustomColors.SettingChanged  += (_, _) => OnCustomColorToggle();
        EnableVanillaCustomColors.SettingChanged += (_, _) => OnCustomColorToggle();
        EnableModdedCustomColors.SettingChanged  += (_, _) => OnCustomColorToggle();

        HighlightBridgeCosmetics.SettingChanged += (_, _) => RuntimeConfigApplier.RefreshCosmeticsMenu();
        HighlightModdedCosmetics.SettingChanged += (_, _) => RuntimeConfigApplier.RefreshCosmeticsMenu();
        BridgeDefaultRarity.SettingChanged += (_, _) =>
        {
            HhhCosmeticLoader.RefreshDefaultRarity();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        ImportOverrides.SettingChanged += (_, _) =>
        {
            if (!ImportOverrides.Value) return;
            CustomizerIO.ImportMerge();
            ImportOverrides.Value = false;
            Config.Save();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        ExportOverrides.SettingChanged += (_, _) =>
        {
            if (!ExportOverrides.Value) return;
            CustomizerIO.ExportAll();
            ExportOverrides.Value = false;
            Config.Save();
        };

        AutoUnlockBridgeCosmetics.SettingChanged += (_, _) =>
        {
            if (!AutoUnlockBridgeCosmetics.Value) return;
            UnlockPatch.RunAutoUnlockNow();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        AutoUnlockModdedCosmetics.SettingChanged += (_, _) =>
        {
            if (!AutoUnlockModdedCosmetics.Value) return;
            UnlockPatch.RunAutoUnlockModdedNow();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        ResetBridgeUnlocks.SettingChanged += (_, _) =>
        {
            if (!ResetBridgeUnlocks.Value) return;
            UnlockPatch.RunResetNow();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        ResetModdedUnlocks.SettingChanged += (_, _) =>
        {
            if (!ResetModdedUnlocks.Value) return;
            UnlockPatch.RunResetModdedNow();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
        };
        ResetCosmeticCustomizer.SettingChanged += (_, _) =>
        {
            if (!ResetCosmeticCustomizer.Value) return;
            CustomizerStore.ResetAll();
            var meta = MetaManager.instance;
            if (meta != null)
            {
                foreach (var asset in meta.cosmeticAssets)
                {
                    if (asset == null || !BridgeIds.IsBridgeAsset(asset)) continue;
                    HhhCosmeticLoader.ReapplyDefaults(asset);
                }
            }
            ResetCosmeticCustomizer.Value = false;
            Config.Save();
            RuntimeConfigApplier.ReinstantiateAllLocalCosmetics();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
            BceConsole.LogInfo("CosmeticCustomizer: all per-cosmetic overrides cleared (ingame)", ConsoleColor.Magenta);
        };
    }

    private void OnApplicationQuit()
    {
        CustomizerStore.FlushPendingWrites();
        PerCosmeticColors.FlushPendingWrites();
        BridgeFavoritesManager.FlushPendingWrites();
    }

    // Both directions take effect immediately. OFF: unequip the Mini-Semibot (the world pipeline despawns it + syncs the loadout; the asset stays registered — REPOLib has no unregister). ON: REPOLib supports LATE registration, so a session started disabled can still enable it live.
    private static void OnEnableMiniSemibotChanged()
    {
        if (EnableMiniSemibot.Value)
        {
            // No-op when already registered this session (OFF only unequips, never unregisters).
            MiniSemibotCosmetic.Register();
            if (AutoUnlockBridgeCosmetics.Value) UnlockPatch.RunAutoUnlockNow();
            RuntimeConfigApplier.RefreshCosmeticsMenu();
            return;
        }

        var meta = MetaManager.instance;
        var asset = MiniSemibotCosmetic.Asset;
        if (meta == null || asset == null) return;

        int idx = meta.cosmeticAssets.IndexOf(asset);
        if (idx < 0) return;
        bool removed = meta.cosmeticEquipped.Remove(idx);
        removed |= meta.cosmeticEquippedPreview.Remove(idx);
        if (removed)
            meta.CosmeticPlayerUpdateLocal(_synced: SemiFunc.IsMultiplayer());
    }

    private static void OnAllowMultipleCosmeticsChanged(object sender, System.EventArgs e)
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        // Apply/restore the native canEquipMultiple flags for the multi-equip types.
        MultiEquipTypeFlags.Sync();

        if (!AllowMultipleCosmetics.Value)
        {
            var seen = new System.Collections.Generic.HashSet<SemiFunc.CosmeticType>();
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

        meta.CosmeticPlayerUpdateLocal(_synced: SemiFunc.IsMultiplayer());
    }

    private static void ClearBridgeIconsWithoutCapture()
    {
        var meta = MetaManager.instance;
        if (meta == null) return;

        foreach (var asset in meta.cosmeticAssets)
        {
            if (asset == null || !BridgeIds.IsBridgeAsset(asset)) continue;
            if (IconCapture.HasCache(asset)) continue; // keep real captured icons
            asset.icon = null;
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
            BceConsole.LogInfo("MoreHead Bridge v" + MyPluginInfo.PLUGIN_VERSION + " by Xuaun");
        }
    }
}
