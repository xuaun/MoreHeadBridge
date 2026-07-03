using System.Collections.Generic;
using T = CosmeticCustomCondition.Type;

namespace MoreHeadBridge;

// Built-in "fit" offsets seeded onto bridge cosmetics at mount time, mirroring vanilla's authored <Region>_Shape_* CosmeticOffsetConditions — without them .hhh cosmetics keep a fixed size and float/clip on non-default body shapes. Values are per-condition AVERAGES mined from every vanilla cosmetic prefab (Resources/cosmetics), mapped per type to its own region's conditions (regenerate with the mining script if prefabs change). Face/chin/ears/eyelid/foot are intentionally NOT seeded (re-verified June 2026: zero or single-sample authored data); feet reuse the well-authored leg entries. Merged in at runtime only — the user's saved per-cosmetic offsets always win for any trigger they configure (MergeInto skips seeded triggers the user already set). Always on (no toggle).
internal static class OffsetSeedDefaults
{
    private static readonly Dictionary<SemiFunc.CosmeticType, CosmeticOffsetEntry[]> Table = Build();

    private static CosmeticOffsetEntry E(
        T trigger, float px, float py, float pz, float rx, float ry, float rz,
        float sx, float sy, float sz, float lerp) => new()
    {
        TriggerType = trigger,
        PosX = px, PosY = py, PosZ = pz,
        RotX = rx, RotY = ry, RotZ = rz,
        ScaleX = sx, ScaleY = sy, ScaleZ = sz,
        LerpSpeed = lerp,
        Seeded = true,   // re-based onto the cosmetic's own default transform at injection
    };

