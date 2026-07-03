// Static option/label tables for CosmeticOverridePopup (option strings + category ↔ type ↔ display-label maps; partial — see CosmeticOverridePopup.cs for the popup builder).

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

internal static partial class CosmeticOverridePopup
{
    private static readonly string[] RarityOptions =
        new[] { "Default" }.Concat(Enum.GetNames(typeof(SemiFunc.Rarity))).ToArray();

    private static readonly string[] MainOptions =
        Enum.GetNames(typeof(MainCosmeticCategory));  // Head, Body, Arms, Legs, World

    // Shown in bool? sliders (Bridge Border Highlight, Remove Physics, Loop Animation); maps to null / true / false.
    private static readonly string[] TriStateOptions = { "Default", "Yes", "No" };

    private static readonly string[] SwayOptions = { "Default", "No", "Light", "Moderate", "Strong" };

    // Options for vanilla equip animation override.
    private static readonly string[] EquipAnimOptions = { "Default", "Fixed", "Normal", "Disabled" };

    // Sub-category types per main group, in display order (overlays excluded).
    internal static readonly Dictionary<MainCosmeticCategory, OverrideCosmeticType[]> SubOptions = new()
    {
        [MainCosmeticCategory.Head] = new[]
        {
            OverrideCosmeticType.Hat,
            OverrideCosmeticType.Eyewear,
            OverrideCosmeticType.FaceTop,
            OverrideCosmeticType.FaceBottom,
            OverrideCosmeticType.HeadBottom,
            OverrideCosmeticType.Ears,
            OverrideCosmeticType.HeadTopMesh,
            OverrideCosmeticType.HeadBottomMesh,
            OverrideCosmeticType.EyeLidRightMesh,
            OverrideCosmeticType.EyeLidLeftMesh,
        },
        [MainCosmeticCategory.Body] = new[]
        {
            OverrideCosmeticType.BodyTop,
            OverrideCosmeticType.BodyBottom,
            OverrideCosmeticType.BodyTopMesh,
            OverrideCosmeticType.BodyBottomMesh,
        },
        [MainCosmeticCategory.Arms] = new[]
        {
            OverrideCosmeticType.ArmRight,
            OverrideCosmeticType.ArmLeft,
            OverrideCosmeticType.ArmRightMesh,
            OverrideCosmeticType.ArmLeftMesh,
        },
        [MainCosmeticCategory.Legs] = new[]
        {
            OverrideCosmeticType.LegRight,
            OverrideCosmeticType.LegLeft,
            OverrideCosmeticType.FootRight,
            OverrideCosmeticType.FootLeft,
            OverrideCosmeticType.LegRightMesh,
            OverrideCosmeticType.LegLeftMesh,
        },
        [MainCosmeticCategory.World] = new[] { OverrideCosmeticType.World },
    };

    // Human-readable display label for each sub-category type.
    internal static readonly Dictionary<OverrideCosmeticType, string> SubLabels = new()
    {
        [OverrideCosmeticType.Hat] = "Hat",
        [OverrideCosmeticType.Eyewear] = "Eyewear",
        [OverrideCosmeticType.FaceTop] = "Face Upper",
        [OverrideCosmeticType.FaceBottom] = "Face Middle",
        [OverrideCosmeticType.HeadBottom] = "Face Lower",
        [OverrideCosmeticType.Ears] = "Ears",
        [OverrideCosmeticType.HeadTopMesh] = "Head Mesh",
        [OverrideCosmeticType.HeadBottomMesh] = "Chin Mesh",
        [OverrideCosmeticType.EyeLidRightMesh] = "Eyelid R Mesh",
        [OverrideCosmeticType.EyeLidLeftMesh] = "Eyelid L Mesh",
        [OverrideCosmeticType.BodyTop] = "Bodywear Top",
        [OverrideCosmeticType.BodyBottom] = "Bodywear Bottom",
        [OverrideCosmeticType.BodyTopMesh] = "Body Top Mesh",
        [OverrideCosmeticType.BodyBottomMesh] = "Body Bot Mesh",
        [OverrideCosmeticType.ArmRight] = "Armwear Right",
        [OverrideCosmeticType.ArmLeft] = "Armwear Left",
        [OverrideCosmeticType.ArmRightMesh] = "Arm R Mesh",
        [OverrideCosmeticType.ArmLeftMesh] = "Arm L Mesh",
        [OverrideCosmeticType.LegRight] = "Legwear Right",
        [OverrideCosmeticType.LegLeft] = "Legwear Left",
        [OverrideCosmeticType.FootRight] = "Footwear Right",
        [OverrideCosmeticType.FootLeft] = "Footwear Left",
        [OverrideCosmeticType.LegRightMesh] = "Leg R Mesh",
        [OverrideCosmeticType.LegLeftMesh] = "Leg L Mesh",
        [OverrideCosmeticType.World] = "World",
    };

    // Reverse-lookup: display label → OverrideCosmeticType (used in sub-slider onChange).
    internal static readonly Dictionary<string, OverrideCosmeticType> LabelToType =
        SubLabels.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
}
