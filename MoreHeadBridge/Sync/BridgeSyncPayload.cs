using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreHeadBridge;

/// Slim sync payload broadcast via Photon — only the fields remote clients actually consume.
/// Fields like Rarity, IsModded, FixCollider, etc. are local-only preferences and are excluded.
internal sealed class BridgeSyncPayload
{
    // ADD-OVERRIDE-FIELD: if the field must reach remote clients, add it here AND in FromOverrideData + ToOverrideData + ClampValues below.
    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public OverrideCosmeticType? Type { get; set; }

    /// Sway intensity — affects remote cosmetic rendering, so it must be synced.
    [JsonProperty("enableSway")]
    [JsonConverter(typeof(NullableSwayModeConverter))]
    public SwayMode? EnableSway { get; set; }

    [JsonProperty("customTypes")]
    public List<CosmeticCustomCondition.Type>? CustomTypes { get; set; }

    [JsonProperty("offsets")]
    public List<CosmeticOffsetEntry>? Offsets { get; set; }

    /// Crown target configuration — affects remote cosmetic rendering, so it must be synced.
    [JsonProperty("crown")]
    public CosmeticCrownConfig? Crown { get; set; }

    /// Loop animation override — determines whether the cosmetic's Animator loops on remote clients.
    [JsonProperty("fixAnim")]
    public bool? FixAnimation { get; set; }

    /// Tintable override — when false, remote clients must NOT inject BridgeTintMaterial
    /// (so default material colour shows instead of the per-type colour).
    [JsonProperty("tintable")]
    public bool? Tintable { get; set; }

