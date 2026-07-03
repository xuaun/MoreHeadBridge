// Spawns/dresses the Mini-Semibot avatar: instantiate the PlayerAvatarMenu prefab INACTIVE so worldAvatar is set before Awake (else Start parks/hijacks the menu instance).
// CRITICAL: NOT parented to the wearer — the mini carries its own PlayerCosmetics, and the wearer's GetComponentsInChildren scans would pick them up (cross-recolour / double-count). Scene root + MiniSemibotFollow; self-destructs with the wearer.
// Anti-recursion: dressing the mini excludes the Mini-Semibot and other world cosmetics, else it'd spawn minis forever.

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Spawn + lifecycle of the mini avatar. Placement constants live in MiniSemibotSpawner.Placement.cs;
// preset-roll logic in MiniSemibotSpawner.Presets.cs.
internal static partial class MiniSemibotSpawner
{
    // "No saved presets" already logged this session (cleared when a preset roll succeeds).
    private static bool _warnedNoPresets;

    // Wearer PlayerCosmetics → its live mini avatar, so outfit-change hooks re-dress without re-spawning. Pruned on access.
    private static readonly Dictionary<PlayerCosmetics, GameObject> _active = new();

    internal static GameObject? Spawn(PlayerCosmetics wearer, CosmeticAsset asset)
    {
        if (wearer == null || wearer.playerAvatarVisuals == null) return null;

        // Expression-preview avatar (5-9 keys HUD) → dedicated corner placement, mirrors the local player's synced expression.
        // CRITICAL: source must be PlayerAvatar.instance — the expression avatar's own playerAvatar is non-null but WRONG (its playerExpressions isn't fed by 5-9). May be null; resolved lazily in MiniSemibotFace.
        bool isExpr = IsExpressionAvatar(wearer.playerAvatarVisuals);
        var exprSource = isExpr ? PlayerAvatar.instance : wearer.playerAvatarVisuals.playerAvatar;

        var src = FindAvatarPrefab();
        if (src == null) { BceConsole.LogWarning("[MiniSemibot] No PlayerAvatarMenu prefab source found."); return null; }

        // Instantiate under an inactive holder so Awake is deferred until worldAvatar is set.
        var holder = new GameObject("MHB_MiniHolder");
        holder.SetActive(false);

        var go = Object.Instantiate(src.gameObject, holder.transform);
        go.name = "MHB_MiniSemibot";

        var menu = go.GetComponent<PlayerAvatarMenu>();
        if (menu != null)
        {
            menu.worldAvatar = true;     // BEFORE Awake → no menu hijack, no staging teleport
            menu.iconMakerAvatar = false;
            menu.expressionAvatar = false;
        }

        // Strip the menu render rig (camera + menu lights + audio listener) BEFORE activation — menu lighting must not leak into the world, and no stray camera / second AudioListener.
        StripRenderRig(go, menu);

        // Reparent to the scene root (NOT the wearer) — activates it, running Awake with worldAvatar set.
        go.transform.SetParent(null, worldPositionStays: false);
        Object.Destroy(holder);

        var miniVisuals = go.GetComponentInChildren<PlayerAvatarVisuals>();

        // The mini wears a FIXED outfit we drive. The menu-avatar prefab's previewCosmetics/previewColors=true makes Setup*Logic ignore our array and substitute MetaManager's live preview loadout — turn it off so our explicit arrays win.
        var miniPc = go.GetComponentInChildren<PlayerCosmetics>(true);
        if (miniPc != null) { miniPc.previewCosmetics = false; miniPc.previewColors = false; }

        // Mirror the wearer's body animation each frame (menu avatars get no movement state). Pass PlayerAvatarVisuals, NOT .animator (assigned in Start, not run yet) — AnimSync resolves both lazily.
        MiniSemibotAnimSync? sync = null;
        if (miniVisuals != null)
        {
            sync = go.AddComponent<MiniSemibotAnimSync>();
            sync.SourceVisuals = wearer.playerAvatarVisuals;
            sync.TargetVisuals = miniVisuals;
        }

        // Follow by script + hold scale (defeats Start()'s localScale reset); drives the placement/death/tumble state machine off the wearer's PlayerAvatar.
        var follow = go.AddComponent<MiniSemibotFollow>();
        follow.WearerVisuals = wearer.playerAvatarVisuals;
        follow.WearerAvatar = exprSource;
        follow.AnimSync = sync;
        follow.Body = miniVisuals != null ? miniVisuals.gameObject : null;
        follow.MiniVisuals = miniVisuals;
        follow.Scale = Vector3.one * MiniSemibotSettings.Scale;
        follow.ExpressionPreview = isExpr;   // initial; re-read live each LateUpdate

        // Tag so WorldCosmeticsSetupPatch's tracking recognises it (the root has no Cosmetic component).
        var tag = go.AddComponent<MiniSemibotTag>();
        tag.Asset = asset;

        foreach (var anim in go.GetComponentsInChildren<Animator>(true))
        {
            if (anim == null) continue;
            anim.enabled = true;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.speed = 1f;
        }

        // Grabber hand visual per config + the map-in-hand driver (no-op unless holding the map and not "clean arm").
        ApplyGrabberVisual(go);
        var miniArm = go.GetComponentInChildren<PlayerAvatarRightArm>();
        var mapHold = go.AddComponent<MiniMapHold>();
        mapHold.WearerAvatar = wearer.playerAvatarVisuals.playerAvatar;
        mapHold.MiniArm = miniArm;
        mapHold.Follow = follow;

        // Mini flashlight while the wearer's is out — same Orb/OrbLight gating as the map clone.
        var flashHold = go.AddComponent<MiniFlashlightHold>();
        flashHold.WearerAvatar = wearer.playerAvatarVisuals.playerAvatar;
        flashHold.Follow = follow;

        // Hurt/heal/upgrade flash + eye-override colours + pupil dilation, mirrored from the wearer (popup "State Effects").
        var stateFx = go.AddComponent<MiniStateEffects>();
        stateFx.WearerAvatar = wearer.playerAvatarVisuals.playerAvatar;
        stateFx.Follow = follow;
        stateFx.MiniCosmetics = go.GetComponentInChildren<PlayerCosmetics>(true);

        // Tumble wings (flight upgrade / zero-g / pink Heart Hugger) mirrored onto the mini.
        var wings = go.AddComponent<MiniWingsHold>();
        wings.WearerAvatar = wearer.playerAvatarVisuals.playerAvatar;
        wings.Follow = follow;

        // Telekinesis beam from the mini's hand to the object YOU are holding.
        var beam = go.AddComponent<MiniGrabBeam>();
        beam.WearerAvatar = wearer.playerAvatarVisuals.playerAvatar;
        beam.MiniArm = miniArm;
        beam.Follow = follow;

        // Crown: the clone's own PlayerCrown activates when the WEARER is the crowned player.
        var crownDriver = go.AddComponent<MiniCrownDriver>();
        crownDriver.WearerAvatar = exprSource;
        crownDriver.Follow = follow;

        // Face: point the cloned PlayerExpression at the WEARER *before its Start runs* (object just activated; Start runs before next Update) so it mirrors the owner's synced expressions, not the local player.
        var wearerAvatar = exprSource;
        var miniTalk = go.GetComponentInChildren<PlayerAvatarTalkAnimation>(true);
        var miniExpr = go.GetComponentInChildren<PlayerExpression>(true);
        if (miniExpr != null && wearerAvatar != null)
        {
            miniExpr.playerAvatar = wearerAvatar;
            miniExpr.onlyVisualRepresentation = true;   // never push expression state to the network
        }
        var face = go.AddComponent<MiniSemibotFace>();
        face.WearerAvatar = wearerAvatar;
        face.ExpressionPreview = isExpr;
        face.Expression = miniExpr;
        face.Follow = follow;
        if (miniTalk != null && miniTalk.objectToRotate != null)
        {
            face.MouthObject = miniTalk.objectToRotate.transform;
            face.MouthMaxAngle = miniTalk.rotationMaxAngle;
        }

        // Roll the random-preset outfit now (re-rolled each equip) for the LOCAL wearer; remote mirrors their synced live outfit.
        RollOutfitForTag(wearer, tag);

        _active[wearer] = go;
        RefreshOutfit(wearer); // dress immediately
        return go;
    }

