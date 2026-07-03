namespace MoreHeadBridge;

// User-facing Mini-Semibot knobs — popup-only (Shift+click), persisted via MiniSemibotVisualPrefs, nothing in the BepInEx config. All LOCAL: they shape how minis look/behave on THIS client (including remote players' minis).
internal static class MiniSemibotSettings
{
    internal static MiniSemibotPosition Position
        => MiniSemibotVisualPrefs.Position;

    internal static MiniSemibotDeathBehavior DeathBehavior
        => MiniSemibotVisualPrefs.DeathBehavior;

    internal static MiniSemibotOutfitMode OutfitMode
        => MiniSemibotVisualPrefs.OutfitMode;

    // Multiplier on the mini's animator speed while moving (walk/sprint/slide), so its little legs scurry faster — selling the "hurrying to keep up" look.
    internal static float LegSpeedMultiplier
        => MiniSemibotVisualPrefs.LegSpeed;

    // How the menu mini's gaze behaves in the ESC / Cosmetics menus. Popup-only (persisted in MiniSemibotVisualPrefs, not the BepInEx config).
    internal static MiniSemibotLookAt LookAt
        => MiniSemibotVisualPrefs.LookAt;

    // What the mini shows in its hand while you grab / map. Popup-only (see MiniSemibotVisualPrefs).
    internal static MiniSemibotGrabberVisual GrabberVisual
        => MiniSemibotVisualPrefs.Grabber;

    // How the mini aims its head in-game: at the same world point the wearer looks at, or copying the wearer's head bone rotation. Popup-only (see MiniSemibotVisualPrefs).
    internal static MiniSemibotGaze Gaze
        => MiniSemibotVisualPrefs.Gaze;

    // How the mini moves its mouth in-game. Popup-only (see MiniSemibotVisualPrefs).
    internal static MiniSemibotMouthMode MouthMode
        => MiniSemibotVisualPrefs.MouthMode;

    // Where the mini's grab-beam colour comes from (only meaningful with CustomGrabColor installed). Popup-only (see MiniSemibotVisualPrefs).
    internal static MiniSemibotBeamColor BeamColor
        => MiniSemibotVisualPrefs.BeamColor;

    // ── Mimic-clip tuning (only used by the MimicClips mouth mode) ───────────────
    // How often the mini picks a new clip. Maps a tier to a (min, max) seconds-between-clips range.
    internal static (float min, float max) MimicChatterDelay
        => ChatterToDelay(MiniSemibotVisualPrefs.MimicChatter);

    // Cadence tiers calibrated around the Mimic mod's defaults (MinDelay 30s / MaxDelay 120s): Moderate mirrors that, Little is rarer, Lots is chattier.
    internal static (float min, float max) ChatterToDelay(MiniSemibotMimicChatter c) => c switch
    {
        MiniSemibotMimicChatter.Little => (60f, 150f),
        MiniSemibotMimicChatter.Lots   => (12f, 35f),
        _                         => (30f, 120f),   // Moderate (default — Mimic's own default)
    };

    // Playback volume of the mini's clips on every client.
    internal static float MimicVolume => VolumeToValue(MiniSemibotVisualPrefs.MimicVolume);

    internal static float VolumeToValue(MiniSemibotMimicVolume v) => v switch
    {
        MiniSemibotMimicVolume.Low  => 0.4f,
        MiniSemibotMimicVolume.High => 1f,
        _                      => 0.7f,   // Medium (default)
    };

    // Max audible distance (3D rolloff) of the mini's clips.
    internal static float MimicRange => RangeToValue(MiniSemibotVisualPrefs.MimicRange);

    internal static float RangeToValue(MiniSemibotMimicRange r) => r switch
    {
        MiniSemibotMimicRange.Near => 10f,
        MiniSemibotMimicRange.Far  => 40f,
        _                     => 20f,   // Medium (default)
    };

    // The base scale tuned for the offsets / default look (the original "Child" size).
    internal const float BaseScale = 0.33f;

    // The LOCAL player's mini body scale, from the popup size tier.
    internal static float Scale => ScaleForSize(MiniSemibotVisualPrefs.Size);

