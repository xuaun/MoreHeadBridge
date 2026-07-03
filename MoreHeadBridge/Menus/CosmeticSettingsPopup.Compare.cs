// Value comparisons for the Sync Customizer preview: "= same" shows only when the actual values
// match (not just the count), so a same-length list with different content reads as a difference.

using HarmonyLib;
using MenuLib;
using MenuLib.MonoBehaviors;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal static partial class CosmeticSettingsPopup
{
    private const float FloatEps = 1e-4f;
    private static bool Approx(float a, float b) => System.Math.Abs(a - b) <= FloatEps;

    private static bool OffsetsEqual(List<CosmeticOffsetEntry>? a, List<CosmeticOffsetEntry>? b)
    {
        int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;

        // Order-independent: every entry in a must match an as-yet-unused entry in b (counts are equal).
        var used = new bool[cb];
        foreach (var ea in a!)
        {
            bool found = false;
            for (int j = 0; j < cb; j++)
            {
                if (used[j] || !OffsetEntryEqual(ea, b![j])) continue;
                used[j] = found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool OffsetEntryEqual(CosmeticOffsetEntry x, CosmeticOffsetEntry y)
        => x.TriggerType == y.TriggerType
           && Approx(x.PosX, y.PosX) && Approx(x.PosY, y.PosY) && Approx(x.PosZ, y.PosZ)
           && Approx(x.RotX, y.RotX) && Approx(x.RotY, y.RotY) && Approx(x.RotZ, y.RotZ)
           && Approx(x.ScaleX, y.ScaleX) && Approx(x.ScaleY, y.ScaleY) && Approx(x.ScaleZ, y.ScaleZ)
           && Approx(x.LerpSpeed, y.LerpSpeed);

    private static bool EnumSetEqual<T>(List<T>? a, List<T>? b) where T : struct, Enum
    {
        int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;
        return new HashSet<T>(a!).SetEquals(b!);
    }

    private static bool FloorPoseEqual(DeathHeadFloorPose a, DeathHeadFloorPose b)
    {
        // Effective "react while dead" folds the legacy Enabled flag (old configs were death-head only).
        bool aDead = a.ReactWhenDead || a.Enabled == true;
        bool bDead = b.ReactWhenDead || b.Enabled == true;
        return a.ReactWhenAlive == b.ReactWhenAlive && aDead == bDead
           && Approx(a.PosX, b.PosX) && Approx(a.PosY, b.PosY) && Approx(a.PosZ, b.PosZ)
           && Approx(a.RotX, b.RotX) && Approx(a.RotY, b.RotY) && Approx(a.RotZ, b.RotZ)
           && Approx(a.ScaleX, b.ScaleX) && Approx(a.ScaleY, b.ScaleY) && Approx(a.ScaleZ, b.ScaleZ)
           && Approx(a.LerpSpeed, b.LerpSpeed);
    }

    private static bool HideEqual(CosmeticHideConfig a, CosmeticHideConfig b)
        => EnumSetEqual(a.WhenTypes, b.WhenTypes)
           && EnumSetEqual(a.WhenConditions, b.WhenConditions)
           && EnumSetEqual(a.WhenPoses, b.WhenPoses)
           && StringSetEqual(a.WhenCosmetics, b.WhenCosmetics);

    private static bool StringSetEqual(List<string>? a, List<string>? b)
    {
        int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;
        return new HashSet<string>(a!).SetEquals(b!);
    }

    // Total active hide rules across all four lists.
    private static int HideRuleCount(CosmeticHideConfig h)
        => (h.WhenTypes?.Count ?? 0) + (h.WhenConditions?.Count ?? 0)
           + (h.WhenPoses?.Count ?? 0) + (h.WhenCosmetics?.Count ?? 0);
}
