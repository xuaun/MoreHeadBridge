// Second-level popup from "Shape Conditions →": one REPOToggle per condition type relevant to the cosmetic. Selections write straight into the shared HashSet (no Apply step); Done keeps, Cancel reverts to the open-time snapshot.

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticConditionsPopup
{
    // ── Layout ────────────────────────────────────────────────────────────────
    private const float PopupX = -120f;
    private const float TitleGap = 15f;
    private const float BtnTopGap = 10f;
    private const float BtnRowH = 30f;
    private const float BtnDoneX = 58f;
    private const float BtnCancelX = -137f;

    // ── Condition lists per cosmetic type ─────────────────────────────────────
    // Only the types relevant to each cosmetic slot are shown, sparing the user the full ~110-entry enum.
    private static readonly Dictionary<SemiFunc.CosmeticType, CosmeticCustomCondition.Type[]> TypeConditions = new()
    {
        [SemiFunc.CosmeticType.Hat] = new[]
        {
            CosmeticCustomCondition.Type.Hat_Shape_Wide,
            CosmeticCustomCondition.Type.Hat_Overlap_FaceTop,
            CosmeticCustomCondition.Type.Hat_Overlap_FaceBottom,
            CosmeticCustomCondition.Type.Hat_Push_Out_Eyebrows_Big,
            CosmeticCustomCondition.Type.Hat_Push_Out_Eyebrows_Small,
            CosmeticCustomCondition.Type.Hat_Hide_Right_Eyebrow,
            CosmeticCustomCondition.Type.HeadTop_Covered_TopMiddle,
            CosmeticCustomCondition.Type.EyeLeft_Disable,
            CosmeticCustomCondition.Type.EyeRight_Disable,
        },
        [SemiFunc.CosmeticType.HeadBottom] = new[]
        {
            CosmeticCustomCondition.Type.HeadBottom_Overlap_BodyTop,
            CosmeticCustomCondition.Type.HeadBottom_Protrude,
            CosmeticCustomCondition.Type.Player_LookDown_Restrict,
        },
        [SemiFunc.CosmeticType.Eyewear] = new[]
        {
            CosmeticCustomCondition.Type.Eyewear_Shape_Tall,
            CosmeticCustomCondition.Type.Eyewear_Shape_VeryTall_Inner,
            CosmeticCustomCondition.Type.Eyewear_Shape_VeryTall_Outer,
            CosmeticCustomCondition.Type.Eyewear_Overlap_FaceBottom,
            CosmeticCustomCondition.Type.Eyewear_Overlap_FaceBottom_Protrude,
            CosmeticCustomCondition.Type.Eyewear_Shape_EyesLeft,
            CosmeticCustomCondition.Type.Eyewear_Shape_EyesBoth,
            CosmeticCustomCondition.Type.EyeRight_Disable,
            CosmeticCustomCondition.Type.EyeLeft_Disable,
            CosmeticCustomCondition.Type.HeadTop_Covered_TopMiddle,
            CosmeticCustomCondition.Type.FaceTop_Covered_Left,
            CosmeticCustomCondition.Type.FaceTop_Covered_Right,
            CosmeticCustomCondition.Type.FaceTop_Covered_Middle,
        },
        [SemiFunc.CosmeticType.FaceTop] = new[]
        {
            CosmeticCustomCondition.Type.FaceTop_Shape_Huge,
            CosmeticCustomCondition.Type.FaceTop_Shape_Tall,
            CosmeticCustomCondition.Type.FaceTop_Shape_VeryTall,
            CosmeticCustomCondition.Type.FaceTop_Overlap_FaceBottom,
            CosmeticCustomCondition.Type.FaceTop_Covered_Middle,
            CosmeticCustomCondition.Type.FaceTop_Covered_Left,
            CosmeticCustomCondition.Type.FaceTop_Covered_Right,
        },
        [SemiFunc.CosmeticType.FaceBottom] = new[]
        {
            CosmeticCustomCondition.Type.FaceBottom_Protrude,
        },
        [SemiFunc.CosmeticType.Ears] = new[]
        {
            CosmeticCustomCondition.Type.Ears_Shape_Tall,
        },
        [SemiFunc.CosmeticType.BodyTop] = new[]
        {
            CosmeticCustomCondition.Type.BodyTop_Overlap_HeadBottom,
            CosmeticCustomCondition.Type.BodyTop_Overlap_BodyBottom_Front,
            CosmeticCustomCondition.Type.Player_LookDown_Restrict,
        },
        [SemiFunc.CosmeticType.BodyBottom] = new[]
        {
            CosmeticCustomCondition.Type.BodyBottom_Protrude,
            CosmeticCustomCondition.Type.BodyBottom_ProtrudeFront,
        },
        [SemiFunc.CosmeticType.LegRight] = new[]
        {
            CosmeticCustomCondition.Type.FootRight_Shape_Long,
            CosmeticCustomCondition.Type.FootRight_Shape_Normal,
            CosmeticCustomCondition.Type.FootRight_Shape_Short,
            CosmeticCustomCondition.Type.FootRight_Protrude,
        },
        [SemiFunc.CosmeticType.LegLeft] = new[]
        {
            CosmeticCustomCondition.Type.FootLeft_Shape_Long,
            CosmeticCustomCondition.Type.FootLeft_Shape_Normal,
            CosmeticCustomCondition.Type.FootLeft_Shape_Short,
            CosmeticCustomCondition.Type.FootLeft_Protrude,
        },
        [SemiFunc.CosmeticType.FootRight] = new[]
        {
            CosmeticCustomCondition.Type.FootRight_Shape_Long,
            CosmeticCustomCondition.Type.FootRight_Shape_Normal,
            CosmeticCustomCondition.Type.FootRight_Shape_Short,
            CosmeticCustomCondition.Type.FootRight_Protrude,
        },
        [SemiFunc.CosmeticType.FootLeft] = new[]
        {
            CosmeticCustomCondition.Type.FootLeft_Shape_Long,
            CosmeticCustomCondition.Type.FootLeft_Shape_Normal,
            CosmeticCustomCondition.Type.FootLeft_Shape_Short,
            CosmeticCustomCondition.Type.FootLeft_Protrude,
        },
        [SemiFunc.CosmeticType.HeadTopMesh] = new[]
        {
            CosmeticCustomCondition.Type.HeadTopMesh_Shape_Big,
            CosmeticCustomCondition.Type.HeadTopMesh_Shape_Default,
            CosmeticCustomCondition.Type.HeadTopMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.HeadTopMesh_Shape_Unchanged,
            CosmeticCustomCondition.Type.HeadTopMesh_Shape_MissingRight,
            CosmeticCustomCondition.Type.EyeRight_Disable,
            CosmeticCustomCondition.Type.EyeLeft_Disable,
        },
        [SemiFunc.CosmeticType.HeadBottomMesh] = new[]
        {
            CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Default,
            CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Big,
            CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.EyeLidRightMesh] = new[]
        {
            CosmeticCustomCondition.Type.EyeLidRightMesh_Protrude,
        },
        [SemiFunc.CosmeticType.EyeLidLeftMesh] = new[]
        {
            CosmeticCustomCondition.Type.EyeLidLeftMesh_Protrude,
        },
        [SemiFunc.CosmeticType.BodyTopMesh] = new[]
        {
            CosmeticCustomCondition.Type.HeadBottom_OverrideFlat,
            CosmeticCustomCondition.Type.BodyTop_Overlap_HeadBottom,
            CosmeticCustomCondition.Type.BodyTopMesh_Shape_Default,
            CosmeticCustomCondition.Type.BodyTopMesh_Shape_Big,
            CosmeticCustomCondition.Type.BodyTopMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.BodyTopMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.BodyBottomMesh] = new[]
        {
            CosmeticCustomCondition.Type.BodyTop_OverrideFlat,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Default,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Wide,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tall,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Big,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.ArmRightMesh] = new[]
        {
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_Default,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_Big,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigUpper,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigLower,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_Huge,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.ArmRightMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.ArmLeftMesh] = new[]
        {
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Default,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Big,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigUpper,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigLower,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Huge,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.LegRightMesh] = new[]
        {
            CosmeticCustomCondition.Type.LegRightMesh_Shape_Default,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_Big,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_BigUpper,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_BigLower,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_Huge,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.LegRightMesh_Shape_Unchanged,
        },
        [SemiFunc.CosmeticType.LegLeftMesh] = new[]
        {
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_Default,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_Big,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigUpper,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigLower,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_Huge,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_Tiny,
            CosmeticCustomCondition.Type.LegLeftMesh_Shape_Unchanged,
        },
    };

    // Human-readable labels for every condition type that can appear in any list above.
    private static readonly Dictionary<CosmeticCustomCondition.Type, string> Labels = new()
    {
        // Hat
        [CosmeticCustomCondition.Type.Hat_Shape_Wide] = "Wide Shape",
        [CosmeticCustomCondition.Type.Hat_Overlap_FaceTop] = "Overlaps Face Top",
        [CosmeticCustomCondition.Type.Hat_Overlap_FaceBottom] = "Overlaps Face Bottom",
        [CosmeticCustomCondition.Type.Hat_Push_Out_Eyebrows_Big] = "Push Eyebrows (Big)",
        [CosmeticCustomCondition.Type.Hat_Push_Out_Eyebrows_Small] = "Push Eyebrows (Small)",
        [CosmeticCustomCondition.Type.Hat_Hide_Right_Eyebrow] = "Hide Right Eyebrow",
        [CosmeticCustomCondition.Type.HeadTop_Covered_TopMiddle] = "Covers Head Top",
        [CosmeticCustomCondition.Type.Player_LookDown_Restrict] = "Restrict Look Down",
        // HeadBottom
        [CosmeticCustomCondition.Type.HeadBottom_Overlap_BodyTop] = "Overlaps Body Top",
        [CosmeticCustomCondition.Type.HeadBottom_OverrideFlat] = "Override Flat",
        [CosmeticCustomCondition.Type.HeadBottom_State_Flat] = "Flat State",
        [CosmeticCustomCondition.Type.HeadBottom_Protrude] = "Protrudes",
        [CosmeticCustomCondition.Type.FaceBottom_Protrude] = "Face Bottom Protrudes",
        [CosmeticCustomCondition.Type.FaceBottom_Overlap_HeadBottom] = "Overlaps Head Bottom",
        // Eyewear
        [CosmeticCustomCondition.Type.Eyewear_Shape_Tall] = "Tall Shape",
        [CosmeticCustomCondition.Type.Eyewear_Shape_VeryTall_Inner] = "Very Tall (Inner)",
        [CosmeticCustomCondition.Type.Eyewear_Shape_VeryTall_Outer] = "Very Tall (Outer)",
        [CosmeticCustomCondition.Type.EyeLidRightMesh_Protrude] = "Right Eyelid Protrudes",
        [CosmeticCustomCondition.Type.EyeLidLeftMesh_Protrude] = "Left Eyelid Protrudes",
        [CosmeticCustomCondition.Type.Eyewear_Overlap_FaceTop] = "Overlaps Face Top",
        [CosmeticCustomCondition.Type.Eyewear_Overlap_FaceBottom] = "Overlaps Face Bottom",
        [CosmeticCustomCondition.Type.Eyewear_Overlap_FaceBottom_Protrude] = "Overlaps Face Bottom (Protruding)",
        [CosmeticCustomCondition.Type.Eyewear_Shape_EyesRight] = "Covers Right Eye",
        [CosmeticCustomCondition.Type.Eyewear_Shape_EyesLeft] = "Covers Left Eye",
        [CosmeticCustomCondition.Type.Eyewear_Shape_EyesBoth] = "Covers Both Eyes",
        [CosmeticCustomCondition.Type.EyeRight_Disable] = "Disable Right Eye",
        [CosmeticCustomCondition.Type.EyeLeft_Disable] = "Disable Left Eye",
        // FaceTop
        [CosmeticCustomCondition.Type.FaceTop_Shape_Huge] = "Huge Shape",
        [CosmeticCustomCondition.Type.FaceTop_Shape_Tall] = "Tall Shape",
        [CosmeticCustomCondition.Type.FaceTop_Shape_VeryTall] = "Very Tall Shape",
        [CosmeticCustomCondition.Type.FaceTop_Overlap_FaceBottom] = "Overlaps Face Bottom",
        [CosmeticCustomCondition.Type.FaceTop_Covered_Middle] = "Covers Middle",
        [CosmeticCustomCondition.Type.FaceTop_Covered_Left] = "Covers Left",
        [CosmeticCustomCondition.Type.FaceTop_Covered_Right] = "Covers Right",
        // FaceBottom
        // (FaceBottom_Protrude and FaceBottom_Overlap_HeadBottom already in HeadBottom above)
        // Ears
        [CosmeticCustomCondition.Type.Ears_Shape_Tall] = "Tall Shape",
        // BodyTop
        [CosmeticCustomCondition.Type.BodyTop_Overlap_HeadBottom] = "Overlaps Neck",
        [CosmeticCustomCondition.Type.BodyTop_Overlap_BodyBottom_Front] = "Overlaps Pants (Front)",
        [CosmeticCustomCondition.Type.BodyTop_Overlap_BodyBottom_Back] = "Overlaps Pants (Back)",
        [CosmeticCustomCondition.Type.BodyTop_OverrideFlat] = "Override Flat",
        [CosmeticCustomCondition.Type.BodyTop_State_Flat] = "Flat State",
        [CosmeticCustomCondition.Type.BodyTopMesh_Shape_Big] = "Wide Torso",
        [CosmeticCustomCondition.Type.BodyTopMesh_Shape_Tiny] = "Narrow Torso",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_Big] = "Right Arm: Big",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigUpper] = "Right Arm: Big Upper",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_BigLower] = "Right Arm: Big Lower",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_Huge] = "Right Arm: Huge",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_Tiny] = "Right Arm: Tiny",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Big] = "Left Arm: Big",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigUpper] = "Left Arm: Big Upper",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_BigLower] = "Left Arm: Big Lower",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Huge] = "Left Arm: Huge",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Tiny] = "Left Arm: Tiny",
        // BodyBottom
        [CosmeticCustomCondition.Type.BodyBottom_Protrude] = "Protrudes",
        [CosmeticCustomCondition.Type.BodyBottom_ProtrudeFront] = "Protrudes (Front)",
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Wide] = "Wide Shape",
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tall] = "Tall Shape",
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Big] = "Big Shape",
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Tiny] = "Narrow Shape",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_Big] = "Right Leg: Big",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_BigUpper] = "Right Leg: Big Upper",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_BigLower] = "Right Leg: Big Lower",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_Huge] = "Right Leg: Huge",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_Tiny] = "Right Leg: Tiny",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_Big] = "Left Leg: Big",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigUpper] = "Left Leg: Big Upper",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_BigLower] = "Left Leg: Big Lower",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_Huge] = "Left Leg: Huge",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_Tiny] = "Left Leg: Tiny",
        // Legs/Feet
        [CosmeticCustomCondition.Type.FootRight_Shape_Long] = "Long Foot",
        [CosmeticCustomCondition.Type.FootRight_Shape_Normal] = "Normal Foot",
        [CosmeticCustomCondition.Type.FootRight_Shape_Short] = "Short Foot",
        [CosmeticCustomCondition.Type.FootRight_Protrude] = "Foot Protrudes",
        [CosmeticCustomCondition.Type.FootLeft_Shape_Long] = "Long Foot",
        [CosmeticCustomCondition.Type.FootLeft_Shape_Normal] = "Normal Foot",
        [CosmeticCustomCondition.Type.FootLeft_Shape_Short] = "Short Foot",
        [CosmeticCustomCondition.Type.FootLeft_Protrude] = "Foot Protrudes",
        // HeadTopMesh
        [CosmeticCustomCondition.Type.HeadTopMesh_Shape_Big] = "Big Shape",
        [CosmeticCustomCondition.Type.HeadTopMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.HeadTopMesh_Shape_Tiny] = "Tiny Shape",
        [CosmeticCustomCondition.Type.HeadTopMesh_Shape_Unchanged] = "Unchanged Shape",
        [CosmeticCustomCondition.Type.HeadTopMesh_Shape_MissingRight] = "Missing Right Side",
        // HeadBottomMesh
        [CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Big] = "Big Shape",
        [CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Tiny] = "Tiny Shape",
        [CosmeticCustomCondition.Type.HeadBottomMesh_Shape_Unchanged] = "Unchanged Shape",
        // ArmRightMesh / ArmLeftMesh
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.ArmRightMesh_Shape_Unchanged] = "Unchanged Shape",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.ArmLeftMesh_Shape_Unchanged] = "Unchanged Shape",
        // BodyTopMesh
        [CosmeticCustomCondition.Type.BodyTopMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.BodyTopMesh_Shape_Unchanged] = "Unchanged Shape",
        // BodyBottomMesh
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.BodyBottomMesh_Shape_Unchanged] = "Unchanged Shape",
        // LegRightMesh / LegLeftMesh
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.LegRightMesh_Shape_Unchanged] = "Unchanged Shape",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_Default] = "Default Shape",
        [CosmeticCustomCondition.Type.LegLeftMesh_Shape_Unchanged] = "Unchanged Shape",
    };

    // ── Public entry point ────────────────────────────────────────────────────

    /// Opens the Condition Trigger Settings page on top of the main override popup.
    /// pendingCustomTypes is mutated directly; the caller reads it back when its own Save fires.
    // Build deferred to mouse-release: a fresh ACTIVE page would let a REPOSlider under the held click get scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(
        HashSet<CosmeticCustomCondition.Type> pendingCustomTypes,
        SemiFunc.CosmeticType cosmeticType,
        Action? onPreview = null,
        Transform? parentPopupTransform = null)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(
            pendingCustomTypes, cosmeticType, onPreview, parentPopupTransform));

    private static void ShowNow(
        HashSet<CosmeticCustomCondition.Type> pendingCustomTypes,
        SemiFunc.CosmeticType cosmeticType,
        Action? onPreview,
        Transform? parentPopupTransform)
    {
        if (!TypeConditions.TryGetValue(cosmeticType, out var conditions) || conditions.Length == 0)
        {
            // No relevant conditions for this type — nothing to show.
            return;
        }

        // Snapshot so Cancel can revert.
        var snapshot = new HashSet<CosmeticCustomCondition.Type>(pendingCustomTypes);

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: "Shape Conditions",
            shouldCachePage: false,
            pageDimmerVisibility: false,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup, parentPopupTransform);

        // ── One REPOToggle per relevant condition type ────────────────────────
        foreach (var condType in conditions)
        {
            var captured = condType; // capture for closure
            string label = Labels.TryGetValue(captured, out var lbl) ? lbl : captured.ToString();
            bool isOn = pendingCustomTypes.Contains(captured);

            popup.AddElementToScrollView(scrollView =>
            {
                var toggle = MenuAPI.CreateREPOToggle(
                    text: label,
                    onToggle: on =>
                    {
                        if (on) pendingCustomTypes.Add(captured);
                        else pendingCustomTypes.Remove(captured);
                        onPreview?.Invoke();
                    },
                    parent: scrollView,
                    defaultValue: isOn);
                return toggle.rectTransform;
            }, topPadding: condType == conditions[0] ? TitleGap : 0f);
        }

        // ── Buttons row: [Cancel]  [Done] ────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);

            MenuAPI.CreateREPOButton("Cancel", () =>
            {
                // Revert to snapshot and notify preview.
                pendingCustomTypes.Clear();
                foreach (var t in snapshot) pendingCustomTypes.Add(t);
                onPreview?.Invoke();
                popup.ClosePage(false);
            }, row, new Vector2(BtnCancelX, 0f));

            MenuAPI.CreateREPOButton("Done", () =>
                popup.ClosePage(false),
                row, new Vector2(BtnDoneX, 0f));

            return row;
        }, topPadding: BtnTopGap);

        popup.OpenPage(openOnTop: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Returns the conditions array for the given type, or empty if none defined.
    internal static bool HasConditions(SemiFunc.CosmeticType type)
        => TypeConditions.ContainsKey(type);

    /// The hide/custom condition types valid for a cosmetic type (empty if none).
    /// Single source of truth, consulted via CosmeticTriggerCatalog by the prune-on-save check.
    internal static IReadOnlyList<CosmeticCustomCondition.Type> ValidCustomTypes(SemiFunc.CosmeticType type)
        => TypeConditions.TryGetValue(type, out var arr) ? arr : System.Array.Empty<CosmeticCustomCondition.Type>();
}
