// Bridges to MoreHeadUtilities' part-hiding system via reflection; a complete no-op when MoreHeadUtilities isn't loaded.

using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static class PartShrinkerBridge
{
    private static bool _initialized;
    private static bool _available;
    private static Type? _shrinkerType;
    private static Type? _hiddenType;
    private static FieldInfo? _partField;
    private static FieldInfo? _hideChildrenField;
    private static MethodInfo? _addMethod;
    private static MethodInfo? _removeMethod;
    private static FieldInfo? _hiddenPartsListField;
    private static MethodInfo? _updateMethod;

    // Cached HiddenParts.Part enum values used to detect eye-hiding PartShrinkers.
    private static object? _eyeLeftValue;
    private static object? _eyeRightValue;

    // HiddenParts.Part values with a mesh-SWITCH counterpart → that swap CosmeticType, so a part-hide can mirror onto the swap mesh in the same slot (see EnforceSwapHiding).
    private static readonly System.Collections.Generic.List<(object part, SemiFunc.CosmeticType type)> _swapMap = new();

    private static void AddSwapMap(Type partEnum, string name, SemiFunc.CosmeticType type)
    {
        try { _swapMap.Add((Enum.Parse(partEnum, name), type)); }
        catch { /* part name absent in this MoreHeadUtilities version — skip it */ }
    }

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _shrinkerType = MoreHeadUtilitiesTypes.FindType("MoreHeadUtilities.PartShrinker");
            _hiddenType   = MoreHeadUtilitiesTypes.FindType("MoreHeadUtilities.HiddenParts");
            if (_shrinkerType == null || _hiddenType == null)
            {
                BridgeLog.Trace("MoreHeadUtilities not loaded — PartShrinker bridge inactive");
                return;
            }

            _partField         = AccessTools.Field(_shrinkerType, "partToHide");
            _hideChildrenField = AccessTools.Field(_shrinkerType, "hideChildren");
            _addMethod         = AccessTools.Method(_hiddenType, "AddHiddenPart");
            _removeMethod      = AccessTools.Method(_hiddenType, "RemoveHiddenPart");
            _hiddenPartsListField = AccessTools.Field(_hiddenType, "hiddenParts");
            _updateMethod         = AccessTools.Method(_hiddenType, "UpdateHiddenParts");

            // Cache EyeLeft / EyeRight enum values for eye-condition injection.
            var partEnum = _hiddenType.GetNestedType("Part");
            if (partEnum != null)
            {
                _eyeLeftValue  = Enum.Parse(partEnum, "EyeLeft");
                _eyeRightValue = Enum.Parse(partEnum, "EyeRight");

                // Body parts that ALSO have a mesh-switch slot — hiding them must hide the swap mesh too. Health / eyes / pupils have no mesh-switch counterpart, so they're omitted.
                _swapMap.Clear();
                AddSwapMap(partEnum, "LeftArm",  SemiFunc.CosmeticType.ArmLeftMesh);
                AddSwapMap(partEnum, "RightArm", SemiFunc.CosmeticType.ArmRightMesh);
                AddSwapMap(partEnum, "LeftLeg",  SemiFunc.CosmeticType.LegLeftMesh);
                AddSwapMap(partEnum, "RightLeg", SemiFunc.CosmeticType.LegRightMesh);
                AddSwapMap(partEnum, "Head",     SemiFunc.CosmeticType.HeadTopMesh);
                AddSwapMap(partEnum, "Neck",     SemiFunc.CosmeticType.HeadBottomMesh);
                AddSwapMap(partEnum, "Body",     SemiFunc.CosmeticType.BodyTopMesh);
                AddSwapMap(partEnum, "Hips",     SemiFunc.CosmeticType.BodyBottomMesh);
            }

            _available = _partField != null && _hideChildrenField != null
                       && _addMethod != null && _removeMethod != null;

            if (_available)
                BceConsole.LogInfo("PartShrinker bridge loaded");
            else
                BceConsole.LogWarning("PartShrinker types found but reflection failed — disabled");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"PartShrinker bridge init error: {ex.Message}");
        }
    }

    // Rebuilds HiddenParts from the PartShrinkers STILL mounted, discarding stale hides from cosmetics removed without Cosmetic.Remove (icon-generation preview swap) — so a hidden eye from the real loadout doesn't bleed into other cosmetics' icons.
    internal static void ResyncFromMountedCosmetics(PlayerAvatarVisuals? avatar)
    {
        EnsureInit();
        if (!_available || avatar == null || _hiddenType == null) return;

        var hp = avatar.GetComponent(_hiddenType);
        if (hp == null) return; // nothing was ever hidden on this avatar

        try
        {
            // Re-add ONLY from shrinkers on ACTIVE cosmetics — includeInactive=false is deliberate: non-preview cosmetics are deactivated, not destroyed, and their hides must NOT apply.
            if (_hiddenPartsListField?.GetValue(hp) is System.Collections.IList list)
                list.Clear();

            foreach (var shrinker in avatar.GetComponentsInChildren(_shrinkerType!, false))
            {
                if (shrinker == null) continue;
                object part    = _partField!.GetValue(shrinker);
                bool hideChild = (bool)_hideChildrenField!.GetValue(shrinker);
                _addMethod!.Invoke(hp, new object[] { part, hideChild, false });
            }

            _updateMethod?.Invoke(hp, null);
            EnforceSwapHiding(avatar);
        }
        catch (Exception ex)
        {
            BridgeLog.Trace($"PartShrinker resync failed: {ex.Message}");
        }
    }

    // A mesh-SWITCH cosmetic replaces a body part's base mesh with a differently-named swap mesh that HiddenParts (fixed mesh names) can't see, so a part-shrinker on that slot stops hiding once swapped. Mirror each active hide onto the swap meshes in that slot, restoring them when the part is shown again. Event-driven (shrinker add/remove + mesh-switch setup/remove), no per-frame cost; safe no-op when MoreHeadUtilities is absent or nothing is hidden.
    internal static void EnforceSwapHiding(PlayerAvatarVisuals? avatar)
    {
        EnsureInit();
        if (!_available || avatar == null || _hiddenType == null) return;

        var pc = avatar.playerCosmetics;
        if (pc == null || pc.cosmeticParents == null) return;

        // Restore last pass first, so a part that's no longer hidden frees its swap meshes.
        var tracker = avatar.GetComponent<BridgeSwapMeshHider>();
        tracker?.Restore();

        var hp = avatar.GetComponent(_hiddenType);
        if (hp == null) return;
        if (_hiddenPartsListField?.GetValue(hp) is not System.Collections.IList hidden || hidden.Count == 0)
            return;

        foreach (var entry in hidden)
        {
            if (entry == null) continue;

            SemiFunc.CosmeticType type = default;
            bool mapped = false;
            foreach (var m in _swapMap)
                if (entry.Equals(m.part)) { type = m.type; mapped = true; break; }
            if (!mapped) continue;   // this hidden part has no mesh-switch slot

            var cp = pc.cosmeticParents.Find(x => x != null && x.cosmeticType == type);
            if (cp == null) continue;

            tracker ??= avatar.gameObject.AddComponent<BridgeSwapMeshHider>();
            HideSwapRenderers(cp, tracker);
        }
    }

    // Disables the ENABLED swap-mesh renderers under a slot's baseMeshParents, skipping the vanilla base mesh (HiddenParts owns that, already disabled when a swap is present).
    private static void HideSwapRenderers(PlayerCosmetics.CosmeticParent cp, BridgeSwapMeshHider tracker)
    {
        if (cp.baseMeshParents == null) return;
        foreach (var parent in cp.baseMeshParents)
        {
            if (parent == null) continue;
            foreach (var r in parent.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;     // already hidden (base mesh / other) → leave it
                if (IsBaseMesh(cp, r.transform)) continue;  // never touch the vanilla base mesh
                tracker.Track(r);                            // a swap mesh in the hidden slot → hide it
            }
        }
    }

    private static bool IsBaseMesh(PlayerCosmetics.CosmeticParent cp, Transform t)
    {
        if (cp.baseMeshes == null) return false;
        foreach (var bm in cp.baseMeshes)
            if (bm != null && (t == bm || t.IsChildOf(bm))) return true;
        return false;
    }

    // Enables/disables EVERY HiddenParts component in the scene — used by the icon batch to freeze part-hiding during capture so a forced eye state can't be re-hidden by HiddenParts.LateUpdate.
    internal static void SetAllHiddenPartsEnabled(bool enabled)
    {
        EnsureInit();
        if (_hiddenType == null) return;
        foreach (var o in UnityEngine.Object.FindObjectsOfType(_hiddenType))
            if (o is Behaviour b) b.enabled = enabled;
    }

    internal static void OnSpawn(GameObject cosmetic, PlayerAvatarVisuals? avatar, PlayerCosmetics? pc = null)
        => Apply(cosmetic, avatar, pc, isAdd: true);

    internal static void OnRemove(GameObject cosmetic, PlayerAvatarVisuals? avatar, PlayerCosmetics? pc = null)
        => Apply(cosmetic, avatar, pc, isAdd: false);

    private static void Apply(GameObject cosmetic, PlayerAvatarVisuals? avatar, PlayerCosmetics? pc, bool isAdd)
    {
        EnsureInit();
        if (_shrinkerType == null || cosmetic == null) return;

        Component[] shrinkers;
        try { shrinkers = cosmetic.GetComponentsInChildren(_shrinkerType, true); }
        catch (System.Exception ex) { BridgeLog.Debug($"PartShrinker: component scan failed — {ex.Message}"); return; }
        if (shrinkers == null || shrinkers.Length == 0) return;

        // Build (or find) the HiddenParts component on the avatar root; skipped gracefully when avatar is null (e.g. death-head cosmetics).
        Component? hp = null;
        if (_available && avatar != null)
        {
            hp = avatar.GetComponent(_hiddenType!);
            if (hp == null)
            {
                try { hp = avatar.gameObject.AddComponent(_hiddenType!); }
                catch (Exception ex)
                {
                    BridgeLog.Trace($"Could not add HiddenParts: {ex.Message}");
                }
            }
        }

        var method = isAdd ? _addMethod : _removeMethod;
        bool needsEyeLeft  = false;
        bool needsEyeRight = false;

        foreach (var shrinker in shrinkers)
        {
            if (shrinker == null) continue;
            try
            {
                if (hp != null && method != null)
                {
                    object part    = _partField!.GetValue(shrinker);
                    bool hideChild = (bool)_hideChildrenField!.GetValue(shrinker);
                    method.Invoke(hp, new object[] { part, hideChild, true });

                    // Track whether eye parts are being added so we can inject EyeLeft_Disable / EyeRight_Disable conditions below.
                    if (isAdd)
                    {
                        if (_eyeLeftValue  != null && part.Equals(_eyeLeftValue))  needsEyeLeft  = true;
                        if (_eyeRightValue != null && part.Equals(_eyeRightValue)) needsEyeRight = true;
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Trace($"PartShrinker {(isAdd ? "Add" : "Remove")} failed: {ex.Message}");
            }
            finally
            {
                // ALWAYS disable in finally, even if AddHiddenPart failed: PartShrinker.Update() walks up to "ANIM BOT", which doesn't exist in menu/death-head hierarchies → NRE.
                if (isAdd && shrinker is MonoBehaviour mb)
                    mb.enabled = false;
            }
        }

        // Eye parts hidden → inject EyeLeft/Right_Disable into the cosmetic's broadcaster so vanilla ConditionUpdateAll hides the eyelid cosmetics — the same mechanism vanilla hats use.
        if (isAdd && pc != null && (needsEyeLeft || needsEyeRight))
        {
            var broadcaster = cosmetic.GetComponent<BridgeCustomTypesBroadcaster>()
                           ?? cosmetic.AddComponent<BridgeCustomTypesBroadcaster>();
            broadcaster.OwnerPc = pc;

            if (needsEyeLeft
                && !broadcaster.Types.Contains(CosmeticCustomCondition.Type.EyeLeft_Disable))
            {
                broadcaster.Types.Add(CosmeticCustomCondition.Type.EyeLeft_Disable);
                pc.ConditionCustomSet(CosmeticCustomCondition.Type.EyeLeft_Disable);
            }
            if (needsEyeRight
                && !broadcaster.Types.Contains(CosmeticCustomCondition.Type.EyeRight_Disable))
            {
                broadcaster.Types.Add(CosmeticCustomCondition.Type.EyeRight_Disable);
                pc.ConditionCustomSet(CosmeticCustomCondition.Type.EyeRight_Disable);
            }
        }

        // Mirror the (un)hide onto any mesh-switch swap in a hidden slot — vanilla mesh names don't reach the swapped mesh, so a shrunk-but-swapped part would otherwise stay visible.
        EnforceSwapHiding(avatar);
    }
}
