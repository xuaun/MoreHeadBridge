using System;

namespace MoreHeadBridge;

// Shared formatting/ordering for CosmeticCustomCondition.Type, used by both offset popups
// (CosmeticOffsetPopup list + CosmeticOffsetEntryPopup configurator).
internal static class CosmeticConditionFormat
{
    internal readonly struct ConditionOption
    {
        internal readonly CosmeticCustomCondition.Type Type;
        internal readonly string Label;

        internal ConditionOption(CosmeticCustomCondition.Type type, string label)
        {
            Type = type;
            Label = label;
        }
    }

    internal static int FamilyRank(CosmeticCustomCondition.Type type)
    {
        var name = type.ToString();
        if (TryGetFamily(name, out var familyRank, out _))
            return familyRank;

        if (name.StartsWith("EyeLeft_", StringComparison.Ordinal) ||
            name.StartsWith("EyeRight_", StringComparison.Ordinal) ||
            name.StartsWith("Hat_", StringComparison.Ordinal) ||
            name.StartsWith("Eyewear_", StringComparison.Ordinal) ||
            name.StartsWith("FaceTop_", StringComparison.Ordinal) ||
            name.StartsWith("FaceBottom_", StringComparison.Ordinal) ||
            name.StartsWith("Ears_", StringComparison.Ordinal) ||
            name.StartsWith("EyeLidRightMesh_", StringComparison.Ordinal) ||
            name.StartsWith("EyeLidLeftMesh_", StringComparison.Ordinal))
            return 2;

        return 100;
    }

    internal static int VariantRank(CosmeticCustomCondition.Type type)
    {
        var name = type.ToString();
        if (TryGetFamily(name, out _, out var variant))
        {
            return variant switch
            {
                "Default" => 0,
                "Big" => 1,
                "Tiny" => 2,
                "Wide" => 3,
                "Tall" => 4,
                "BigUpper" => 5,
                "BigLower" => 6,
                "Huge" => 7,
                "MissingRight" => 8,
                _ => 50,
            };
        }

        return 50;
    }

    private static bool TryGetFamily(string name, out int familyRank, out string variant)
    {
        familyRank = 100;
        variant = string.Empty;

        if (name.StartsWith("HeadTopMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 0;
            variant = name["HeadTopMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("HeadBottomMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 1;
            variant = name["HeadBottomMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("BodyTopMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 2;
            variant = name["BodyTopMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("BodyBottomMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 3;
            variant = name["BodyBottomMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("ArmRightMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 4;
            variant = name["ArmRightMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("ArmLeftMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 5;
            variant = name["ArmLeftMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("LegRightMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 6;
            variant = name["LegRightMesh_Shape_".Length..];
            return true;
        }

        if (name.StartsWith("LegLeftMesh_Shape_", StringComparison.Ordinal))
        {
            familyRank = 7;
            variant = name["LegLeftMesh_Shape_".Length..];
            return true;
        }

        return false;
    }

    /// headExplicit — when true, Head Top / Head Bottom are spelled out instead of the
    /// shorter "Top" / "Bottom". Used by the World list, where the trigger could come from
    /// any body region and the bare "Top"/"Bottom" would be ambiguous.
    internal static string Label(CosmeticCustomCondition.Type type, bool headExplicit = false)
    {
        var name = type.ToString();
        if (TryGetFamily(name, out _, out var variant))
            return $"{FamilyLabel(name, headExplicit)} - {Humanize(variant)}";

        var splitIndex = name.IndexOf('_');
        if (splitIndex >= 0)
        {
            var family = name[..splitIndex];
            var suffix = name[(splitIndex + 1)..];
            return $"{Humanize(family, headExplicit)} - {Humanize(suffix)}";
        }

        return Humanize(name, headExplicit);
    }

    private static string FamilyLabel(string name, bool headExplicit)
    {
        if (name.StartsWith("HeadTop", StringComparison.Ordinal))
            return headExplicit ? "Head Top" : "Top";

        if (name.StartsWith("HeadBottom", StringComparison.Ordinal))
            return headExplicit ? "Head Bottom" : "Bottom";

        if (name.StartsWith("BodyTop", StringComparison.Ordinal))
            return "Body Top";

        if (name.StartsWith("BodyBottom", StringComparison.Ordinal))
            return "Body Bottom";

        if (name.StartsWith("ArmRight", StringComparison.Ordinal))
            return "Arm Right";

        if (name.StartsWith("ArmLeft", StringComparison.Ordinal))
            return "Arm Left";

        if (name.StartsWith("LegRight", StringComparison.Ordinal))
            return "Leg Right";

        if (name.StartsWith("LegLeft", StringComparison.Ordinal))
            return "Leg Left";

        return Humanize(name.Replace("Mesh", string.Empty), headExplicit);
    }

    private static string Humanize(string token, bool headExplicit = false)
    {
        return token
            .Replace("BigUpper", "Big Upper")
            .Replace("BigLower", "Big Lower")
            .Replace("MissingRight", "Missing Right")
            .Replace("EyeLeft", "Eye Left")
            .Replace("EyeRight", "Eye Right")
            .Replace("EyeLidRight", "Eye Lid Right")
            .Replace("EyeLidLeft", "Eye Lid Left")
            .Replace("HeadTop", headExplicit ? "Head Top" : "Top")
            .Replace("HeadBottom", headExplicit ? "Head Bottom" : "Bottom")
            .Replace("BodyTop", "Body Top")
            .Replace("BodyBottom", "Body Bottom")
            .Replace("ArmRight", "Arm Right")
            .Replace("ArmLeft", "Arm Left")
            .Replace("LegRight", "Leg Right")
            .Replace("LegLeft", "Leg Left")
            .Replace("FootRight", "Foot Right")
            .Replace("FootLeft", "Foot Left")
            .Replace("_", " ")
            .Trim();
    }

    internal static int Compare(CosmeticOffsetEntry left, CosmeticOffsetEntry right)
    {
        int leftFamily = FamilyRank(left.TriggerType);
        int rightFamily = FamilyRank(right.TriggerType);
        if (leftFamily != rightFamily)
            return leftFamily.CompareTo(rightFamily);

        int leftVariant = VariantRank(left.TriggerType);
        int rightVariant = VariantRank(right.TriggerType);
        if (leftVariant != rightVariant)
            return leftVariant.CompareTo(rightVariant);

        return string.Compare(Label(left.TriggerType), Label(right.TriggerType), StringComparison.Ordinal);
    }
}
