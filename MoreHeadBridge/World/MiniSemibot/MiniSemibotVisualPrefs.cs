using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;

namespace MoreHeadBridge;

// Persistent store for the popup-only Mini-Semibot knobs (intentionally NOT BepInEx config). Saved to BepInEx/config/MoreHeadBridge/MiniSemibot.json.
internal static class MiniSemibotVisualPrefs
{
    private sealed class Data
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotLookAt LookAt { get; set; } = MiniSemibotLookAt.Mouse;

        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotGrabberVisual Grabber { get; set; } = MiniSemibotGrabberVisual.CleanArm;

        // The rolled RandomPreset slot. Persisted so menu, in-game AND future sessions dress from the SAME preset (an in-memory static diverged across restarts). -1 = not rolled.
        public int RolledPreset { get; set; } = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotSize Size { get; set; } = MiniSemibotSize.Child;

        // In-game gaze: aim at the same world point the wearer looks at (default) vs copy the head bone.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotGaze Gaze { get; set; } = MiniSemibotGaze.SameTarget;

        // In-game mouth animation. Default mirrors the wearer's real voice.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotMouthMode MouthMode { get; set; } = MiniSemibotMouthMode.WhenITalk;

        // Grab-beam colour source (only used with CustomGrabColor). Default matches the wearer's beam.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotBeamColor BeamColor { get; set; } = MiniSemibotBeamColor.SameAsPlayer;

        // MimicClips tuning.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotMimicChatter MimicChatter { get; set; } = MiniSemibotMimicChatter.Moderate;

        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotMimicVolume MimicVolume { get; set; } = MiniSemibotMimicVolume.Medium;

        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotMimicRange MimicRange { get; set; } = MiniSemibotMimicRange.Medium;

        // When true, a small copy of the mini peeks into the facial-expression preview panel (5-9 keys HUD), mirroring the expression. Purely local eye-candy; default on.
        public bool ShowInExpressionPreview { get; set; } = true;

        // Hide the in-world mini on the "King of the Losers" kart arena (cramped; the mini clipped onto the kart). Local viewer preference; default true.
        public bool HideInArena { get; set; } = true;

        // How the mini eases toward its follow position. Off = rigidly glued (original); Soft = smooth lag; Springy = a bouncy overshoot on move/turn. Local visual feel; default Off.
        [JsonConverter(typeof(StringEnumConverter))]
        public FollowSpringMode FollowSpring { get; set; } = FollowSpringMode.Off;

        // When true, the in-game mini gently glances around while the wearer is idle, so it reads as "alive" instead of frozen. Local visual feel; default on.
        public bool IdleGlance { get; set; } = true;

        // Mirror the wearer's hurt/heal/upgrade material flash on the mini's body. Local; default on.
        public bool StateEffects { get; set; } = true;

        // Light footstep sound at the mini's feet whenever the wearer steps. Local; default off (per-mini audio could get noisy in a full lobby).
        public bool FootstepSounds { get; set; } = false;

        // Give the mini its own flashlight whenever the wearer's is out (automatic, NOT tied to the grabber/map). Local viewer preference; default on.
        public bool MiniFlashlight { get; set; } = true;

        // Clamp the follow spot by level geometry (walls / stairs / ledges). Local visual feel for every mini on this client; default on.
        public bool AvoidWalls { get; set; } = true;

        // ── Behaviour knobs (moved out of the BepInEx config; popup-only by request) ──
        // Where the mini sits relative to you (Behind trails, Front leads). Default Behind.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotPosition Position { get; set; } = MiniSemibotPosition.Behind;

        // What the mini does while you're dead/downed. Default CrouchWait.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotDeathBehavior DeathBehavior { get; set; } = MiniSemibotDeathBehavior.CrouchWait;

        // Where the mini's outfit comes from (your live outfit vs a random saved preset). Default SameAsPlayer.
        [JsonConverter(typeof(StringEnumConverter))]
        public MiniSemibotOutfitMode OutfitMode { get; set; } = MiniSemibotOutfitMode.SameAsPlayer;

        // Animator-speed multiplier for the mini's legs while moving (sells the "hurrying to keep up" look). 1 = same as the player. Default 1.4.
        public float LegSpeed { get; set; } = 1.4f;

