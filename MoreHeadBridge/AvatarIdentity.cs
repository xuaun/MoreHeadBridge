namespace MoreHeadBridge;

// How an avatar's PlayerCosmetics is owned. RemoteMini/Remote carry the owner's actor number.
internal enum AvatarKind { Local, Menu, RemoteMini, Remote }

// Single source of truth for "whose avatar is this?" — every identity question (remote actor,
// local-vs-menu, remote mini, local-style target) is answered here so call sites can't
// half-reimplement the rules.
internal readonly struct AvatarIdentity
{
    public readonly AvatarKind Kind;
    public readonly int Actor;   // owner actor for RemoteMini/Remote, else -1

    public AvatarIdentity(AvatarKind kind, int actor) { Kind = kind; Actor = actor; }

    public bool IsRemote => Kind == AvatarKind.RemoteMini || Kind == AvatarKind.Remote;

    // ORDER MATTERS:
    //  1. A remote player's mini is a locally-spawned menu-avatar clone that may carry a LEFTOVER PhotonView
    //     (owner null / not IsMine). Resolve it by its follow-hierarchy FIRST so it's owner-authoritative
    //     regardless of any stray PhotonView — else it'd resolve actor=-1 and leak the viewer-mutated shared asset.
    //  2. Menu/preview avatars (cosmetics preview, expression portrait, icon maker) are ALWAYS local; their
    //     leftover PhotonView would otherwise route them to another actor's overrides. The only remote menu
    //     avatar is a remote mini, already handled in step 1.
    //  3. Otherwise mirror REPOLib: death-head avatars reach the player's photonView via a chain; others directly.
    public static AvatarIdentity Of(PlayerCosmetics instance)
    {
        if (!SemiFunc.IsMultiplayer()) return new AvatarIdentity(AvatarKind.Local, -1);

        int miniActor = MiniSemibotSpawner.RemoteMiniActorOf(instance);
        if (miniActor > 0) return new AvatarIdentity(AvatarKind.RemoteMini, miniActor);

        if (MiniSemibotSpawner.IsMenuOrPreviewWearer(instance.playerAvatarVisuals))
            return new AvatarIdentity(AvatarKind.Menu, -1);

        var photonView = instance.deathHead && instance.deathHead.setup && instance.deathHead.playerAvatar
            ? instance.deathHead.playerAvatar.photonView
            : instance.photonView;
        if (photonView == null || photonView.IsMine) return new AvatarIdentity(AvatarKind.Local, -1);

        int actor = photonView.Owner?.ActorNumber ?? -1;
        return actor > 0 ? new AvatarIdentity(AvatarKind.Remote, actor) : new AvatarIdentity(AvatarKind.Local, -1);
    }

    /// True (and sets actorNumber) when the avatar belongs to a remote player (incl. their mini).
    /// False for the local player, menu avatars, and singleplayer.
    internal static bool TryGetRemoteActor(PlayerCosmetics instance, out int actorNumber)
    {
        var id = Of(instance);
        actorNumber = id.IsRemote ? id.Actor : -1;
        return id.IsRemote;
    }

    /// The local player, a local death head, or any menu avatar. NOTE: a remote player's mini also
    /// passes (menu-avatar clone, no PhotonView) — use IsLocalStyleTarget when the answer decides
    /// whether the LOCAL stores may dress/colour the avatar.
    internal static bool IsLocalOrMenu(PlayerCosmetics pc)
    {
        if (pc == null) return false;
        if (pc.playerAvatarVisuals?.isMenuAvatar == true) return true;
        if (pc.photonView == null) return true;
        if (pc.photonView.IsMine) return true;
        return pc.deathHead != null && pc.deathHead.setup
            && pc.deathHead.playerAvatar?.photonView?.IsMine == true;
    }

    /// A remote player's Mini-Semibot — owner-authoritative; local apply paths must skip it.
    internal static bool IsRemoteMini(PlayerCosmetics? pc)
        => MiniSemibotSpawner.IsRemoteMiniCosmetics(pc);

    /// Avatars dressed and coloured from the LOCAL stores: the local player and menu/preview
    /// avatars, excluding a remote player's mini (which IsLocalOrMenu alone would treat as local).
    internal static bool IsLocalStyleTarget(PlayerCosmetics pc)
        => IsLocalOrMenu(pc) && !IsRemoteMini(pc);
}
