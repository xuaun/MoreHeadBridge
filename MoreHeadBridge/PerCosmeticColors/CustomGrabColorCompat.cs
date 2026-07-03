// ============================================================================
// Soft-compat for CustomGrabColor (games.enchanted.CustomGrabColour): our custom RGB on the grabber arm (ArmRightMesh, slot 13) lives outside colorsEquipped[]/MetaManager.colors, so its "Match Skin" beam (which reads the grabber colour as a PALETTE index) never sees our RGB.
// Fix: reflectively postfix whichever colour-resolution method the installed version exposes so the LOCAL beam returns our RGB. Original DLL: GetGrabberCosmeticColour(Color) + GrabBeamUtil refresh; fork: GetAvatarColour(AvatarColourSource, Color) + UpdateBeamColourForAllBeams(). All reflection, no compile-time ref; no-ops if absent or API changed.
// ============================================================================

using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CustomGrabColorCompat
{
    private static readonly int GrabberSlot = (int)SemiFunc.CosmeticType.ArmRightMesh; // 13 (== AvatarColourSource.Grabber)
    private const BindingFlags PubStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PubInst   = BindingFlags.Public | BindingFlags.Instance;

    private static FieldInfo? _playerField;     // CustomGrabBeamColour.player

    // Live-refresh entry points — version-dependent; whichever exists is used (original preferred).
    private static MethodInfo? _trySendAll;     // ORIGINAL: GrabBeamUtil.TrySendBeamColourUpdateForAllBeams(PlayerAvatar)
    private static MethodInfo? _updateAllBeams; // FORK:     CustomGrabBeamColour.UpdateBeamColourForAllBeams()

    // Wired from Plugin after PatchAll. No-ops unless CustomGrabColor is installed.
    internal static void TryApply(Harmony harmony)
    {
        if (!MiniSemibotModCompat.HasCustomGrabColor) return;
        try
        {
            var asm = FindAssembly("CustomGrabColour");
            if (asm == null) return;

            var beamType = asm.GetType("CustomGrabColour.PlayerGrabBeam.CustomGrabBeamColour");
            if (beamType == null) return;

            _playerField = beamType.GetField("player", PubInst);
            if (_playerField == null) return;

            // Live-refresh handles: the original ships GrabBeamUtil; the fork folds it into UpdateBeamColourForAllBeams.
            var utilType    = asm.GetType("CustomGrabColour.PlayerGrabBeam.GrabBeamUtil");
            _trySendAll     = utilType?.GetMethod("TrySendBeamColourUpdateForAllBeams", PubStatic);
            _updateAllBeams = beamType.GetMethod("UpdateBeamColourForAllBeams", PubStatic, null, Type.EmptyTypes, null);

            // ORIGINAL: public Color GetGrabberCosmeticColour(Color)
            var original = beamType.GetMethod("GetGrabberCosmeticColour", PubInst, null, new[] { typeof(Color) }, null);
            if (original != null)
            {
                var postfix = new HarmonyMethod(typeof(CustomGrabColorCompat)
                    .GetMethod(nameof(GrabberColourPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(original, postfix: postfix);
                BceConsole.LogInfo("CustomGrabColor compat: grabber colour bridge active (GetGrabberCosmeticColour).", ConsoleColor.Cyan);
            }

            // FORK: public Color GetAvatarColour(AvatarColourSource, Color)
            var fork = beamType.GetMethod("GetAvatarColour", PubInst);
            if (fork != null)
            {
                var postfix = new HarmonyMethod(typeof(CustomGrabColorCompat)
                    .GetMethod(nameof(GetAvatarColourPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(fork, postfix: postfix);
                BceConsole.LogInfo("CustomGrabColor compat: grabber colour bridge active (GetAvatarColour).", ConsoleColor.Cyan);
            }

            if (original == null && fork == null)
                BceConsole.LogWarning("CustomGrabColor compat: no known grabber-colour method found — version unsupported.");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"CustomGrabColor compat patch failed (harmless): {ex.Message}");
        }
    }

    // ORIGINAL path: the Match-Skin beam asks for the grabber colour here.
    private static void GrabberColourPostfix(object __instance, ref Color __result)
    {
        TrySubstituteLocalCustom(__instance, ref __result);
    }

    // FORK path: GetAvatarColour(source, fallback). Substitute only for the Grabber source (slot 13), leaving ArmRight alone. __0 is the AvatarColourSource enum (taken as object → boxed).
    private static void GetAvatarColourPostfix(object __instance, object __0, ref Color __result)
    {
        try { if (Convert.ToInt32(__0) != GrabberSlot) return; }
        catch { return; }
        TrySubstituteLocalCustom(__instance, ref __result);
    }

    // Shared: if the LOCAL player has a custom RGB grabber, push it into the beam result (keep their alpha).
    private static void TrySubstituteLocalCustom(object beamInstance, ref Color result)
    {
        try
        {
            if (!Plugin.EnableVanillaCustomColors.Value) return;
            if (_playerField?.GetValue(beamInstance) is not PlayerAvatar player || !player.isLocal) return;
            if (PerCosmeticColors.TryGetCustomColor(VanillaTintHelper.BaseMeshAssetId(GrabberSlot), out var custom))
                result = new Color(custom.r, custom.g, custom.b, result.a);
        }
        catch { /* never break their beam */ }
    }

    // Pushes a live beam refresh through whichever update path the installed version exposes, called on local colour re-apply so the beam updates immediately.
    internal static void RefreshLocalBeam()
    {
        try
        {
            if (_trySendAll != null && PlayerAvatar.instance != null)
                _trySendAll.Invoke(null, new object[] { PlayerAvatar.instance });
            else
                _updateAllBeams?.Invoke(null, null);
        }
        catch { /* harmless */ }
    }

    private static Assembly? FindAssembly(string name)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return a;
        return null;
    }
}