    // Re-dresses the wearer's mini to the CURRENT outfit, only if changed. Works local (MetaManager) and remote (MiniSemibotOutfitCache — what the engine already synced).
    internal static void RefreshOutfit(PlayerCosmetics wearer)
    {
        if (wearer == null) return;
        PruneActive();
        if (!_active.TryGetValue(wearer, out var go) || go == null) return;

        var meta = MetaManager.instance;
        if (meta == null) return;

        var tag = go.GetComponent<MiniSemibotTag>();

        // Re-derive the RandomPreset snapshot from the persisted slot on every refresh — a roll changed elsewhere (unequip+equip re-roll) must reach an already-spawned mini. Cheap: the same slot is reused unless cleared.
        if (tag != null) RollOutfitForTag(wearer, tag);

        bool isLocal = IsLocalWearer(wearer.playerAvatarVisuals);

        int[] cosmetics;
        int[]? colors;
        int remoteActor = -1;   // set for remote wearers (used to apply their synced per-cosmetic colours)

        // Local + RandomPreset with a rolled snapshot → use that fixed outfit; otherwise mirror the live/synced outfit.
        if (isLocal && tag != null && tag.PresetCosmetics != null)
        {
            cosmetics = FilterIndices(tag.PresetCosmetics, meta, excludeWorldOverride: true);
            colors = tag.PresetColors;
        }
        else if (isLocal)
        {
            cosmetics = FilterIndices(meta.cosmeticEquipped, meta, excludeWorldOverride: true);
            colors = meta.colorsEquipped;
        }
        else
        {
            // Remote: prefer the OWNER's broadcast outfit (rolled preset shows right), falling back to the synced live outfit.
            remoteActor = MiniSemibotSync.ActorOf(wearer.playerAvatarVisuals.playerAvatar);
            if (MiniSemibotSync.TryGetRemoteOutfit(remoteActor, out var syncedCos, out var syncedCol))
            {
                cosmetics = FilterIndices(syncedCos, meta, remoteActor: remoteActor);
                colors = syncedCol;
            }
            else
            {
                var raw = MiniSemibotOutfitCache.GetCosmetics(wearer);
                if (raw == null) return; // their outfit hasn't been synced/captured yet
                cosmetics = FilterIndices(raw, meta, remoteActor: remoteActor);
                colors = MiniSemibotOutfitCache.GetColors(wearer);
            }
        }

        // Remember what the LOCAL mini is wearing so MiniSemibotSync can broadcast it to other clients.
        // Colours are COPIED (colorsEquipped is the live array — an alias would leak in-progress colours into
        // every snapshot); outfit AND colours are held back while the browse gate is open (committed on menu confirm).
        if (isLocal)
        {
            if (PerCosmeticColorNetworkSync.BrowseGateOpen)
            {
                _pendingBroadcastCosmetics = cosmetics;
                _pendingBroadcastColors = (int[]?)colors?.Clone();
            }
            else
            {
                LocalState.Cosmetics = cosmetics;
                LocalState.Colors = (int[]?)colors?.Clone();
            }
        }

        string sig = Signature(cosmetics, colors);
        if (tag != null && tag.OutfitSig == sig)
            return; // outfit unchanged → skip the (costly) re-dress (config changes broadcast separately)

        // includeInactive: the body is SetActive(false) in the "Hide" state but must still be dressed in DATA — else a later Death Head switch clones a bald, uncoloured mini.
        var pc = go.GetComponentInChildren<PlayerCosmetics>(true);
        if (pc == null) return;

        void Dress()
        {
            pc.SetupCosmeticsLogic(cosmetics, _forced: true);
            if (colors != null) pc.SetupColorsLogic(colors);
            DisableCosmeticLights(pc);
        }

        // RandomPreset: dress under the PRESET's colour context so customs/animations come from the preset, not the live store. SameAsPlayer dresses normally.
        if (tag != null && tag.PresetSlot >= 0)
            PerCosmeticColors.RunWithPresetContext(tag.PresetSlot, Dress);
        else
            Dress();

        if (tag != null) tag.OutfitSig = sig;

        // Outfit/colours changed → invalidate the built death-head model so it rebuilds with the new look (only reached when the signature actually changed).
        go.GetComponent<MiniSemibotFollow>()?.InvalidateDeathHead();

        if (isLocal)
        {
            // Capture the mini's per-cosmetic custom/animated colours for broadcast (RandomPreset ships the preset's; SameAsPlayer empty → viewer reuses our live store).
            CaptureLocalMiniColorData(tag != null ? tag.PresetSlot : -1);
            MiniSemibotSync.BroadcastLocal();   // our outfit changed → tell everyone
        }
        else
        {
            // Remote mini: paint the OWNER's synced custom/animated per-cosmetic colours over the palette dress.
            MiniSemibotSync.ApplyRemoteMiniColors(remoteActor, pc);
        }
    }