        // Legacy (pre preset-style photo): the icon is now ALWAYS a captured photo (initial = reset colours, Recapture = player's colours). Kept only so older MiniSemibot.json files load.
        public bool IconFromAvatar { get; set; } = true;
    }

    private static readonly string SavePath = BridgePaths.Of("MiniSemibot.json");

    private static Data _data = new();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(SavePath))
                _data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(SavePath)) ?? new Data();
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"Mini-Semibot prefs load failed: {ex.Message}");
            _data = new Data();
        }
    }

    internal static MiniSemibotLookAt LookAt
    {
        get { EnsureLoaded(); return _data.LookAt; }
        set { EnsureLoaded(); _data.LookAt = value; Save(); }
    }

    internal static MiniSemibotGrabberVisual Grabber
    {
        get { EnsureLoaded(); return _data.Grabber; }
        set { EnsureLoaded(); _data.Grabber = value; Save(); }
    }

    // Persisted RandomPreset slot (-1 = none). Read/written by MiniSemibotSpawner's roll logic.
    internal static int RolledPreset
    {
        get { EnsureLoaded(); return _data.RolledPreset; }
        set { EnsureLoaded(); if (_data.RolledPreset == value) return; _data.RolledPreset = value; Save(); }
    }

    internal static MiniSemibotSize Size
    {
        get { EnsureLoaded(); return _data.Size; }
        set { EnsureLoaded(); if (_data.Size == value) return; _data.Size = value; Save(); }
    }

    internal static MiniSemibotGaze Gaze
    {
        get { EnsureLoaded(); return _data.Gaze; }
        set { EnsureLoaded(); if (_data.Gaze == value) return; _data.Gaze = value; Save(); }
    }

    internal static MiniSemibotMouthMode MouthMode
    {
        get { EnsureLoaded(); return _data.MouthMode; }
        set { EnsureLoaded(); if (_data.MouthMode == value) return; _data.MouthMode = value; Save(); }
    }

    internal static MiniSemibotBeamColor BeamColor
    {
        get { EnsureLoaded(); return _data.BeamColor; }
        set { EnsureLoaded(); if (_data.BeamColor == value) return; _data.BeamColor = value; Save(); }
    }

    internal static MiniSemibotMimicChatter MimicChatter
    {
        get { EnsureLoaded(); return _data.MimicChatter; }
        set { EnsureLoaded(); if (_data.MimicChatter == value) return; _data.MimicChatter = value; Save(); }
    }

    internal static MiniSemibotMimicVolume MimicVolume
    {
        get { EnsureLoaded(); return _data.MimicVolume; }
        set { EnsureLoaded(); if (_data.MimicVolume == value) return; _data.MimicVolume = value; Save(); }
    }

    internal static MiniSemibotMimicRange MimicRange
    {
        get { EnsureLoaded(); return _data.MimicRange; }
        set { EnsureLoaded(); if (_data.MimicRange == value) return; _data.MimicRange = value; Save(); }
    }

    internal static bool ShowInExpressionPreview
    {
        get { EnsureLoaded(); return _data.ShowInExpressionPreview; }
        set { EnsureLoaded(); if (_data.ShowInExpressionPreview == value) return; _data.ShowInExpressionPreview = value; Save(); }
    }

    internal static bool HideInArena
    {
        get { EnsureLoaded(); return _data.HideInArena; }
        set { EnsureLoaded(); if (_data.HideInArena == value) return; _data.HideInArena = value; Save(); }
    }

    internal static FollowSpringMode FollowSpring
    {
        get { EnsureLoaded(); return _data.FollowSpring; }
        set { EnsureLoaded(); if (_data.FollowSpring == value) return; _data.FollowSpring = value; Save(); }
    }

    internal static bool IdleGlance
    {
        get { EnsureLoaded(); return _data.IdleGlance; }
        set { EnsureLoaded(); if (_data.IdleGlance == value) return; _data.IdleGlance = value; Save(); }
    }

    internal static bool StateEffects
    {
        get { EnsureLoaded(); return _data.StateEffects; }
        set { EnsureLoaded(); if (_data.StateEffects == value) return; _data.StateEffects = value; Save(); }
    }

    internal static bool FootstepSounds
    {
        get { EnsureLoaded(); return _data.FootstepSounds; }
        set { EnsureLoaded(); if (_data.FootstepSounds == value) return; _data.FootstepSounds = value; Save(); }
    }

    internal static bool MiniFlashlight
    {
        get { EnsureLoaded(); return _data.MiniFlashlight; }
        set { EnsureLoaded(); if (_data.MiniFlashlight == value) return; _data.MiniFlashlight = value; Save(); }
    }

    internal static bool AvoidWalls
    {
        get { EnsureLoaded(); return _data.AvoidWalls; }
        set { EnsureLoaded(); if (_data.AvoidWalls == value) return; _data.AvoidWalls = value; Save(); }
    }

    internal static MiniSemibotPosition Position
    {
        get { EnsureLoaded(); return _data.Position; }
        set { EnsureLoaded(); if (_data.Position == value) return; _data.Position = value; Save(); }
    }

    internal static MiniSemibotDeathBehavior DeathBehavior
    {
        get { EnsureLoaded(); return _data.DeathBehavior; }
        set { EnsureLoaded(); if (_data.DeathBehavior == value) return; _data.DeathBehavior = value; Save(); }
    }

    internal static MiniSemibotOutfitMode OutfitMode
    {
        get { EnsureLoaded(); return _data.OutfitMode; }
        set { EnsureLoaded(); if (_data.OutfitMode == value) return; _data.OutfitMode = value; Save(); }
    }

    internal static float LegSpeed
    {
        get { EnsureLoaded(); return _data.LegSpeed; }
        set { EnsureLoaded(); if (_data.LegSpeed == value) return; _data.LegSpeed = value; Save(); }
    }

    internal static bool IconFromAvatar
    {
        get { EnsureLoaded(); return _data.IconFromAvatar; }
        set { EnsureLoaded(); if (_data.IconFromAvatar == value) return; _data.IconFromAvatar = value; Save(); }
    }

    private static void Save()
    {
        try { AtomicJson.Write(SavePath, JsonConvert.SerializeObject(_data, Formatting.Indented)); }
        catch (Exception ex) { BceConsole.LogWarning($"Mini-Semibot prefs save failed: {ex.Message}"); }
    }
}
