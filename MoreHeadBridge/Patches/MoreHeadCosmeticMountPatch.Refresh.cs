// Live re-application of override data after a save, remote-player refresh/defer, and the owner-authoritative mesh-switch base-mesh reconcile. Partial of MoreHeadCosmeticMountPatch — see the core file for shared reflection handles and caches.

using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MoreHeadBridge;

internal static partial class MoreHeadCosmeticMountPatch
{
    // ── Live offset refresh (called after saving per-cosmetic offsets) ──────────

    // Reflection handles for accessing internal/private vanilla members.
    private static readonly FieldInfo? _cosmeticEquippedField =
        AccessTools.Field(typeof(PlayerCosmetics), "cosmeticEquipped");
    private static readonly FieldInfo? _cosmeticEquippedRawField =
        AccessTools.Field(typeof(PlayerCosmetics), "cosmeticEquippedRaw");
    private static readonly FieldInfo? _cosmeticAssetField =
        AccessTools.Field(typeof(Cosmetic), "cosmeticAsset");
    private static readonly MethodInfo? _conditionsSetupMethod =
        AccessTools.Method(typeof(PlayerCosmetics), "ConditionsSetup");
    private static readonly MethodInfo? _setupColorsLogicMethod =
        AccessTools.Method(typeof(PlayerCosmetics), "SetupColorsLogic");

    // CosmeticOffsetCondition captures its "resting" transform in Awake() — reset the transform to those defaults before destroying, so the replacement's Awake() captures the correct baseline.
    private static readonly FieldInfo? _positionDefaultField =
        AccessTools.Field(typeof(CosmeticOffsetCondition), "positionDefault");
    private static readonly FieldInfo? _rotationDefaultField =
        AccessTools.Field(typeof(CosmeticOffsetCondition), "rotationDefault");
    private static readonly FieldInfo? _scaleDefaultField =
        AccessTools.Field(typeof(CosmeticOffsetCondition), "scaleDefault");

    private static void ResetTransformToDefault(Transform t, CosmeticOffsetCondition[] conditions)
    {
        if (conditions.Length == 0) return;
        var first = conditions[0];
        if (_positionDefaultField?.GetValue(first) is Vector3 pos)
            t.localPosition = pos;
        if (_rotationDefaultField?.GetValue(first) is Vector3 rot)
            t.localEulerAngles = rot;
        if (_scaleDefaultField?.GetValue(first) is Vector3 scale)
            t.localScale = scale;
    }