    // Snapshots the LOCAL mini's custom/animated colour blobs for broadcast: RandomPreset → the preset's (not in our live store); SameAsPlayer → null (viewers reuse our synced live colours).
    // Outfit/body colours captured while the browse gate was open — committed to LocalState on menu confirm.
    private static int[]? _pendingBroadcastCosmetics;
    private static int[]? _pendingBroadcastColors;

    internal static void CommitPendingBroadcast()
    {
        if (_pendingBroadcastCosmetics != null) { LocalState.Cosmetics = _pendingBroadcastCosmetics; _pendingBroadcastCosmetics = null; }
        if (_pendingBroadcastColors != null)    { LocalState.Colors    = _pendingBroadcastColors;    _pendingBroadcastColors = null; }
    }

    private static void CaptureLocalMiniColorData(int presetSlot)
    {
        if (presetSlot >= 0)
            (LocalState.ColorData, LocalState.AnimData, LocalState.CustomData, LocalState.SlotAnimData)
                = PerCosmeticColors.SerializePresetForBroadcast(presetSlot);
        else
            LocalState.ColorData = LocalState.AnimData = LocalState.CustomData = LocalState.SlotAnimData = null;
    }

    // True for a Mini-Semibot worn by a REMOTE player: its colours come from the owner's synced data, so the local apply paths must skip it — else the viewer's store bleeds onto another player's mini.
    internal static bool IsRemoteMiniCosmetics(PlayerCosmetics? pc)
        => RemoteMiniWearerOf(pc) != null;