    private static Dictionary<SemiFunc.CosmeticType, CosmeticOffsetEntry[]> Build()
    {
        var seedHeadTop = new[]
        {
            E(T.HeadTopMesh_Shape_Big, 0.0f,-0.0013f,0.0008f, 0.0216f,0.0f,3.286f, 1.027f,1.0176f,1.0232f, 3.0f),
            E(T.HeadTopMesh_Shape_Tiny, -0.013f,-0.0171f,-0.0243f, 4.0828f,0.0151f,3.7491f, 0.932f,0.9406f,0.9427f, 3.0f),
            E(T.HeadTopMesh_Shape_MissingRight, -0.0163f,-0.0072f,0.001f, 6.9458f,14.0261f,46.6643f, 0.9782f,0.9836f,0.9836f, 3.0f),
        };
        var seedHat = new[]
        {
            E(T.HeadTopMesh_Shape_Big, 0.0f,-0.0013f,0.0008f, 0.0216f,0.0f,3.286f, 1.027f,1.0176f,1.0232f, 3.0f),
            E(T.HeadTopMesh_Shape_Tiny, -0.013f,-0.0171f,-0.0243f, 4.0828f,0.0151f,3.7491f, 0.932f,0.9406f,0.9427f, 3.0f),
            E(T.HeadTopMesh_Shape_MissingRight, -0.0163f,-0.0072f,0.001f, 6.9458f,14.0261f,46.6643f, 0.9782f,0.9836f,0.9836f, 3.0f),
            E(T.Hat_Shape_Wide, 0.0f,0.0383f,0.0f, 0.0f,0.0f,0.0f, 1.0f,0.75f,1.0f, 3.0f),
        };
        var seedBodyTop = new[]
        {
            E(T.BodyTopMesh_Shape_Big, 0.0f,0.0f,0.0015f, 0.0f,0.0f,0.0f, 1.0631f,1.0f,1.13f, 3.0f),
            E(T.BodyTopMesh_Shape_Tiny, -0.0015f,0.0004f,-0.004f, -0.8824f,0.0f,0.0f, 0.7842f,0.9798f,0.7763f, 3.0f),
        };
        var seedBodyBot = new[]
        {
            E(T.BodyBottomMesh_Shape_Big, 0.0f,-0.0068f,0.0116f, 0.0f,0.0f,0.0f, 1.0608f,1.0497f,1.0767f, 3.0f),
            E(T.BodyBottomMesh_Shape_Tiny, 0.0002f,-0.0022f,-0.0164f, 0.0f,0.0f,0.0f, 0.8696f,0.986f,0.8025f, 3.0f),
            E(T.BodyBottomMesh_Shape_Wide, -0.0001f,-0.0091f,-0.0004f, 0.0f,0.0f,0.0f, 1.0293f,1.0221f,1.0576f, 3.0f),
            E(T.BodyBottomMesh_Shape_Tall, 0.0f,-0.0215f,0.0f, 0.0f,0.0f,0.0f, 1.0622f,1.169f,1.0689f, 3.0f),
        };
        var seedArmR = new[]
        {
            E(T.ArmRightMesh_Shape_Big, -0.0046f,0.0029f,0.0f, 0.0f,0.0f,11.2037f, 1.0496f,1.3052f,1.3495f, 3.0f),
            E(T.ArmRightMesh_Shape_Tiny, -0.0007f,0.0f,0.0f, 0.0f,0.0f,0.0f, 0.9983f,0.8486f,0.8486f, 3.0f),
            E(T.ArmRightMesh_Shape_BigUpper, 0.0372f,0.0012f,0.0007f, 0.0f,0.0f,11.3588f, 0.9976f,1.1713f,1.2537f, 3.0f),
            E(T.ArmRightMesh_Shape_BigLower, -0.0209f,0.0017f,0.0f, 0.0f,0.0f,22.7138f, 1.0088f,1.2623f,1.3263f, 3.0f),
            E(T.ArmRightMesh_Shape_Huge, 0.0057f,0.0049f,0.0f, 0.0f,0.0f,11.5225f, 0.9979f,1.1146f,1.1901f, 3.0f),
        };
        var seedArmL = new[]
        {
            E(T.ArmLeftMesh_Shape_Big, 0.0099f,0.0035f,0.0f, 0.0f,0.0f,0.1834f, 1.0423f,1.2984f,1.3414f, 3.0f),
            E(T.ArmLeftMesh_Shape_Tiny, 0.0014f,0.0003f,0.0f, 0.0f,0.0f,0.0754f, 0.9981f,0.8483f,0.8483f, 3.0f),
            E(T.ArmLeftMesh_Shape_BigUpper, -0.0376f,-0.0f,0.0007f, 0.0f,0.0f,0.0165f, 0.9962f,1.172f,1.249f, 3.0f),
            E(T.ArmLeftMesh_Shape_BigLower, 0.021f,0.001f,0.0f, 0.0f,0.0f,10.5522f, 1.0013f,1.2561f,1.3181f, 3.0f),
            E(T.ArmLeftMesh_Shape_Huge, -0.0062f,0.0068f,0.0f, 0.0f,0.0f,10.7012f, 0.9888f,1.1112f,1.1967f, 3.0f),
        };
        var seedLegR = new[]
        {
            E(T.LegRightMesh_Shape_Big, -0.0002f,0.0042f,0.0f, 0.0f,11.1131f,0.0f, 1.1307f,1.0117f,1.1111f, 3.0f),
            E(T.LegRightMesh_Shape_Tiny, 0.0f,0.0f,0.0f, 0.0f,0.0f,0.0f, 0.8755f,0.9957f,0.8759f, 3.0f),
            E(T.LegRightMesh_Shape_BigUpper, -0.0002f,-0.0028f,0.0002f, 0.0f,11.1131f,0.0f, 1.1328f,1.0156f,1.1292f, 3.0f),
            E(T.LegRightMesh_Shape_BigLower, -0.0002f,0.0028f,0.0033f, 0.0f,11.1131f,0.0f, 1.1285f,1.016f,1.1429f, 3.0f),
            E(T.LegRightMesh_Shape_Huge, -0.0002f,-0.0015f,0.0f, 0.0f,11.1131f,0.0f, 1.1703f,1.0213f,1.1991f, 3.0f),
        };
        var seedLegL = new[]
        {
            E(T.LegLeftMesh_Shape_Big, -0.0002f,0.0042f,0.0f, 0.0f,0.1369f,0.0f, 1.1321f,1.0038f,1.1075f, 3.0f),
            E(T.LegLeftMesh_Shape_Tiny, 0.0f,0.0f,0.0f, 0.0f,0.0f,0.0f, 0.8677f,0.9957f,0.8681f, 3.0f),
            E(T.LegLeftMesh_Shape_BigUpper, -0.0002f,-0.0028f,0.0002f, 0.0f,0.1369f,0.0f, 1.1358f,1.0077f,1.1303f, 3.0f),
            E(T.LegLeftMesh_Shape_BigLower, -0.0002f,0.0028f,0.0033f, 0.0f,0.1369f,0.0f, 1.1315f,1.0081f,1.144f, 3.0f),
            E(T.LegLeftMesh_Shape_Huge, -0.0002f,-0.0015f,0.0f, 0.0f,0.1369f,0.0f, 1.1749f,1.0134f,1.2018f, 3.0f),
        };
        var seedEyewear = new[]
        {
            E(T.Eyewear_Shape_Tall, 0.0f,0.0f,0.0f, -5.7143f,0.0f,0.0f, 1.0f,1.0f,1.0f, 3.0f),
            E(T.Eyewear_Shape_VeryTall_Inner, 0.0f,0.0f,0.0f, -10.3f,0.0f,0.0f, 1.0f,1.0f,1.0f, 3.0f),
            E(T.Eyewear_Shape_VeryTall_Outer, 0.0f,0.0f,0.0f, -5.3333f,0.0f,0.0f, 1.0f,1.0f,1.0f, 3.0f),
        };

        return new Dictionary<SemiFunc.CosmeticType, CosmeticOffsetEntry[]>
        {
            // Head — top of the skull
            [SemiFunc.CosmeticType.Hat]            = seedHat,
            [SemiFunc.CosmeticType.HeadTopMesh]    = seedHeadTop,
            [SemiFunc.CosmeticType.HeadTopOverlay] = seedHeadTop,

            // Head — eyewear (rotation tilt for tall lenses)
            [SemiFunc.CosmeticType.Eyewear] = seedEyewear,

            // Body — top
            [SemiFunc.CosmeticType.BodyTop]        = seedBodyTop,
            [SemiFunc.CosmeticType.BodyTopMesh]    = seedBodyTop,
            [SemiFunc.CosmeticType.BodyTopOverlay] = seedBodyTop,

            // Body — bottom
            [SemiFunc.CosmeticType.BodyBottom]        = seedBodyBot,
            [SemiFunc.CosmeticType.BodyBottomMesh]    = seedBodyBot,
            [SemiFunc.CosmeticType.BodyBottomOverlay] = seedBodyBot,

            // Right arm (the grabber mesh rides the right arm too)
            [SemiFunc.CosmeticType.ArmRight]        = seedArmR,
            [SemiFunc.CosmeticType.ArmRightMesh]    = seedArmR,
            [SemiFunc.CosmeticType.ArmRightOverlay] = seedArmR,
            [SemiFunc.CosmeticType.GrabberMesh]     = seedArmR,

            // Left arm
            [SemiFunc.CosmeticType.ArmLeft]        = seedArmL,
            [SemiFunc.CosmeticType.ArmLeftMesh]    = seedArmL,
            [SemiFunc.CosmeticType.ArmLeftOverlay] = seedArmL,

            // Right leg / foot (feet ride the leg shape)
            [SemiFunc.CosmeticType.LegRight]        = seedLegR,
            [SemiFunc.CosmeticType.LegRightMesh]    = seedLegR,
            [SemiFunc.CosmeticType.LegRightOverlay] = seedLegR,
            [SemiFunc.CosmeticType.FootRight]       = seedLegR,

            // Left leg / foot
            [SemiFunc.CosmeticType.LegLeft]        = seedLegL,
            [SemiFunc.CosmeticType.LegLeftMesh]    = seedLegL,
            [SemiFunc.CosmeticType.LegLeftOverlay] = seedLegL,
            [SemiFunc.CosmeticType.FootLeft]       = seedLegL,
        };
    }

