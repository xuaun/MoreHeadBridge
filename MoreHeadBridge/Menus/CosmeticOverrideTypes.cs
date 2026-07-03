// ============================================================================
// Data types, enums, and JSON converters for the cosmetic override system.
// Main persistence and application logic lives in CustomizerStore.cs.
// ============================================================================

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace MoreHeadBridge;

/// Equip animation behaviour for bridge cosmetics.
public enum VanillaEquipAnimationMode { Disabled, Normal, Fixed }

/// Sway intensity levels for bridge cosmetics.
/// Null in CosmeticOverrideData means no override (sway disabled by default for bridge cosmetics).
internal enum SwayMode { None, Light, Moderate, Strong }

/// User-visible cosmetic sub-category options for per-cosmetic overrides.
internal enum OverrideCosmeticType
{
    // HEAD
    Hat,
    HeadBottom,
    Ears,
    Eyewear,
    FaceTop,
    FaceBottom,
    // HEAD MESHES
    HeadTopMesh,
    HeadBottomMesh,
    EyeLidRightMesh,
    EyeLidLeftMesh,
    // BODY
    BodyTop,
    BodyBottom,
    BodyTopOverlay,
    BodyBottomOverlay,
    // BODY MESHES
    BodyTopMesh,
    BodyBottomMesh,
    // ARMS
    ArmRight,
    ArmLeft,
    ArmRightOverlay,
    ArmLeftOverlay,
    // ARM MESHES
    ArmRightMesh,
    ArmLeftMesh,
    // LEGS
    LegRight,
    LegLeft,
    FootRight,
    FootLeft,
    LegRightOverlay,
    LegLeftOverlay,
    // LEG MESHES
    LegRightMesh,
    LegLeftMesh,
    // WORLD
    World,
}

/// Top-level cosmetic category groups used in the override popup.
internal enum MainCosmeticCategory { Head, Body, Arms, Legs, World }

// ADD-OVERRIDE-FIELD: a new per-cosmetic override field touches ~6 sites — grep "ADD-OVERRIDE-FIELD" for all:
// (1) here, (2) BridgeSyncPayload field, (3) BridgeSyncPayload.ClampValues (REQUIRED — untrusted network input),
// (4) BridgeSyncPayload.FromOverrideData, (5) BridgeSyncPayload.ToOverrideData, (6) MoreHeadCosmeticMountPatch.Resolve,
// plus the popup (CosmeticOverridePopup BuildPending / DoSave).
internal sealed class CosmeticOverrideData
{
    /// null  = use global HighlightBridgeCosmetics setting.
    /// true  = always treat as modded (orange border, sort first).
    /// false = never treat as modded (vanilla appearance and sort position).
    [JsonProperty("isModded")]
    public bool? IsModded { get; set; }

    [JsonProperty("rarity")]
    [JsonConverter(typeof(StringEnumConverter))]
    public SemiFunc.Rarity? Rarity { get; set; }

    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public OverrideCosmeticType? Type { get; set; }

    /// null  = use global RemoveBridgePhysics setting.
    /// true  = always remove Collider/Rigidbody on this cosmetic.
    /// false = never remove Collider/Rigidbody on this cosmetic.
    [JsonProperty("fixCollider")]
    public bool? FixCollider { get; set; }

    /// null  = use global LoopBridgeAnimation setting.
    /// true  = always force-loop animations on this cosmetic.
    /// false = never force-loop animations on this cosmetic.
    [JsonProperty("fixAnimation")]
    public bool? FixAnimation { get; set; }

    /// null/false = no fix. true = inject an empty CosmeticPlayerCrown at setup, silencing vanilla's "has no CosmeticPlayerCrown" error — no targetMain, so no crown ever appears.
    /// Non-bridge modded only; bridge cosmetics manage their crown via ApplyCrownConfig.
    [JsonProperty("fixCrown")]
    public bool? FixCrown { get; set; }

    [JsonProperty("vanillaEquipAnimationMode")]
    [JsonConverter(typeof(StringEnumConverter))]
    public VanillaEquipAnimationMode? VanillaEquipAnimationMode { get; set; }

    /// null  = use global EnableBridgeTinting setting and auto-detected shader support.
    /// true  = always tintable regardless of shader or global setting.
    /// false = never tintable even if the shader supports it.
    [JsonProperty("tintable")]
    public bool? Tintable { get; set; }

    /// Null   = no override (bridge cosmetics have no sway by default).
    /// None   = explicitly disabled.
    /// Light/Moderate/Strong = enabled with given intensity (0.35×/1.0×/2.2× base force).
    [JsonProperty("enableSway")]
    [JsonConverter(typeof(NullableSwayModeConverter))]
    public SwayMode? EnableSway { get; set; }