    /// Death-head visibility — when false, remote clients must hide this cosmetic on the
    /// owner's in-game death head. null = shown (default).
    [JsonProperty("showOnDeathHead", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ShowOnDeathHead { get; set; }

    /// Death-head "floor pose" — affects how the remote cosmetic reacts to the ground, so it
    /// must be synced. null = no reaction (default).
    [JsonProperty("floorPose", NullValueHandling = NullValueHandling.Ignore)]
    public DeathHeadFloorPose? FloorPose { get; set; }

    /// Per-cosmetic "hide-self" rules — affects whether the remote cosmetic shows, so it must be
    /// synced. null = never auto-hide (default).
    [JsonProperty("hide", NullValueHandling = NullValueHandling.Ignore)]
    public CosmeticHideConfig? HideConditions { get; set; }

    /// World-cosmetic "Avoid Walls" — affects where the cosmetic floats on remote screens, so it
    /// follows the OWNER's choice. Lives in WorldFollowPrefs (not the override store); it only
    /// rides this payload. null = off (default).
    [JsonProperty("avoidWalls", NullValueHandling = NullValueHandling.Ignore)]
    public bool? AvoidWalls { get; set; }

    /// World-cosmetic "Hide on Kart" — hides the cosmetic on every screen while the wearer is on a
    /// vehicle / the kart-arena level, so it follows the OWNER's choice. Lives in WorldFollowPrefs
    /// (not the override store); it only rides this payload. null = off (default).
    [JsonProperty("hideOnKart", NullValueHandling = NullValueHandling.Ignore)]
    public bool? HideOnKart { get; set; }

    /// The sync payload for a local override record — the ONLY place that decides which
    /// CosmeticOverrideData fields travel to remote clients.
    // ADD-OVERRIDE-FIELD: if the field must reach remote clients, copy it here (and see ToOverrideData/ClampValues below).
    internal static BridgeSyncPayload FromOverrideData(CosmeticOverrideData d) => new()
    {
        Type       = d.Type,
        EnableSway = d.EnableSway,
        CustomTypes = d.CustomTypes is { Count: > 0 } ? new(d.CustomTypes) : null,
        Offsets    = d.Offsets is { Count: > 0 } ? new(d.Offsets) : null,
        Crown      = d.Crown?.Clone(),
        FixAnimation = d.FixAnimation,
        Tintable   = d.Tintable,
        ShowOnDeathHead = d.ShowOnDeathHead,
        FloorPose  = d.FloorPose?.Clone(),
        HideConditions = d.HideConditions is { HasAny: true } ? d.HideConditions.Clone() : null,
    };

    /// Converts to a full CosmeticOverrideData for import into the local store.
    /// Fields not present in the sync payload remain null (use local defaults).
    // ADD-OVERRIDE-FIELD: map the new field here too, or "Import from player" silently drops it.
    internal CosmeticOverrideData ToOverrideData() => new()
    {
        Type = Type,
        EnableSway = EnableSway,
        CustomTypes = CustomTypes != null ? new(CustomTypes) : null,
        Offsets = Offsets != null ? new(Offsets) : null,
        Crown = Crown?.Clone(),
        FixAnimation = FixAnimation,
        Tintable = Tintable,
        ShowOnDeathHead = ShowOnDeathHead,
        FloorPose = FloorPose?.Clone(),
        HideConditions = HideConditions?.Clone(),
    };

    /// Clamps all numeric fields to safe ranges and caps list sizes.
    /// Called on every received remote payload before storing or applying it.
    // ADD-OVERRIDE-FIELD: any new field carrying numbers/lists/enums from the network MUST be clamped/validated here.
    internal void ClampValues()
    {
        const int MaxListSize = 20;

        // Drop values outside the defined enum ranges — untrusted network input could carry
        // arbitrary integers cast to these enums (which would slip past type-only checks).
        if (Type.HasValue && !Enum.IsDefined(typeof(OverrideCosmeticType), Type.Value))
            Type = null;
        if (EnableSway.HasValue && !Enum.IsDefined(typeof(SwayMode), EnableSway.Value))
            EnableSway = null;
        CustomTypes?.RemoveAll(t => !Enum.IsDefined(typeof(CosmeticCustomCondition.Type), t));
        Offsets?.RemoveAll(e => !Enum.IsDefined(typeof(CosmeticCustomCondition.Type), e.TriggerType));

        if (HideConditions != null)
        {
            HideConditions.WhenTypes?.RemoveAll(t => !Enum.IsDefined(typeof(SemiFunc.CosmeticType), t));
            HideConditions.WhenConditions?.RemoveAll(t => !Enum.IsDefined(typeof(CosmeticCustomCondition.Type), t));
            HideConditions.WhenPoses?.RemoveAll(p => !Enum.IsDefined(typeof(PlayerAvatarVisuals.Pose), p));
            // WhenCosmetics is untrusted free-form names — drop empties and cap each length.
            HideConditions.WhenCosmetics?.RemoveAll(n => string.IsNullOrEmpty(n) || n.Length > 128);
            if (HideConditions.WhenTypes is { Count: > MaxListSize })
                HideConditions.WhenTypes.RemoveRange(MaxListSize, HideConditions.WhenTypes.Count - MaxListSize);
            if (HideConditions.WhenConditions is { Count: > MaxListSize })
                HideConditions.WhenConditions.RemoveRange(MaxListSize, HideConditions.WhenConditions.Count - MaxListSize);
            if (HideConditions.WhenPoses is { Count: > MaxListSize })
                HideConditions.WhenPoses.RemoveRange(MaxListSize, HideConditions.WhenPoses.Count - MaxListSize);
            if (HideConditions.WhenCosmetics is { Count: > MaxListSize })
                HideConditions.WhenCosmetics.RemoveRange(MaxListSize, HideConditions.WhenCosmetics.Count - MaxListSize);
            if (!HideConditions.HasAny) HideConditions = null;
        }

        if (CustomTypes != null && CustomTypes.Count > MaxListSize)
            CustomTypes.RemoveRange(MaxListSize, CustomTypes.Count - MaxListSize);

        if (Offsets != null)
        {
            if (Offsets.Count > MaxListSize)
                Offsets.RemoveRange(MaxListSize, Offsets.Count - MaxListSize);

            foreach (var e in Offsets)
            {
                e.PosX = Mathf.Clamp(e.PosX, -100f, 100f);
                e.PosY = Mathf.Clamp(e.PosY, -100f, 100f);
                e.PosZ = Mathf.Clamp(e.PosZ, -100f, 100f);
                e.RotX = Mathf.Clamp(e.RotX, -360f, 360f);
                e.RotY = Mathf.Clamp(e.RotY, -360f, 360f);
                e.RotZ = Mathf.Clamp(e.RotZ, -360f, 360f);
                e.ScaleX = Mathf.Clamp(e.ScaleX, 0.001f, 100f);
                e.ScaleY = Mathf.Clamp(e.ScaleY, 0.001f, 100f);
                e.ScaleZ = Mathf.Clamp(e.ScaleZ, 0.001f, 100f);
                e.LerpSpeed = Mathf.Clamp(e.LerpSpeed, 0.1f, 20f);
            }
        }

        if (Crown != null)
        {
            Crown.PosX = Mathf.Clamp(Crown.PosX, -100f, 100f);
            Crown.PosY = Mathf.Clamp(Crown.PosY, -100f, 100f);
            Crown.PosZ = Mathf.Clamp(Crown.PosZ, -100f, 100f);
            Crown.RotX = Mathf.Clamp(Crown.RotX, -360f, 360f);
            Crown.RotY = Mathf.Clamp(Crown.RotY, -360f, 360f);
            Crown.RotZ = Mathf.Clamp(Crown.RotZ, -360f, 360f);
            Crown.ScaleX = Mathf.Clamp(Crown.ScaleX, 0.001f, 100f);
            Crown.ScaleY = Mathf.Clamp(Crown.ScaleY, 0.001f, 100f);
            Crown.ScaleZ = Mathf.Clamp(Crown.ScaleZ, 0.001f, 100f);
            Crown.Priority = Mathf.Clamp(Crown.Priority, -999, 999);
        }

        if (FloorPose != null)
        {
            FloorPose.PosX = Mathf.Clamp(FloorPose.PosX, -100f, 100f);
            FloorPose.PosY = Mathf.Clamp(FloorPose.PosY, -100f, 100f);
            FloorPose.PosZ = Mathf.Clamp(FloorPose.PosZ, -100f, 100f);
            FloorPose.RotX = Mathf.Clamp(FloorPose.RotX, -360f, 360f);
            FloorPose.RotY = Mathf.Clamp(FloorPose.RotY, -360f, 360f);
            FloorPose.RotZ = Mathf.Clamp(FloorPose.RotZ, -360f, 360f);
            FloorPose.ScaleX = Mathf.Clamp(FloorPose.ScaleX, 0.001f, 100f);
            FloorPose.ScaleY = Mathf.Clamp(FloorPose.ScaleY, 0.001f, 100f);
            FloorPose.ScaleZ = Mathf.Clamp(FloorPose.ScaleZ, 0.001f, 100f);
            FloorPose.LerpSpeed = Mathf.Clamp(FloorPose.LerpSpeed, 0.1f, 20f);
        }
    }
}