    internal static bool HasDefaults(SemiFunc.CosmeticType type) => Table.ContainsKey(type);

    // Every distinct trigger that ANY seed reacts to (the body-shape "fit" conditions). An offset on one of these is a RELATIVE fit adjustment re-based onto the cosmetic at injection (seed OR user), so editing one never snaps the cosmetic to an absolute pose.
    private static readonly HashSet<CosmeticCustomCondition.Type> FitTriggerSet = BuildFitTriggerSet();
    private static HashSet<CosmeticCustomCondition.Type> BuildFitTriggerSet()
    {
        var set = new HashSet<CosmeticCustomCondition.Type>();
        foreach (var arr in Table.Values)
            foreach (var e in arr) set.Add(e.TriggerType);
        return set;
    }

    internal static bool IsFitTrigger(CosmeticCustomCondition.Type t) => FitTriggerSet.Contains(t);

    // Fit seeds too aggressive to apply blind (MissingRight's ~47° Z tilt suits skull-hugging vanilla
    // tops but wrecks tall hats / off-centre props). Kept in the table so the editor still pre-fills
    // the mined values, but MergeInto skips them until the user opts in per cosmetic (popup "Use").
    private static readonly HashSet<CosmeticCustomCondition.Type> OptInTriggers = new()
    {
        T.HeadTopMesh_Shape_MissingRight,
    };

