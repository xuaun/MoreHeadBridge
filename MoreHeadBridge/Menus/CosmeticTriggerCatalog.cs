using System.Collections.Generic;

namespace MoreHeadBridge;

// Single consult point for "which triggers are valid for a type" — delegates to the popups that own the maps, so the prune-on-save check can never diverge from them.
internal static class CosmeticTriggerCatalog
{
    internal static IReadOnlyList<CosmeticCustomCondition.Type> ValidCustomTypes(SemiFunc.CosmeticType type)
        => CosmeticConditionsPopup.ValidCustomTypes(type);

    internal static IReadOnlyList<CosmeticCustomCondition.Type> ValidOffsetTriggers(SemiFunc.CosmeticType type)
        => CosmeticOffsetPopup.ValidOffsetTriggers(type);
}