    /// Custom condition types this cosmetic announces every frame via
    /// Cosmetic.CustomTypesLogic() → PlayerCosmetics.ConditionCustomSet().
    /// Null or empty = no custom types (default).
    [JsonProperty("customTypes")]
    public List<CosmeticCustomCondition.Type>? CustomTypes { get; set; }

    /// Runtime CosmeticOffsetCondition entries injected at instantiation time.
    /// Each entry moves/rotates this cosmetic's root when the trigger condition is active.
    /// Null or empty = no offset (default).
    [JsonProperty("offsets")]
    public List<CosmeticOffsetEntry>? Offsets { get; set; }

    /// Crown target for Hat/HeadTopMesh: injects a CosmeticPlayerCrown pointing at a "Crown Target Bridge" child at the configured transform. Null = no crown (default).
    [JsonProperty("crown")]
    public CosmeticCrownConfig? Crown { get; set; }

    /// Whether this cosmetic is shown on the in-game death head.
    /// null (or true) = shown (default); false = hidden on the death head only.
    /// Applies to bridge and (opt-in) modded non-bridge cosmetics; vanilla is unaffected.
    [JsonProperty("showOnDeathHead", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ShowOnDeathHead { get; set; }

    /// Optional "floor pose" the cosmetic lerps to when it contacts the ground on the death head.
    /// null = no reaction (default). Only takes effect while Enabled is true.
    [JsonProperty("floorPose", NullValueHandling = NullValueHandling.Ignore)]
    public DeathHeadFloorPose? FloorPose { get; set; }

    /// Optional "hide-self" rules: this cosmetic hides while another cosmetic type is equipped or a
    /// shape condition is active (mirrors vanilla CosmeticHideCondition). null = never auto-hide.
    [JsonProperty("hideConditions", NullValueHandling = NullValueHandling.Ignore)]
    public CosmeticHideConfig? HideConditions { get; set; }

    /// null  = use the global EnableBridgeCustomColors / EnableModdedCustomColors setting.
    /// true  = always show the custom-RGB "C" button for this cosmetic.
    /// false = never show it.
    [JsonProperty("enableCustomColors", NullValueHandling = NullValueHandling.Ignore)]
    public bool? EnableCustomColors { get; set; }

    /// null  = use the global EnableBridgeColorAnimations setting.
    /// true  = always show the animate-colour "A" button for this (bridge) cosmetic.
    /// false = never show it.
    [JsonProperty("enableColorAnimations", NullValueHandling = NullValueHandling.Ignore)]
    public bool? EnableColorAnimations { get; set; }

    /// null  = use the global UseIsolatedIconRender setting.
    /// true  = always render this cosmetic's icon in isolation (SemiIconMaker-style).
    /// false = always crop it from the avatar preview.
    [JsonProperty("useIsolatedIcon", NullValueHandling = NullValueHandling.Ignore)]
    public bool? UseIsolatedIcon { get; set; }

    /// null  = use the global UseVanillaPositionFixes setting.
    /// true  = always apply the automatic vanilla position/scale fixes to this cosmetic.
    /// false = never apply them (keep the authored size/position on every body shape).
    [JsonProperty("useFitOffsets", NullValueHandling = NullValueHandling.Ignore)]
    public bool? UseFitOffsets { get; set; }

    // ── Snapshot fields (non-bridge modded cosmetics only) ────────────────────
    // Written once on the first SetAndApply, never updated. Reset() restores these instead of bridge-loader defaults, which would overwrite the authoring mod's rarity/type.

    /// Original rarity of the asset at the time the first override was created.
    [JsonProperty("origRarity", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public SemiFunc.Rarity? OriginalRarity { get; set; }

    /// Original CosmeticType of the asset at the time the first override was created.
    [JsonProperty("origType", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public SemiFunc.CosmeticType? OriginalType { get; set; }

    /// Original tintable flag of the asset at the time the first override was created.
    /// Used by ApplyToAsset to restore the mod's default tintability when the user
    /// resets the Allow Coloring slider back to "Default".
    [JsonProperty("origTintable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? OriginalTintable { get; set; }
}

/// Crown target configuration injected at runtime on Hat/HeadTopMesh bridge cosmetics.
/// Controls where the vanilla PlayerCrown should appear when this cosmetic is equipped.
internal sealed class CosmeticCrownConfig
{
    [JsonProperty("posX")] public float PosX { get; set; }
    // Internal values are display + offset (0, +0.35, +0.25) so display (0,0,0) == bare-head default.
    [JsonProperty("posY")] public float PosY { get; set; } = 0.35f;
    [JsonProperty("posZ")] public float PosZ { get; set; } = 0.25f;
    [JsonProperty("rotX")] public float RotX { get; set; }
    [JsonProperty("rotY")] public float RotY { get; set; }
    [JsonProperty("rotZ")] public float RotZ { get; set; }
    [JsonProperty("scaleX")] public float ScaleX { get; set; } = 1f;
    [JsonProperty("scaleY")] public float ScaleY { get; set; } = 1f;
    [JsonProperty("scaleZ")] public float ScaleZ { get; set; } = 1f;

    /// Crown competition rank. Vanilla hats use 0; HeadTopMesh items use -25 / -40 / -50.
    /// Higher value wins (range -999 to +999; start value used by PlayerCrown is -999).
    [JsonProperty("priority")] public int Priority { get; set; }

    /// When true, the crown snaps directly to the target instead of spring-following it.
    [JsonProperty("disableSpring")] public bool DisableSpring { get; set; }

    /// Shallow copy — every field is a value type, so this is a full independent clone.
    internal CosmeticCrownConfig Clone() => (CosmeticCrownConfig)MemberwiseClone();

    /// Value equality with a float tolerance (eps ≤ 0 → exact). Handles null operands.
    internal static bool ValueEquals(CosmeticCrownConfig? a, CosmeticCrownConfig? b, float eps)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return Near(a.PosX, b.PosX, eps) && Near(a.PosY, b.PosY, eps) && Near(a.PosZ, b.PosZ, eps)
            && Near(a.RotX, b.RotX, eps) && Near(a.RotY, b.RotY, eps) && Near(a.RotZ, b.RotZ, eps)
            && Near(a.ScaleX, b.ScaleX, eps) && Near(a.ScaleY, b.ScaleY, eps) && Near(a.ScaleZ, b.ScaleZ, eps)
            && a.Priority == b.Priority && a.DisableSpring == b.DisableSpring;
    }

    private static bool Near(float x, float y, float eps)
        => eps <= 0f ? x == y : System.Math.Abs(x - y) <= eps;
}

/// Per-cosmetic "impact pose" (bridge CosmeticBlocked equivalent): springs to this local transform on contact — live body (ReactWhenAlive), death head (ReactWhenDead), or both.
/// Defaults are the NEUTRAL pose (scale 1, no offset) — matching vanilla transformBlocked medians,
/// which keep scale 1.0 and tuck via position instead. The user sculpts the pose from there.
internal sealed class DeathHeadFloorPose
{
    /// React while alive: the cosmetic springs to this pose when it pushes into a low ceiling /
    /// wall / enemy on the live player body.
    [JsonProperty("reactAlive")] public bool ReactWhenAlive { get; set; }

    /// React while dead: the cosmetic springs to this pose when it contacts the ground / walls on
    /// the in-game death head.
    [JsonProperty("reactDead")] public bool ReactWhenDead { get; set; }

    /// Legacy "React to Floor" flag (pre-impact-pose builds = death-head only). Migrated to
    /// ReactWhenDead via MigrateLegacy. Not written for new configs (null).
    [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)] public bool? Enabled { get; set; }

    [JsonProperty("posX")] public float PosX { get; set; }
    [JsonProperty("posY")] public float PosY { get; set; }
    [JsonProperty("posZ")] public float PosZ { get; set; }
    [JsonProperty("rotX")] public float RotX { get; set; }
    [JsonProperty("rotY")] public float RotY { get; set; }
    [JsonProperty("rotZ")] public float RotZ { get; set; }
    [JsonProperty("scaleX")] public float ScaleX { get; set; } = 1f;
    [JsonProperty("scaleY")] public float ScaleY { get; set; } = 1f;
    [JsonProperty("scaleZ")] public float ScaleZ { get; set; } = 1f;

    /// How fast the cosmetic lerps in/out of the pose (1–10).
    [JsonProperty("lerp")] public float LerpSpeed { get; set; } = 7f;

    /// Folds the legacy Enabled flag into ReactWhenDead (old configs were death-head only) and
    /// clears it so it is never written again. Call once after load / before editing.
    internal void MigrateLegacy()
    {
        if (Enabled == true && !ReactWhenAlive && !ReactWhenDead) ReactWhenDead = true;
        Enabled = null;
    }

    /// True when this pose drives any reaction.
    internal bool HasAny => ReactWhenAlive || ReactWhenDead || Enabled == true;

    /// Shallow copy — every field is a value type, so this is a full independent clone.
    internal DeathHeadFloorPose Clone() => (DeathHeadFloorPose)MemberwiseClone();
}

/// Per-cosmetic "hide-self" rules, mirroring vanilla CosmeticHideCondition. The cosmetic hides
/// (springs to scale 0) while ANY listed signal is active. Empty/null lists = never auto-hide.
internal sealed class CosmeticHideConfig
{
    /// Hide while a cosmetic whose type is in this list is equipped (anti-clipping with whole
    /// categories — e.g. hide hair when any Hat is on).
    [JsonProperty("whenTypes", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(EnumListConverter<SemiFunc.CosmeticType>))]
    public List<SemiFunc.CosmeticType>? WhenTypes { get; set; }

    /// Hide while one of these shape/state conditions is active on the player.
    [JsonProperty("whenConditions", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(EnumListConverter<CosmeticCustomCondition.Type>))]
    public List<CosmeticCustomCondition.Type>? WhenConditions { get; set; }

    /// Hide while the player's current pose is in this list (Stand/Crouch/Crawl/Tumble).
    [JsonProperty("whenPoses", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(EnumListConverter<PlayerAvatarVisuals.Pose>))]
    public List<PlayerAvatarVisuals.Pose>? WhenPoses { get; set; }

    /// Hide while a specific cosmetic is equipped, keyed by CosmeticAsset.name (works for vanilla too,
    /// unlike assetId). Absorbed from a modded cosmetic's native cosmeticList; read-only in the UI.
    [JsonProperty("whenCosmetics", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? WhenCosmetics { get; set; }

    /// True when there is at least one active rule.
    internal bool HasAny =>
        (WhenTypes is { Count: > 0 }) || (WhenConditions is { Count: > 0 })
        || (WhenPoses is { Count: > 0 }) || (WhenCosmetics is { Count: > 0 });

    internal CosmeticHideConfig Clone() => new()
    {
        WhenTypes = WhenTypes != null ? new List<SemiFunc.CosmeticType>(WhenTypes) : null,
        WhenConditions = WhenConditions != null ? new List<CosmeticCustomCondition.Type>(WhenConditions) : null,
        WhenPoses = WhenPoses != null ? new List<PlayerAvatarVisuals.Pose>(WhenPoses) : null,
        WhenCosmetics = WhenCosmetics != null ? new List<string>(WhenCosmetics) : null,
    };
}

/// Serialises a List&lt;TEnum&gt; as an array of enum names (so the JSON is human-readable and
/// stable across enum reordering). Mirrors how individual enums use StringEnumConverter.
internal sealed class EnumListConverter<TEnum> : JsonConverter where TEnum : struct, Enum
{
    public override bool CanConvert(Type objectType) => typeof(List<TEnum>).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        if (value is List<TEnum> list)
            foreach (var e in list) writer.WriteValue(e.ToString());
        writer.WriteEndArray();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var result = new List<TEnum>();
        if (reader.TokenType == JsonToken.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                if (reader.Value is string s && Enum.TryParse<TEnum>(s, out var parsed))
                    result.Add(parsed);
            }
        }
        return result;
    }
}

/// One conditional position/rotation/scale offset injected at runtime on a bridge cosmetic.
internal sealed class CosmeticOffsetEntry
{
    [JsonProperty("triggerType")]
    [JsonConverter(typeof(StringEnumConverter))]
    public CosmeticCustomCondition.Type TriggerType { get; set; }

    [JsonProperty("posX")] public float PosX { get; set; }
    [JsonProperty("posY")] public float PosY { get; set; }
    [JsonProperty("posZ")] public float PosZ { get; set; }
    [JsonProperty("rotX")] public float RotX { get; set; }
    [JsonProperty("rotY")] public float RotY { get; set; }
    [JsonProperty("rotZ")] public float RotZ { get; set; }
    [JsonProperty("scaleX")] public float ScaleX { get; set; } = 1f;
    [JsonProperty("scaleY")] public float ScaleY { get; set; } = 1f;
    [JsonProperty("scaleZ")] public float ScaleZ { get; set; } = 1f;
    [JsonProperty("lerpSpeed")] public float LerpSpeed { get; set; } = 3f;

    /// True for the built-in "fit" offsets seeded by OffsetSeedDefaults (NOT persisted). These are
    /// generic vanilla averages, so InjectOffsetConditions re-bases them onto the cosmetic's own
    /// authored default transform (vanilla CosmeticOffsetCondition snaps to absolute pos/rot/scale).
    /// User-configured offsets leave this false and stay absolute.
    [JsonIgnore] internal bool Seeded;

    /// Shallow copy — every field is a value type, so this is a full independent clone.
    internal CosmeticOffsetEntry Clone() => (CosmeticOffsetEntry)MemberwiseClone();
}