    // Maps a size tier to its world scale. Shared by the local accessor and the network sync, so a remote player's chosen tier resolves to the same scale on every client.
    internal static float ScaleForSize(MiniSemibotSize size) => size switch
    {
        MiniSemibotSize.Baby => 0.22f,
        MiniSemibotSize.Teen => 0.45f,
        MiniSemibotSize.Junior => 0.65f,
        _               => BaseScale,   // Child (default)
    };
}

// How big the mini is. Discrete tiers (the popup uses string options). Values map in MiniSemibotSettings.Scale.
public enum MiniSemibotSize
{
    Baby,    // ~0.22 — tiny "pet"
    Child,   // 0.33  — the original default
    Teen,    // ~0.45 — noticeably bigger
    Junior,  // ~0.65 — clearly smaller than you, but a real "buddy"
}

// Menu gaze behaviour. Only relevant in the ESC / Cosmetics menus (there's no cursor in gameplay).
public enum MiniSemibotLookAt
{
    Still,   // mini keeps a neutral idle gaze
    Copy,    // mirror the big menu avatar's eyes + head bones (imitation; slightly off due to offset)
    Mouse,   // actually aim the mini's eyes at the cursor from its own position (default, correct)
}

// How the mini aims its head while in-game (gameplay, not menus).
public enum MiniSemibotGaze
{
    SameTarget,  // aim the mini's head at the SAME world point the wearer looks at, from the mini's own
                 // position (two beings looking at the same thing — default)
    CopyHead,    // copy the wearer's head bone rotation verbatim (identical relative angle — "mirror")
}

// How the mini moves its mouth while in-game.
public enum MiniSemibotMouthMode
{
    Never,       // mouth stays shut
    Random,      // procedural idle "chatter" — random talk bursts, no audio, no dependency
    WhenITalk,   // mirror the wearer's real voice loudness (default — looks like it talks when you do)
    MimicClips,  // play random clips the Mimic mod recorded + drive the mouth (only with Mimic installed)
}

// Where the mini's grab-beam colour comes from. Only exposed/used with CustomGrabColor installed (without it the beam keeps its plain faded-white look).
public enum MiniSemibotBeamColor
{
    SameAsPlayer,  // match the wearer's own beam colour (their CustomGrabColor choice) — default
    MiniGrabber,   // use the mini's OWN grabber cosmetic colour (slot 13 of its outfit)
}

// How chatty the mini is in MimicClips mode (cadence between clips).
public enum MiniSemibotMimicChatter
{
    Little,     // long gaps between clips
    Moderate,   // default
    Lots,       // frequent clips
}

// Playback volume tier of the mini's mimic clips.
public enum MiniSemibotMimicVolume
{
    Low,
    Medium,     // default
    High,
}

// How far the mini's mimic clips can be heard (3D rolloff distance).
public enum MiniSemibotMimicRange
{
    Near,
    Medium,     // default
    Far,
}

// What the mini holds when you grab an item / open the map.
public enum MiniSemibotGrabberVisual
{
    CleanArm,   // just the raised arm, no grabber orb (default)
    Orb,        // show the grabber orb/claw mesh, no light + copy the map model into its hand
    OrbLight,   // show the grabber orb/claw mesh WITH its light + copy the map model into its hand
}

// Where the mini sits. Both options face the wearer's way (you see its back) — never moonwalking. Public: type parameter of a public ConfigEntry<>.
public enum MiniSemibotPosition
{
    Behind,   // trails behind the wearer (default)
    Front,    // walks ahead of the wearer
}

// What the mini does while the wearer is dead / downed.
public enum MiniSemibotDeathBehavior
{
    DeathHead,    // slumps at the wearer's death-head location
    CrouchWait,   // crouches in place next to where the wearer fell, waiting (default)
    Hide,         // disappears until the wearer is revived
}

// Where the mini's outfit comes from.
public enum MiniSemibotOutfitMode
{
    SameAsPlayer,  // mirrors the wearer's live outfit (default)
    RandomPreset,  // a random saved cosmetics preset, re-rolled each time Mini-Semibot is equipped
}
