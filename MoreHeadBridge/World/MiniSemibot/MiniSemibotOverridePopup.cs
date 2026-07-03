// ============================================================================
// Dedicated in-game popup for the Mini-Semibot world cosmetic (Shift+click it).
// The generic CosmeticOverridePopup targets mesh cosmetics — almost nothing applies to the meshless Mini-Semibot. This popup exposes only the knobs that make sense, backed by MiniSemibotVisualPrefs (popup-only), applied live.
// ============================================================================

using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;

namespace MoreHeadBridge;

internal static class MiniSemibotOverridePopup
{
    private const float PopupX = -120f;
    private const float TitleGap = 15f;

    private static readonly string[] PositionOptions = { "Behind", "Front" };
    private static readonly string[] DeathOptions    = { "Death Head", "Crouch & Wait", "Hide" };
    private static readonly string[] OutfitOptions   = { "Same As You", "Random Preset" };
    private static readonly string[] LegSpeedOptions = { "1.0x", "1.2x", "1.4x", "1.6x", "1.8x", "2.0x", "2.5x", "3.0x" };
    private static readonly string[] LookAtOptions   = { "At Mouse", "Copy Avatar", "Still" };
    private static readonly string[] HandsOptions    = { "Clean Arm", "Orb", "Orb + Light" };
    private static readonly string[] SizeOptions     = { "Baby", "Child", "Teen", "Junior" };
    private static readonly string[] GazeOptions     = { "Same Target", "Copy Head" };
    private static readonly string[] BeamOptions     = { "Same As You", "Mini-Semibot Grabber" };
    private static readonly string[] ChatterOptions  = { "Talks Little", "Moderate", "Talks Lots" };
    private static readonly string[] VolumeOptions   = { "Low", "Medium", "High" };
    private static readonly string[] RangeOptions    = { "Near", "Medium", "Far" };
    private static readonly string[] OnOffOptions    = { "Off", "On" };
    private static readonly string[] SpringOptions   = { "Off", "Soft", "Bouncy" };

    // Build deferred to mouse-release: a fresh page is ACTIVE before OpenPage, so a REPOSlider landing under the still-held click gets scrubbed (see PopupUI.AfterMouseRelease).
    internal static void Show(CosmeticAsset asset)
        => PopupUI.AfterMouseRelease(Plugin.Instance, () => ShowNow(asset));