    /// The live mini's PlayerCosmetics + current size for a wearer, or null. Lets the sway
    /// impulse patches forward the wearer's spring kicks to their mini (the mini's own PC never
    /// receives the vanilla CosmeticSpring* calls), force-scaled so the small body doesn't whip.
    internal static (PlayerCosmetics pc, float scale)? ActiveMiniOf(PlayerCosmetics? wearer)
    {
        if (wearer == null) return null;
        PruneActive();
        if (!_active.TryGetValue(wearer, out var go) || go == null) return null;

        var pc = go.GetComponentInChildren<PlayerCosmetics>(true);
        if (pc == null) return null;
        var follow = go.GetComponent<MiniSemibotFollow>();
        return (pc, follow != null ? follow.Scale.x : 1f);
    }

    /// Re-dresses the mini that owns <paramref name="pc"/> from its proper outfit source (rolled
    /// preset or live outfit). False when pc isn't an active mini. Used by ReinstantiateCosmetic:
    /// vanilla SetupCosmetics on a menu avatar reads the LOCAL MetaManager loadout, which would
    /// dress a RandomPreset mini in the live outfit.
    internal static bool TryRedressMini(PlayerCosmetics? pc)
    {
        if (pc == null) return false;
        PruneActive();
        foreach (var kvp in _active)
        {
            var go = kvp.Value;
            if (go == null || go.GetComponentInChildren<PlayerCosmetics>(true) != pc) continue;

            // Clear the fingerprint so the re-dress runs even if the outfit is unchanged (mount postfixes re-read the saved override data).
            var tag = go.GetComponent<MiniSemibotTag>();
            if (tag != null) tag.OutfitSig = null;
            RefreshOutfit(kvp.Key);
            return true;
        }
        return false;
    }

    // The REMOTE wearer of a mini's PlayerCosmetics, or null if not a remote mini. Manual parent walk (GetComponentInParent skips the SetActive(false) Hide-state body).
    internal static PlayerAvatar? RemoteMiniWearerOf(PlayerCosmetics? pc)
    {
        if (pc == null) return null;
        MiniSemibotFollow? follow = null;
        for (var t = pc.transform; t != null && follow == null; t = t.parent)
            follow = t.GetComponent<MiniSemibotFollow>();
        var wearer = follow != null ? follow.WearerAvatar : null;
        return wearer != null && !wearer.isLocal ? wearer : null;
    }

    // The Photon actor of a remote mini's wearer, or -1 (not a remote mini / actor unresolved).
    internal static int RemoteMiniActorOf(PlayerCosmetics? pc)
        => MiniSemibotSync.ActorOf(RemoteMiniWearerOf(pc));

