using HarmonyLib;
using System.Collections.Generic;

namespace MoreHeadBridge;

// UpdateHighlight walks every cosmetic and searches the history and unlock lists for each one: about 5 ms per call with thousands of cosmetics, 26 calls per menu open and several on each first hover. Swaps those searches for cached sets and keeps vanilla's counting.
[HarmonyPatch]
internal static class CosmeticHighlightCountPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        var types = new[]
        {
            typeof(MenuElementCosmeticSection),
            typeof(MenuElementButtonCosmeticCategory),
            typeof(MenuPageMain),
            typeof(MenuPageLobby),
            typeof(MenuPageEsc),
        };
        foreach (var type in types)
        {
            var method = AccessTools.DeclaredMethod(type, "UpdateHighlight");
            if (method != null) yield return method;
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var listContains = AccessTools.Method(typeof(List<int>), nameof(List<int>.Contains));
        var setContains = AccessTools.Method(typeof(CosmeticIndexSets), nameof(CosmeticIndexSets.Contains));

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(listContains))
            {
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = setContains;
            }
            yield return instruction;
        }
    }
}

// Set view of MetaManager's history and unlock index lists. Those lists only grow or get cleared, so a changed count is enough to rebuild.
internal static class CosmeticIndexSets
{
    private sealed class Entry
    {
        public int Count = -1;
        public readonly HashSet<int> Set = new();
    }

    private static readonly Dictionary<List<int>, Entry> Entries = new();

    internal static HashSet<int> Of(List<int> list)
    {
        if (!Entries.TryGetValue(list, out var entry))
            Entries[list] = entry = new Entry();

        if (entry.Count != list.Count)
        {
            entry.Set.Clear();
            foreach (int value in list) entry.Set.Add(value);
            entry.Count = list.Count;
        }
        return entry.Set;
    }

    internal static bool Contains(List<int> list, int value) => Of(list).Contains(value);
}
