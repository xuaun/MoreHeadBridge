using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

// Records the cosmetic/colour arrays the game last passed to each player's SetupCosmetics/ColorsLogic — the engine already syncs these to every client, so a REMOTE player's outfit is learned with no custom networking and used to dress their mini.
internal static class MiniSemibotOutfitCache
{
    private static readonly Dictionary<PlayerCosmetics, int[]> _cosmetics = new();
    private static readonly Dictionary<PlayerCosmetics, int[]> _colors = new();

    internal static void RecordCosmetics(PlayerCosmetics pc, int[]? indices)
    {
        if (pc == null || indices == null) return;
        Prune();
        _cosmetics[pc] = (int[])indices.Clone();
    }

    internal static void RecordColors(PlayerCosmetics pc, int[]? colors)
    {
        if (pc == null || colors == null) return;
        Prune();
        _colors[pc] = (int[])colors.Clone();
    }

    internal static int[]? GetCosmetics(PlayerCosmetics pc)
        => pc != null && _cosmetics.TryGetValue(pc, out var v) ? v : null;

    internal static int[]? GetColors(PlayerCosmetics pc)
        => pc != null && _colors.TryGetValue(pc, out var v) ? v : null;

    // Drops entries whose PlayerCosmetics (a Unity object) has been destroyed (player left).
    private static void Prune()
    {
        PruneDict(_cosmetics);
        PruneDict(_colors);
    }

    private static void PruneDict(Dictionary<PlayerCosmetics, int[]> dict)
    {
        if (dict.Count == 0) return;
        List<PlayerCosmetics>? dead = null;
        foreach (var key in dict.Keys)
            if (key == null) (dead ??= new()).Add(key!);
        if (dead != null) foreach (var k in dead) dict.Remove(k);
    }
}