    // The death-head model clones the outfit ONCE, so a colour/animation edit wouldn't show while a mini is in death-head state — invalidate our OWN minis' death heads to rebuild next frame. (Remote rebuild via OnRemoteSyncChanged.)
    internal static void InvalidateLocalDeathHeads()
    {
        PruneActive();
        foreach (var kv in _active)
        {
            var wearerAvatar = kv.Key != null ? kv.Key.playerAvatarVisuals?.playerAvatar : null;
            if (wearerAvatar != null && wearerAvatar.isLocal && kv.Value != null)
                kv.Value.GetComponent<MiniSemibotFollow>()?.InvalidateDeathHead();
        }
    }

    // Mirror of InvalidateLocalDeathHeads for REMOTE wearers' minis — used when a viewer-side toggle
    // (e.g. SeeRemoteColorAnimations) changes how their baked death-head models should look.
    internal static void InvalidateRemoteMiniDeathHeads()
    {
        PruneActive();
        foreach (var kv in _active)
        {
            var wearerAvatar = kv.Key != null ? kv.Key.playerAvatarVisuals?.playerAvatar : null;
            if (wearerAvatar != null && !wearerAvatar.isLocal && kv.Value != null)
                kv.Value.GetComponent<MiniSemibotFollow>()?.InvalidateDeathHead();
        }
    }

    // Re-broadcast after a per-cosmetic colour edit — a colour-only edit doesn't change the outfit signature, so RefreshOutfit alone wouldn't resend. Local view updates via the normal SetupColors path.
    internal static void OnLocalColorsChanged()
    {
        PruneActive();
        if (_active.Count == 0) return;

        InvalidateLocalDeathHeads();

        int slot = MiniSemibotSettings.OutfitMode == MiniSemibotOutfitMode.RandomPreset
            ? MiniSemibotVisualPrefs.RolledPreset : -1;
        CaptureLocalMiniColorData(slot);
        MiniSemibotSync.BroadcastLocal();
    }

    // Snapshot of what the LOCAL player's mini is wearing, read by MiniSemibotSync.BuildSection to broadcast it.
    // PRODUCER→CONSUMER coupling: written only by RefreshOutfit / CaptureLocalMiniColorData; grouping the six
    // values into one struct makes that explicit. Reading before the first RefreshOutfit yields the default
    // (all-null) state — i.e. "nothing applied yet", which BuildSection treats as an empty outfit.
    internal struct LocalMiniState
    {
        public int[]? Cosmetics;     // applied cosmetic indices
        public int[]? Colors;        // applied palette colours
        public string? ColorData;    // RandomPreset only (null for SameAsPlayer); per-cosmetic blobs:
        public string? AnimData;
        public string? CustomData;
        public string? SlotAnimData;
    }

    internal static LocalMiniState LocalState;

    // A remote player's synced Mini-Semibot payload changed → re-dress + re-apply visuals (placement/size/leg-speed are read live each frame).
    internal static void OnRemoteSyncChanged(int actor)
    {
        PruneActive();
        foreach (var kv in _active)
        {
            var wearer = kv.Key;
            var go = kv.Value;
            if (wearer == null || go == null || wearer.playerAvatarVisuals == null) continue;
            if (MiniSemibotSync.ActorOf(wearer.playerAvatarVisuals.playerAvatar) != actor) continue;

            var tag = go.GetComponent<MiniSemibotTag>();
            if (tag != null) tag.OutfitSig = null;   // force re-dress from the new synced outfit
            ApplyGrabberVisual(go);                   // grabber orb mesh per their Holding choice
            RefreshOutfit(wearer);
        }
    }

    // Re-applies a remote actor's synced colours onto their mini without re-dressing (the colours live in the
    // colour cache, which the Mini section can't carry; refreshed after the colour sections arrive).
    internal static void RefreshRemoteMiniColors(int actor)
    {
        if (actor < 0) return;
        PruneActive();
        foreach (var kv in _active)
        {
            var wearer = kv.Key;
            var go = kv.Value;
            if (wearer == null || go == null || wearer.playerAvatarVisuals == null) continue;
            if (MiniSemibotSync.ActorOf(wearer.playerAvatarVisuals.playerAvatar) != actor) continue;

            var pc = go.GetComponentInChildren<PlayerCosmetics>(true);
            if (pc != null) MiniSemibotSync.ApplyRemoteMiniColors(actor, pc);

            // The static death-head model bakes colours at build — rebuild it so the fresh cache shows there too.
            go.GetComponent<MiniSemibotFollow>()?.InvalidateDeathHead();
        }
    }