    /// Forces a full rebuild (SetupCosmetics _forced + SetupColors) on every local
    /// PlayerCosmetics that has <paramref name="asset"/> equipped, so all our postfixes
    /// (mount, tint, offsets, sway) re-run with current override data. Call after SetAndApply.
    internal static void ReinstantiateCosmetic(CosmeticAsset asset)
    {
        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (!IsLocalPlayerCosmetics(pc)) continue;

            var equipped = _cosmeticEquippedField?.GetValue(pc) as List<Cosmetic>;
            if (equipped == null) continue;

            bool has = false;
            foreach (var c in equipped)
            {
                var ca = _cosmeticAssetField?.GetValue(c) as CosmeticAsset;
                if (c != null && ca == asset) { has = true; break; }
            }
            if (!has && pc.playerAvatarVisuals?.isMenuAvatar == true)
            {
                var meta = MetaManager.instance;
                if (meta != null)
                {
                    int idx = meta.cosmeticAssets.IndexOf(asset);
                    if (idx >= 0 && meta.cosmeticEquipped.Contains(idx))
                        has = true;
                    if (!has && meta.cosmeticPreviewEnabled
                        && meta.cosmeticEquippedPreview.Contains(idx))
                        has = true;
                }
            }
            if (!has) continue;

            // A local Mini-Semibot re-dresses from its OWN outfit source (rolled preset or live); vanilla SetupCosmetics on a menu avatar would read the MetaManager live loadout.
            if (MiniSemibotSpawner.TryRedressMini(pc)) continue;

            pc.SetupCosmetics(_synced: false, _forced: true);
            pc.SetupColors(_synced: false);
        }
    }

    /// Destroys any existing BridgeHideCondition (its OnDestroy restores the cosmetic's scale) and
    /// re-injects a fresh one when the config has rules. Shared by both RefreshLiveOffsets loops.
    private static void ReinjectHide(GameObject go, Cosmetic? cosmetic, CosmeticHideConfig? cfg)
    {
        foreach (var old in go.GetComponents<BridgeHideCondition>())
            Object.DestroyImmediate(old);
        if (cosmetic != null && cfg is { HasAny: true })
            go.AddComponent<BridgeHideCondition>().Init(cosmetic, cfg);
    }

    /// Destroys any existing BridgeLiveBlocked (its OnDestroy restores the pose) and re-injects a
    /// fresh one when the impact pose reacts while alive. Bridge cosmetics only.
    private static void ReinjectLiveBlocked(GameObject go, Cosmetic? cosmetic, DeathHeadFloorPose? pose)
    {
        foreach (var old in go.GetComponents<BridgeLiveBlocked>())
            Object.DestroyImmediate(old);
        if (cosmetic != null && pose is { ReactWhenAlive: true }
            && BridgeIds.IsBridgeAsset(cosmetic.cosmeticAsset))
            go.AddComponent<BridgeLiveBlocked>().Init(cosmetic, pose);
    }

    /// Tears down and re-injects offset/broadcaster components on every local PlayerCosmetics
    /// instance that currently has <paramref name="asset"/> equipped (main avatar, ESC preview,
    /// death-head, emotion-icon avatars).  Call after writing new override data.
    internal static void RefreshLiveOffsets(CosmeticAsset asset)
    {
        CustomizerStore.TryGet(asset.assetId, out var refreshData);
        // Take-over: suppress the native announce when an override record exists on a cosmetic with a native list.
        bool suppressNativeCustomTypes = refreshData != null
            && NativeCustomTypeImport.HasNativeAnnounceList(asset);

        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (!IsLocalPlayerCosmetics(pc)) continue;

            var equipped = _cosmeticEquippedField?.GetValue(pc) as List<Cosmetic>;
            if (equipped == null) continue;

            bool pcUpdated = false;
            foreach (var cosmetic in equipped)
            {
                if (cosmetic == null) continue;
                var ca = _cosmeticAssetField?.GetValue(cosmetic) as CosmeticAsset;
                if (ca != asset) continue;

                var existingConditions = cosmetic.GetComponents<CosmeticOffsetCondition>();
                ResetTransformToDefault(cosmetic.transform, existingConditions);
                foreach (var old in existingConditions)
                    Object.DestroyImmediate(old);
                foreach (var old in cosmetic.GetComponents<BridgeCustomTypesBroadcaster>())
                    Object.DestroyImmediate(old);

                InjectOffsetConditions(cosmetic.gameObject, asset, pc,
                    refreshData?.Offsets, refreshData?.CustomTypes, suppressNativeCustomTypes);
                ReinjectHide(cosmetic.gameObject, cosmetic, refreshData?.HideConditions);
                ReinjectLiveBlocked(cosmetic.gameObject, cosmetic, refreshData?.FloorPose);
                pcUpdated = true;
            }
            // Multi-equip extras (2nd, 3rd… of same type) live in cosmeticEquipped, so the loop above already covers them.

            if (pcUpdated)
                _conditionsSetupMethod?.Invoke(pc, null);
        }
    }

    /// Adds or removes BridgeSwaySpring on every local PlayerCosmetics instance that currently
    /// has <paramref name="asset"/> equipped, matching the current SwayMode override.
    /// Call after writing new override data (already done inside SetAndApply).
    internal static void RefreshLiveSway(CosmeticAsset asset)
    {
        var swayMode = CustomizerStore.GetEffectiveSway(asset.assetId);
        bool hasExplicitSway = swayMode.HasValue;
        bool shouldHaveBridgeSway = swayMode is SwayMode.Light or SwayMode.Moderate or SwayMode.Strong;
        float intensity = CosmeticSwayHelper.SwayModeToFactor(swayMode);

        foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>(includeInactive: true))
        {
            if (!IsLocalPlayerCosmetics(pc)) continue;

            var equipped = _cosmeticEquippedField?.GetValue(pc) as List<Cosmetic>;
            if (equipped == null) continue;

            foreach (var cosmetic in equipped)
            {
                if (cosmetic == null) continue;
                var ca = _cosmeticAssetField?.GetValue(cosmetic) as CosmeticAsset;
                if (ca != asset) continue;

                var nativeSprings = cosmetic.GetComponentsInChildren<CosmeticSprings>(includeInactive: true);

                // Sync native-spring state: suppress when override is active, restore on Default.
                foreach (var cs in nativeSprings)
                    cs.enabled = !hasExplicitSway;

                // BridgeSwaySpring is only valid with no native springs (bridge cosmetic) or an explicit sway override (native springs suppressed above).
                bool canHaveBridgeSpring = hasExplicitSway || nativeSprings.Length == 0;

                if (shouldHaveBridgeSway && canHaveBridgeSpring)
                {
                    var spring = cosmetic.GetComponent<BridgeSwaySpring>()
                                 ?? cosmetic.gameObject.AddComponent<BridgeSwaySpring>();
                    spring.Init(cosmetic, intensity);
                }
                else
                {
                    foreach (var old in cosmetic.GetComponents<BridgeSwaySpring>())
                        Object.DestroyImmediate(old);
                }
            }
        }
    }

    /// Re-instantiates a remote player's cosmetics so override changes show without them
    /// re-equipping. Uses their own cosmeticEquippedRaw (not the local player's), and our
    /// InstantiateCosmetic postfix reads the already-synced CustomizerSync._remote data.
    internal static void RefreshRemoteCosmetics(int actorNumber)
    {
        if (_cosmeticEquippedRawField == null)
        {
            BceConsole.LogWarning("RefreshRemoteCosmetics: cosmeticEquippedRaw reflection failed — cannot refresh");
            return;
        }

        // Refresh every genuine remote avatar for this actor (main + death head), using the same photonView resolution as the InstantiateCosmetic postfix so we never hit local/menu ones.
        int matched = 0;
        bool anyReady = false;
        foreach (var remotePC in Object.FindObjectsOfType<PlayerCosmetics>())
        {
            if (!IsRemoteAvatarForActor(remotePC, actorNumber)) continue;
            matched++;

            var rawEquipped = _cosmeticEquippedRawField.GetValue(remotePC) as List<int>;
            if (rawEquipped == null || rawEquipped.Count == 0) continue;
            anyReady = true;

            // SetupCosmeticsLogic directly, not SetupCosmetics(_synced:false): the latter hits REPOLib's prefix, which clears cosmeticEquipped and drops bridge cosmetics from refreshes.
            remotePC.SetupCosmeticsLogic(rawEquipped.ToArray(), _forced: true);

            // SetupCosmeticsLogic makes fresh BridgeTintMaterials but never colours them — re-apply type + per-cosmetic colours here or they're lost.
            remotePC.SetupColorsLogic(remotePC.colorsEquipped);
        }

        // No avatar ready yet (join / scene transition). Defer — data is cached in CustomizerSync._remote, and the retry covers the mid-game-change-with-no-equip case.
        if (matched == 0 || !anyReady)
            ScheduleDeferredRefresh(actorNumber);
    }

    // True for a genuine REMOTE avatar of actorNumber — same photonView resolution as TryGetRemoteActor so both classify an instance identically. Excludes the local player and menu avatars, EXCEPT the actor's Mini-Semibot (a menu-avatar clone whose overrides are owner-authoritative and must refresh with them).
    private static bool IsRemoteAvatarForActor(PlayerCosmetics pc, int actorNumber)
    {
        if (pc == null) return false;
        if (pc.playerAvatarVisuals != null && pc.playerAvatarVisuals.isMenuAvatar)
            return MiniSemibotSpawner.RemoteMiniActorOf(pc) == actorNumber;

        var pv = pc.deathHead && pc.deathHead.setup && pc.deathHead.playerAvatar
            ? pc.deathHead.playerAvatar.photonView
            : pc.photonView;
        if (pv == null || pv.IsMine) return false;
        return pv.Owner?.ActorNumber == actorNumber;
    }

    // Actors with a pending deferred refresh (avoids stacking multiple coroutines per actor).
    private static readonly HashSet<int> _pendingRefresh = new();

    /// Starts a single coroutine that waits for the remote avatar's cosmetics to be set up,
    /// then re-applies the latest cached override data. No-op if one is already pending for
    /// this actor or if there is no coroutine host.
    private static void ScheduleDeferredRefresh(int actorNumber)
    {
        if (!_pendingRefresh.Add(actorNumber)) return;
        if (Plugin.Instance == null)
        {
            _pendingRefresh.Remove(actorNumber);
            return;
        }
        Plugin.Instance.StartCoroutine(DeferredRefreshRoutine(actorNumber));
    }

    private static IEnumerator DeferredRefreshRoutine(int actorNumber)
    {
        // Poll until a genuine remote avatar for this actor is set up, the player leaves, or we time out.
        const float interval = 0.25f;
        float remaining = 10f;
        while (remaining > 0f)
        {
            yield return new WaitForSeconds(interval);
            remaining -= interval;

            bool found = false, ready = false;
            foreach (var pc in Object.FindObjectsOfType<PlayerCosmetics>())
            {
                if (!IsRemoteAvatarForActor(pc, actorNumber)) continue;
                found = true;
                var raw = _cosmeticEquippedRawField?.GetValue(pc) as List<int>;
                if (raw != null && raw.Count > 0) { ready = true; break; }
            }

            if (!found) break;       // avatar destroyed / player left
            if (!ready) continue;    // still not set up

            // A remote avatar is ready — re-apply the latest cached data (may have changed since the skip); RefreshRemoteCosmetics now takes the populated-rawEquipped path.
            _pendingRefresh.Remove(actorNumber);
            if (CustomizerSync.GetRemotePlayerData(actorNumber) != null)
                RefreshRemoteCosmetics(actorNumber);
            yield break;
        }
        _pendingRefresh.Remove(actorNumber);
    }

    private static bool IsLocalPlayerCosmetics(PlayerCosmetics pc)
    {
        // A remote player's Mini-Semibot is a menu-avatar clone but its overrides are owner-authoritative — the local refresh paths must never touch it.
        if (AvatarIdentity.IsRemoteMini(pc)) return false;
        // Menu avatars are always local (and their PhotonView may be uninitialized — don't trust photonView for them).
        if (pc.playerAvatarVisuals?.isMenuAvatar == true) return true;
        if (pc.photonView == null) return true; // singleplayer non-menu
        if (pc.photonView.IsMine) return true;
        return pc.deathHead != null && pc.deathHead.setup
            && pc.deathHead.playerAvatar?.photonView?.IsMine == true;
    }

    // Bone lookup cache: (avatarVisuals InstanceID, bone name) → Transform. Cleared wholesale by PurgeActor on any player leave so departed players don't linger.
    private static readonly Dictionary<(int, string), Transform?> _boneCache = new();

    /// Drops per-actor refresh bookkeeping and the bone cache when a player leaves the room.
    internal static void PurgeActor(int actorNumber)
    {
        _pendingRefresh.Remove(actorNumber);
        _boneCache.Clear();
    }

    /// Drops all per-actor refresh bookkeeping and the bone cache — called on full disconnect.
    internal static void PurgeAll()
    {
        _pendingRefresh.Clear();
        _boneCache.Clear();
    }

    private static Transform? FindByName(Transform root, string boneName)
    {
        int key = root.GetInstanceID();
        if (_boneCache.TryGetValue((key, boneName), out var cached))
            return cached; // may be null (bone not found on this avatar)

        var result = FindByNameRecursive(root, boneName);
        _boneCache[(key, boneName)] = result;
        return result;
    }

    private static Transform? FindByNameRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var hit = FindByNameRecursive(child, name);
            if (hit != null) return hit;
        }
        return null;
    }

    // Mounts a world cosmetic under its OWN follower node: the node tracks the player, the cosmetic keeps its prefab-local pose + offsets as its child, each world trails with its own follow-spring. Node destroyed with the cosmetic; spring off → node snaps every frame (rigid attach, the old behaviour).
    internal static void MountWorldCosmetic(GameObject instance, Transform avatarVisuals,
                                            GameObject sourcePrefab, string? assetId)
    {
        var node = new GameObject("WorldDecorationFollower");
        node.transform.SetParent(avatarVisuals, false);
        node.transform.localPosition = Vector3.zero;
        node.transform.localRotation = Quaternion.identity;
        node.transform.localScale = Vector3.one;
        node.AddComponent<WorldCosmeticsFollower>().Configure(
            avatarVisuals, assetId,
            wearer: avatarVisuals.GetComponent<PlayerAvatarVisuals>(), cosmetic: instance);

        Mount(instance, node.transform, sourcePrefab);

        instance.AddComponent<WorldFollowerCleanup>().Node = node;
    }

    // Per-PlayerCosmetics set of meshSwitch types OUR reconcile hid — so we only re-show base meshes we hid ourselves (never HiddenParts/PartShrinker or vanilla-local ones). Weak keys: drops with the avatar at GC, no manual prune.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayerCosmetics, HashSet<SemiFunc.CosmeticType>>
        _reconcileHidden = new();

    // Vanilla Cosmetic.Setup hides a meshSwitch type's base meshes by the SHARED asset.type. For a REMOTE instance that carries the VIEWER's own Type override, so vanilla hides the wrong base meshes (blanking another player's body part) or none (the owner's mesh override isn't reflected). Here, owner-authoritatively: hide a meshSwitch type's base meshes iff this avatar wears a cosmetic whose EFFECTIVE (owner) type is that meshSwitch type, else restore ONLY what we hid. Local instances are vanilla-correct, so they're skipped (HiddenParts/PartShrinker untouched).
    internal static void ReconcileMeshSwitchBaseMeshes(PlayerCosmetics pc)
    {
        if (pc == null || pc.cosmeticParents == null) return;
        var meta = MetaManager.instance;
        if (meta == null) return;

        // Only remote instances (incl. remote minis) can have a meshSwitch type discrepancy.
        if (!AvatarIdentity.TryGetRemoteActor(pc, out int actor)) return;

        var ownerWorn = new HashSet<SemiFunc.CosmeticType>();
        void Consider(CosmeticAsset? asset)
        {
            if (asset == null) return;
            SemiFunc.CosmeticType ownerType;
            if (BridgeIds.IsCustomizable(asset))
            {
                CustomizerSync.TryGetRemote(actor, asset.assetId, out var rd);
                ownerType = GetRemoteEffectiveType(asset, rd).cosmeticType;
            }
            else
            {
                ownerType = asset.type; // non-customizable assets are never mutated
            }
            if (IsMeshSwitchType(meta, ownerType)) ownerWorn.Add(ownerType);
        }

        var equipped = GetEquippedCosmetics(pc);
        if (equipped != null)
            foreach (var c in equipped)
                if (c != null) Consider(GetCosmeticAsset(c));

        var hiddenByUs = _reconcileHidden.GetOrCreateValue(pc);

        foreach (var cp in pc.cosmeticParents)
        {
            if (cp?.baseMeshes == null || !IsMeshSwitchType(meta, cp.cosmeticType)) continue;
            var type = cp.cosmeticType;

            if (ownerWorn.Contains(type))
            {
                foreach (var bm in cp.baseMeshes)
                    if (bm != null) bm.gameObject.SetActive(false);
                hiddenByUs.Add(type);
            }
            else if (hiddenByUs.Remove(type))
            {
                // We hid these for an owner meshSwitch that's now gone — vanilla won't undo OUR hide (it acted on the shared type), so restore them.
                foreach (var bm in cp.baseMeshes)
                    if (bm != null) bm.gameObject.SetActive(true);
            }
        }
    }

    private static bool IsMeshSwitchType(MetaManager meta, SemiFunc.CosmeticType type)
    {
        var ta = meta.cosmeticTypeAssets;
        if (ta == null) return false;
        int idx = (int)type;
        if (idx < 0 || idx >= ta.Count) return false;
        return ta[idx]?.meshSwitch ?? false;
    }
}