    internal static bool IsOptInTrigger(CosmeticCustomCondition.Type t) => OptInTriggers.Contains(t);

    /// The fit triggers a given type has seeds for (in table order). Empty when none.
    internal static IReadOnlyList<CosmeticCustomCondition.Type> SeedTriggersFor(SemiFunc.CosmeticType type)
        => Table.TryGetValue(type, out var arr)
            ? System.Array.ConvertAll(arr, e => e.TriggerType)
            : System.Array.Empty<CosmeticCustomCondition.Type>();

    /// The seed (raw fit values) for a type+trigger, cloned. False when none — used to pre-fill the
    /// editor so tuning a fit starts from the automatic values instead of identity.
    internal static bool TryGetSeed(SemiFunc.CosmeticType type, CosmeticCustomCondition.Type trigger,
                                    out CosmeticOffsetEntry seed)
    {
        if (Table.TryGetValue(type, out var arr))
            foreach (var e in arr)
                if (e.TriggerType == trigger) { seed = e.Clone(); return true; }
        seed = null!;
        return false;
    }

    // User offsets + seeded fit offsets for this type; any trigger the user configured wins. Returns the input unchanged when seeding is off or nothing applies; never mutates it.
    internal static List<CosmeticOffsetEntry>? MergeInto(
        SemiFunc.CosmeticType type, List<CosmeticOffsetEntry>? userOffsets)
    {
        if (!Table.TryGetValue(type, out var seeds) || seeds.Length == 0) return userOffsets;

        var result = userOffsets != null
            ? new List<CosmeticOffsetEntry>(userOffsets)
            : new List<CosmeticOffsetEntry>();

        foreach (var seed in seeds)
        {
            if (OptInTriggers.Contains(seed.TriggerType)) continue;   // opt-in seeds only apply via a user entry
            bool userHasTrigger = false;
            foreach (var u in result)
                if (u.TriggerType == seed.TriggerType) { userHasTrigger = true; break; }
            if (!userHasTrigger) result.Add(seed.Clone());
        }
        return result.Count > 0 ? result : userOffsets;
    }
}