    // Pushes popup setting changes onto every active mini: re-roll + re-dress. Placement/death/leg-speed are read every frame and apply on their own.
    internal static void ApplyLiveSettings()
    {
        PruneActive();
        foreach (var kv in _active)
        {
            var wearer = kv.Key;
            var go = kv.Value;
            if (wearer == null || go == null) continue;
            var tag = go.GetComponent<MiniSemibotTag>();
            if (tag != null)
            {
                RollOutfitForTag(wearer, tag);
                tag.OutfitSig = null; // force RefreshOutfit to re-apply
            }
            ApplyGrabberVisual(go);   // grabber hand visual can change in the popup
            RefreshOutfit(wearer);
        }
        // A local popup change (position/size/leg-speed/holding/death/outfit) → tell other clients.
        MiniSemibotSync.BroadcastLocal();
    }

    // Grabber hand visual: show/hide the orb mesh + light per the WEARER's effective config (owner-authoritative). StripRenderRig killed all lights — only the grabber light is ever re-enabled (OrbLight).
    internal static void ApplyGrabberVisual(GameObject go)
    {
        var wearer = go.GetComponent<MiniSemibotFollow>()?.WearerAvatar;
        var visual = MiniSemibotSync.Resolve(wearer).Grabber;
        bool showOrb = visual != MiniSemibotGrabberVisual.CleanArm;
        bool light = visual == MiniSemibotGrabberVisual.OrbLight;

        foreach (var arm in go.GetComponentsInChildren<PlayerAvatarRightArm>(true))
        {
            if (arm == null) continue;
            // The "Grabber" root is INACTIVE in the menu prefab (vanilla only activates it for the live menu avatar), so the claw/orb never renders — activate the root per config; GrabberClawLogic still gates the claw child to actual grabs.
            if (arm.grabberTransform != null) arm.grabberTransform.gameObject.SetActive(showOrb);
            SetRenderersUnder(arm.grabberClawParent, showOrb);
            SetRenderersUnder(arm.grabberOrb, showOrb);
            if (arm.grabberLight != null) arm.grabberLight.enabled = light;
        }
    }

