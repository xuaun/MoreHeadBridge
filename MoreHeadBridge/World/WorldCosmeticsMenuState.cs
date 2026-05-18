using UnityEngine;

namespace MoreHeadBridge;

// Shared state for the WORLD category UI — holds the injected CosmeticCategoryAsset
// and a reference to the active Cosmetics-tab page so other patches can read
// selectedCategory without a FindObjectOfType call.
internal static class WorldCosmeticsMenuState
{
    internal static CosmeticCategoryAsset? Category { get; private set; }
    internal static MenuPageCosmetics? CurrentPage { get; set; }

    internal static bool IsWorldCategory(CosmeticCategoryAsset? category)
        => category != null && (category == Category || category.name == "MHB_World");

    internal static void EnsureCategory()
    {
        if (Category != null) return;

        Category = ScriptableObject.CreateInstance<CosmeticCategoryAsset>();
        Category.name = "MHB_World";
        Category.categoryName = "WORLD";
        // World assets are Hat type internally so vanilla populates sections with Hat
        // cosmetics (which include worlds). WorldCosmeticsMenuFilterPatch then controls
        // visibility via IsWorldAsset.
        Category.typeList = [SemiFunc.CosmeticType.Hat];
    }
}
