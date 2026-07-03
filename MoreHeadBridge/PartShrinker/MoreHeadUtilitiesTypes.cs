using System;

namespace MoreHeadBridge;

// Type resolution for MoreHeadUtilities via manual assembly scan — AccessTools.TypeByName logs a [Warning: HarmonyX] when the type is absent.
internal static class MoreHeadUtilitiesTypes
{
    internal static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            if (asm.GetName().Name == "MoreHeadUtilities")
                return asm.GetType(fullName);
        return null;
    }
}