    private static void SetRenderersUnder(Transform? root, bool enabled)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = enabled;
    }

    // Every menu/preview avatar is the local player (playerAvatar back-ref can be null early) → treat as local, else the mini skips the RandomPreset roll. Uses IsMenuOrPreviewWearer (the cosmetics preview is a PlayerAvatarMenu without isMenuAvatar).
    private static bool IsLocalWearer(PlayerAvatarVisuals? v)
        => v != null && (IsMenuOrPreviewWearer(v) || (v.playerAvatar != null && v.playerAvatar.isLocal));

    // True when these visuals belong to the expression-preview avatar (5-9 keys HUD); resolves the PlayerAvatarMenu via back-ref or parent lookup.
    internal static bool IsExpressionAvatar(PlayerAvatarVisuals? v)
    {
        if (v == null) return false;
        var menu = v.playerAvatarMenu != null ? v.playerAvatarMenu : v.GetComponentInParent<PlayerAvatarMenu>();
        return menu != null && menu.expressionAvatar;
    }

    // True when the WEARER is a menu/preview avatar (every one is a PlayerAvatarMenu; the real avatar isn't). Such minis must NEVER enter death/tumble states — that's the "death head sticks at the menu position" bug.
    internal static bool IsMenuOrPreviewWearer(PlayerAvatarVisuals? v)
    {
        if (v == null) return false;
        if (v.isMenuAvatar) return true;
        return (v.playerAvatarMenu != null ? v.playerAvatarMenu : v.GetComponentInParent<PlayerAvatarMenu>()) != null;
    }

    // Re-dresses the expression-preview avatar so its mini spawns/despawns right after the popup toggle. No-op outside a level.
    internal static void RefreshExpressionPreview()
    {
        var pc = PlayerExpressionsUI.instance?.playerAvatarVisuals?.playerCosmetics;
        if (pc == null) return;
        try
        {
            pc.SetupCosmetics(_synced: false, _forced: true);
            pc.SetupColors(_synced: false);
        }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"Mini-Semibot expression-preview refresh failed: {ex.Message}");
        }
    }

    private static void PruneActive()
    {
        if (_active.Count == 0) return;
        List<PlayerCosmetics>? dead = null;
        foreach (var kv in _active)
            if (kv.Key == null || kv.Value == null) (dead ??= new()).Add(kv.Key!);
        if (dead != null) foreach (var k in dead) _active.Remove(k);
    }

    // Cosmetics dressed onto the mini AFTER StripRenderRig (spawn-only) carry their own Lights — a vanilla
    // HeadTopMesh light then blows out the world around the small mini. Disable them after every dress.
    private static void DisableCosmeticLights(PlayerCosmetics pc)
    {
        if (pc.cosmeticEquipped != null)
            foreach (var c in pc.cosmeticEquipped)
                if (c != null)
                    foreach (var l in c.GetComponentsInChildren<Light>(true)) l.enabled = false;
    }

    // Disables the cloned menu avatar's render rig (menu lights, extra Camera/AudioListener) so it's a plain world prop.
    private static void StripRenderRig(GameObject go, PlayerAvatarMenu? menu)
    {
        // The camera + menu lights live under cameraAndStuff; deactivating it kills them in one shot.
        if (menu != null && menu.cameraAndStuff != null)
            menu.cameraAndStuff.gameObject.SetActive(false);

        // Defensive sweep for any stray light/camera/audio listener anywhere in the rig.
        foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;

        // CRITICAL: kill every collider. The mini sits on the PlayerVisuals layer with a NULL visuals.playerAvatar — vanilla SphereCasts that dereference it (PlayerNameChecker.Update) would NRE every frame and crash. Purely decorative; never hit by name tags / targeting / grabs.
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
    }

    // Picks the best avatar source in memory: a PLAIN prefab asset (idle-animated) over icon-maker/world/expression.
    internal static PlayerAvatarMenu? FindAvatarPrefab()
    {
        PlayerAvatarMenu? pick = null;
        int best = int.MinValue;
        foreach (var c in Resources.FindObjectsOfTypeAll<PlayerAvatarMenu>())
        {
            if (c == null) continue;
            bool isAsset = !c.gameObject.scene.IsValid();
            int score = 0;
            if (isAsset) score += 100;
            if (!c.iconMakerAvatar && !c.expressionAvatar && !c.worldAvatar) score += 50;
            else if (c.worldAvatar) score += 20;
            else if (c.iconMakerAvatar) score += 10;
            else if (c.expressionAvatar) score += 1;
            if (score > best) { best = score; pick = c; }
        }
        return pick;
    }

    // Keeps only valid wearable indices: drops out-of-range, the Mini-Semibot itself, and any world cosmetic (anti-recursion).
    // remoteActor > 0: world membership comes from the OWNER's broadcast Type (GetRemoteEffectiveType), NOT global WorldAssetIds — the viewer's own Type=World override mutates WorldAssetIds and would mis-judge the owner's hat.
    private static int[] FilterIndices(IList<int> raw, MetaManager meta,
        bool excludeWorldOverride = false, int remoteActor = -1)
    {
        bool isRemote = remoteActor > 0;
        var list = new List<int>(raw.Count);
        foreach (int i in raw)
        {
            if (i < 0 || i >= meta.cosmeticAssets.Count) continue;
            var a = meta.cosmeticAssets[i];
            if (a == null || a.assetId == MiniSemibotCosmetic.AssetId) continue;

            bool isWorld;
            if (isRemote)
            {
                // Owner-authoritative world flag (native world OR the owner's own Type=World override).
                CustomizerSync.TryGetRemote(remoteActor, a.assetId, out var rd);
                isWorld = MoreHeadCosmeticMountPatch.GetRemoteEffectiveType(a, rd).isWorld;
            }
            else
            {
                // Local: WorldAssetIds already reflects the viewer's Type=World override, so IsWorldAsset catches both; the explicit check is belt-and-braces.
                isWorld = HhhCosmeticLoader.IsWorldAsset(a)
                          || (excludeWorldOverride
                              && CustomizerStore.TryGet(a.assetId, out var d)
                              && d.Type == OverrideCosmeticType.World);
            }
            if (isWorld) continue;
            list.Add(i);
        }
        return list.ToArray();
    }

    // Cheap fingerprint of an outfit (cosmetics + colours) so RefreshOutfit skips the costly re-dress when nothing changed.
    private static string Signature(int[] cosmetics, int[]? colors)
    {
        var sb = new StringBuilder();
        foreach (int i in cosmetics) sb.Append(i).Append(',');
        sb.Append('|');
        if (colors != null)
            foreach (int c in colors) sb.Append(c).Append(',');
        return sb.ToString();
    }
}