    private static void ShowNow(CosmeticAsset asset)
    {
        string displayName = asset.assetName ?? asset.name ?? "Mini-Semibot";

        var popup = MenuAPI.CreateREPOPopupPage(
            headerText: displayName,
            shouldCachePage: false,
            pageDimmerVisibility: true,
            spacing: 5f,
            localPosition: new Vector2(PopupX, 0f));

        PopupUI.AttachGuards(popup);

        AddSectionLabel(popup, "Placement", TitleGap);

        // ── Position ──────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Position",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.Position = opt == "Front" ? MiniSemibotPosition.Front : MiniSemibotPosition.Behind;
                    Apply();
                },
                parent: scrollView,
                stringOptions: PositionOptions,
                defaultOption: MiniSemibotVisualPrefs.Position == MiniSemibotPosition.Front ? "Front" : "Behind");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Size ──────────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Size",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.Size = opt switch
                    {
                        "Baby" => MiniSemibotSize.Baby,
                        "Teen" => MiniSemibotSize.Teen,
                        "Junior" => MiniSemibotSize.Junior,
                        _      => MiniSemibotSize.Child,
                    };
                    // Scale is read live every frame, but push it out to other clients (owner-authoritative).
                    MiniSemibotSpawner.ApplyLiveSettings();
                },
                parent: scrollView,
                stringOptions: SizeOptions,
                defaultOption: MiniSemibotVisualPrefs.Size switch
                {
                    MiniSemibotSize.Baby => "Baby",
                    MiniSemibotSize.Teen => "Teen",
                    MiniSemibotSize.Junior => "Junior",
                    _               => "Child",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Outfit", 10f);

        // ── Outfit mode ───────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Outfit",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.OutfitMode = opt == "Random Preset"
                        ? MiniSemibotOutfitMode.RandomPreset : MiniSemibotOutfitMode.SameAsPlayer;
                    // Switching the mode here counts as a fresh choice → re-roll a new random preset.
                    MiniSemibotSpawner.ClearLocalRoll();
                    Apply();
                },
                parent: scrollView,
                stringOptions: OutfitOptions,
                defaultOption: MiniSemibotVisualPrefs.OutfitMode == MiniSemibotOutfitMode.RandomPreset
                    ? "Random Preset" : "Same As You");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Menu Gaze", 10f);

        // ── Look At (menu only) ───────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Look At",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.LookAt = opt switch
                    {
                        "Copy Avatar" => MiniSemibotLookAt.Copy,
                        "Still" => MiniSemibotLookAt.Still,
                        _ => MiniSemibotLookAt.Mouse,
                    };
                    // read live each frame — no re-apply needed
                },
                parent: scrollView,
                stringOptions: LookAtOptions,
                defaultOption: MiniSemibotVisualPrefs.LookAt switch
                {
                    MiniSemibotLookAt.Copy => "Copy Avatar",
                    MiniSemibotLookAt.Still => "Still",
                    _ => "At Mouse",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "In-Game Gaze", 10f);

        // ── In-game gaze (look-at) ────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Look At",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.Gaze = opt == "Copy Head"
                        ? MiniSemibotGaze.CopyHead : MiniSemibotGaze.SameTarget;
                    MiniSemibotSpawner.ApplyLiveSettings();   // owner-authoritative → broadcast
                },
                parent: scrollView,
                stringOptions: GazeOptions,
                defaultOption: MiniSemibotVisualPrefs.Gaze == MiniSemibotGaze.CopyHead ? "Copy Head" : "Same Target");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Movement", 10f);

        // ── Follow spring ─────────────────────────────────────────────────────
        // How the mini eases toward its follow spot: Off = rigidly glued (original), Soft = smooth lag, Springy = a bouncy overshoot on move/turn. Read live every frame; local visual feel.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Follow Smoothing",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.FollowSpring = opt switch
                    {
                        "Soft"   => FollowSpringMode.Soft,
                        "Bouncy" => FollowSpringMode.Springy,
                        _        => FollowSpringMode.Off,
                    };
                    // Read live by MiniSemibotFollow — no re-apply needed.
                },
                parent: scrollView,
                stringOptions: SpringOptions,
                defaultOption: MiniSemibotVisualPrefs.FollowSpring switch
                {
                    FollowSpringMode.Soft    => "Soft",
                    FollowSpringMode.Springy => "Bouncy",
                    _                        => "Off",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Avoid walls ───────────────────────────────────────────────────────
        // Clamps the follow spot by level geometry (squeeze against walls, stand on stairs, stop at ledges). Owner-authoritative: YOUR choice for YOUR mini on everyone's screen.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Avoid Walls",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.AvoidWalls = opt == "On";
                    MiniSemibotSync.BroadcastLocal();   // owner-authoritative → broadcast
                },
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.AvoidWalls ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Idle glance ───────────────────────────────────────────────────────
        // When on, the mini gently looks around while idle so it reads as "alive". Read live every frame; local visual feel.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Idle Glance",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.IdleGlance = opt == "On";
                    // Read live by MiniSemibotFollow — no re-apply needed.
                },
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.IdleGlance ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── State effects ─────────────────────────────────────────────────────
        // Mirror the wearer's hurt/heal/upgrade material flash on the mini's body. Read live.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "State Effects",
                description: "",
                onOptionChanged: opt => MiniSemibotVisualPrefs.StateEffects = opt == "On",
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.StateEffects ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Footstep sounds ───────────────────────────────────────────────────
        // Light footstep at the mini's feet whenever the wearer steps. Read live.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Footstep Sounds",
                description: "",
                onOptionChanged: opt => MiniSemibotVisualPrefs.FootstepSounds = opt == "On",
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.FootstepSounds ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Flashlight ────────────────────────────────────────────────────────
        // Mini gets its own flashlight automatically whenever yours is out. Read live.
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Flashlight",
                description: "",
                onOptionChanged: opt => MiniSemibotVisualPrefs.MiniFlashlight = opt == "On",
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.MiniFlashlight ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Face", 10f);

        // ── Mouth animation ───────────────────────────────────────────────────
        // "Mimic Clips" appears only when the Mimic mod is installed (it plays the clips Mimic records); the other three are always available.
        var mouthOptions = MiniSemibotModCompat.HasMimic
            ? new[] { "Never", "Random", "When I Talk", "Mimic Clips" }
            : new[] { "Never", "Random", "When I Talk" };

        // Mimic-clip tuning rows — only relevant while Mouth is "Mimic Clips", so they show/hide live with the Mouth slider.
        var mimicRows = new System.Collections.Generic.List<REPOScrollViewElement>();
        void UpdateMimicRows()
        {
            bool show = MiniSemibotVisualPrefs.MouthMode == MiniSemibotMouthMode.MimicClips;
            foreach (var el in mimicRows)
                if (el != null) el.visibility = show;
        }

        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Mouth",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.MouthMode = opt switch
                    {
                        "Never"       => MiniSemibotMouthMode.Never,
                        "Random"      => MiniSemibotMouthMode.Random,
                        "Mimic Clips" => MiniSemibotMouthMode.MimicClips,
                        _             => MiniSemibotMouthMode.WhenITalk,
                    };
                    UpdateMimicRows();
                    MiniSemibotSpawner.ApplyLiveSettings();   // owner-authoritative → broadcast
                },
                parent: scrollView,
                stringOptions: mouthOptions,
                defaultOption: MiniSemibotVisualPrefs.MouthMode switch
                {
                    MiniSemibotMouthMode.Never      => "Never",
                    MiniSemibotMouthMode.Random     => "Random",
                    MiniSemibotMouthMode.MimicClips => "Mimic Clips",
                    _                          => "When I Talk",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Mimic-clip tuning (cadence / volume / range) — only with the Mimic mod ─
        if (MiniSemibotModCompat.HasMimic)
        {
            RectTransform? chatterTr = null, volumeTr = null, rangeTr = null;

            // Cadence
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Chatter - Mimic",
                    description: "",
                    onOptionChanged: opt =>
                    {
                        MiniSemibotVisualPrefs.MimicChatter = opt switch
                        {
                            "Talks Little" => MiniSemibotMimicChatter.Little,
                            "Talks Lots"   => MiniSemibotMimicChatter.Lots,
                            _              => MiniSemibotMimicChatter.Moderate,
                        };
                        MiniSemibotSpawner.ApplyLiveSettings();
                    },
                    parent: scrollView,
                    stringOptions: ChatterOptions,
                    defaultOption: MiniSemibotVisualPrefs.MimicChatter switch
                    {
                        MiniSemibotMimicChatter.Little => "Talks Little",
                        MiniSemibotMimicChatter.Lots   => "Talks Lots",
                        _                         => "Moderate",
                    });
                return chatterTr = (RectTransform)slider.transform;
            }, topPadding: 10f);

            // Volume
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Voice Volume - Mimic",
                    description: "",
                    onOptionChanged: opt =>
                    {
                        MiniSemibotVisualPrefs.MimicVolume = opt switch
                        {
                            "Low"  => MiniSemibotMimicVolume.Low,
                            "High" => MiniSemibotMimicVolume.High,
                            _      => MiniSemibotMimicVolume.Medium,
                        };
                        MiniSemibotSpawner.ApplyLiveSettings();
                    },
                    parent: scrollView,
                    stringOptions: VolumeOptions,
                    defaultOption: MiniSemibotVisualPrefs.MimicVolume switch
                    {
                        MiniSemibotMimicVolume.Low  => "Low",
                        MiniSemibotMimicVolume.High => "High",
                        _                      => "Medium",
                    });
                return volumeTr = (RectTransform)slider.transform;
            }, topPadding: 10f);

            // Range
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Voice Range - Mimic",
                    description: "",
                    onOptionChanged: opt =>
                    {
                        MiniSemibotVisualPrefs.MimicRange = opt switch
                        {
                            "Near" => MiniSemibotMimicRange.Near,
                            "Far"  => MiniSemibotMimicRange.Far,
                            _      => MiniSemibotMimicRange.Medium,
                        };
                        MiniSemibotSpawner.ApplyLiveSettings();
                    },
                    parent: scrollView,
                    stringOptions: RangeOptions,
                    defaultOption: MiniSemibotVisualPrefs.MimicRange switch
                    {
                        MiniSemibotMimicRange.Near => "Near",
                        MiniSemibotMimicRange.Far  => "Far",
                        _                     => "Medium",
                    });
                return rangeTr = (RectTransform)slider.transform;
            }, topPadding: 10f);

            // AddElementToScrollView attaches the REPOScrollViewElement after the callback returns, so collect them here and apply the initial visibility.
            foreach (var tr in new[] { chatterTr, volumeTr, rangeTr })
            {
                var el = tr != null ? tr.GetComponent<REPOScrollViewElement>() : null;
                if (el != null) mimicRows.Add(el);
            }
            UpdateMimicRows();
        }

        AddSectionLabel(popup, "Hands", 10f);

        // ── Hands / grabber visual ────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Holding",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.Grabber = opt switch
                    {
                        "Orb" => MiniSemibotGrabberVisual.Orb,
                        "Orb + Light" => MiniSemibotGrabberVisual.OrbLight,
                        _ => MiniSemibotGrabberVisual.CleanArm,
                    };
                    MiniSemibotSpawner.ApplyLiveSettings();   // toggle orb/light on live minis
                },
                parent: scrollView,
                stringOptions: HandsOptions,
                defaultOption: MiniSemibotVisualPrefs.Grabber switch
                {
                    MiniSemibotGrabberVisual.Orb => "Orb",
                    MiniSemibotGrabberVisual.OrbLight => "Orb + Light",
                    _ => "Clean Arm",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Beam colour (only with CustomGrabColor installed) ─────────────────
        if (MiniSemibotModCompat.HasCustomGrabColor)
        {
            popup.AddElementToScrollView(scrollView =>
            {
                var slider = MenuAPI.CreateREPOSlider(
                    text: "Beam Color",
                    description: "",
                    onOptionChanged: opt =>
                    {
                        MiniSemibotVisualPrefs.BeamColor = opt == "Mini-Semibot Grabber"
                            ? MiniSemibotBeamColor.MiniGrabber : MiniSemibotBeamColor.SameAsPlayer;
                        MiniSemibotSpawner.ApplyLiveSettings();   // owner-authoritative → broadcast
                    },
                    parent: scrollView,
                    stringOptions: BeamOptions,
                    defaultOption: MiniSemibotVisualPrefs.BeamColor == MiniSemibotBeamColor.MiniGrabber
                        ? "Mini-Semibot Grabber" : "Same As You");
                return (RectTransform)slider.transform;
            }, topPadding: 10f);
        }

        AddSectionLabel(popup, "Behaviour", 10f);

        // ── Leg speed ─────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Leg Speed",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.LegSpeed = LegSpeedFromOption(opt);
                    Apply();
                },
                parent: scrollView,
                stringOptions: LegSpeedOptions,
                defaultOption: LegSpeedToOption(MiniSemibotVisualPrefs.LegSpeed));
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Death behaviour ───────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "When You Die",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.DeathBehavior = opt switch
                    {
                        "Death Head" => MiniSemibotDeathBehavior.DeathHead,
                        "Hide" => MiniSemibotDeathBehavior.Hide,
                        _ => MiniSemibotDeathBehavior.CrouchWait,
                    };
                    Apply();
                },
                parent: scrollView,
                stringOptions: DeathOptions,
                defaultOption: MiniSemibotVisualPrefs.DeathBehavior switch
                {
                    MiniSemibotDeathBehavior.DeathHead => "Death Head",
                    MiniSemibotDeathBehavior.Hide => "Hide",
                    _ => "Crouch & Wait",
                });
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        // ── Hide on the King of the Losers kart (Arena Race) ──────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Hide on Kart",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.HideInArena = opt == "On";
                    // Read live every frame by MiniSemibotFollow — no re-apply needed.
                },
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.HideInArena ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Expression Preview", 10f);

        // ── Show in the facial-expression preview (5-9 keys HUD panel) ─────────
        popup.AddElementToScrollView(scrollView =>
        {
            var slider = MenuAPI.CreateREPOSlider(
                text: "Show Mini",
                description: "",
                onOptionChanged: opt =>
                {
                    MiniSemibotVisualPrefs.ShowInExpressionPreview = opt == "On";
                    // Re-dress the expression avatar so its mini spawns/despawns live (no-op outside a level).
                    MiniSemibotSpawner.RefreshExpressionPreview();
                },
                parent: scrollView,
                stringOptions: OnOffOptions,
                defaultOption: MiniSemibotVisualPrefs.ShowInExpressionPreview ? "On" : "Off");
            return (RectTransform)slider.transform;
        }, topPadding: 10f);

        AddSectionLabel(popup, "Actions", 10f);

        // ── Recapture Icon — retakes the preset-style photo with your current body colours ────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Recapture Icon", () =>
            {
                // Run on the cosmetics page (outlives the popup) so the capture finishes even if the user closes this popup right away.
                MonoBehaviour host = popup;
                if (CosmeticsMenuState.ActivePage != null) host = CosmeticsMenuState.ActivePage;
                MiniSemibotIconCapture.ForceRecapture(host);
                popup.ClosePage(false);
            }, row, new Vector2(-137f, 0f));
            return row;
        }, topPadding: 10f);

        // ── Back ──────────────────────────────────────────────────────────────
        popup.AddElementToScrollView(scrollView =>
        {
            var row = PopupUI.MakeRow(scrollView);
            MenuAPI.CreateREPOButton("Back", () => popup.ClosePage(false), row, new Vector2(-137f, 0f));
            return row;
        }, topPadding: 10f);

        popup.OpenPage(openOnTop: false);
    }

    // Pushes the changed pref onto any live mini (outfit re-roll + re-dress); placement / death / leg-speed are read every frame so they need no explicit refresh. The MiniSemibotVisualPrefs setter already persisted the value (AtomicJson) — no BepInEx Config.Save() needed (this popup writes no ConfigEntry).
    private static void Apply()
    {
        MiniSemibotSpawner.ApplyLiveSettings();
    }

    private static float LegSpeedFromOption(string opt)
        => float.TryParse(opt.TrimEnd('x'), System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 1.4f;

    // Snaps the saved float to the nearest discrete option label.
    private static string LegSpeedToOption(float v)
    {
        string best = LegSpeedOptions[0];
        float bestDiff = float.MaxValue;
        foreach (var opt in LegSpeedOptions)
        {
            float f = LegSpeedFromOption(opt);
            float d = Mathf.Abs(f - v);
            if (d < bestDiff) { bestDiff = d; best = opt; }
        }
        return best;
    }

    private static void AddSectionLabel(REPOPopupPage popup, string text, float topPadding)
    {
        popup.AddElementToScrollView(scrollView =>
        {
            var label = MenuAPI.CreateREPOLabel(text, scrollView);
            label.labelTMP.fontSize = 18f;
            label.labelTMP.alpha = 0.85f;
            label.labelTMP.alignment = TMPro.TextAlignmentOptions.Left;
            label.rectTransform.sizeDelta = new Vector2(200f, 24f);
            return (RectTransform)label.transform;
        }, topPadding: topPadding);
    }
}
