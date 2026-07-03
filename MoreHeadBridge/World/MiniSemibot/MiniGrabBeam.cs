using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Draws a beam from the mini's hand to the SAME object the wearer grabs. NOT the mini's own PhysGrabBeam (wired to the wearer's physGrabber → mis-renders/NREs) — our own LineRenderer with the wearer beam's material, between the mini's grab point and the wearer beam's live endpoint. Only while actually grabbing AND the grabber visual isn't "Clean Arm"; no beam for the map.
internal sealed class MiniGrabBeam : MonoBehaviour
{
    internal PlayerAvatar? WearerAvatar;
    internal PlayerAvatarRightArm? MiniArm;
    internal MiniSemibotFollow? Follow;   // mini is hidden (e.g. on the kart) → hide the grab beam too

    private PhysGrabBeam? _wearerBeam;
    private LineRenderer? _line;
    private bool _built;
    private const int Res = 20;
    private readonly Vector3[] _pts = new Vector3[Res];

    private Material? _miniBeamMat;       // our private instance of the wearer beam material (so we can tint it)
    private Material? _srcMat;            // which wearer material _miniBeamMat was cloned from (detect swaps)
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    // Overcharge glow at the beam origin (light + sparks + hum), mirroring vanilla PlayerAvatarOverchargeVisuals. The vanilla component can't be reused — its Start self-wires via GetComponentInParent<PhysGrabBeam>, which the mini lacks — so clone its rig from the wearer and drive it manually.
    private GameObject? _overchargeGO;
    private Light? _overchargeLight;
    private ParticleSystem? _overchargeParticles;
    private AnimationCurve? _overchargeCurve;
    private Sound? _overchargeSound;
    private bool _overchargeTried;        // one clone attempt per mini — failure just leaves the glow off

    private void LateUpdate()
    {
        // PhysGrabBeam lives on the physGrabber rig, NOT under the avatar — use the physGrabber's direct reference, not GetComponentInChildren.
        if (_wearerBeam == null && WearerAvatar != null && WearerAvatar.physGrabber != null)
            _wearerBeam = WearerAvatar.physGrabber.physGrabBeamComponent;

        var cfg = MiniSemibotSync.Resolve(WearerAvatar);
        var origin = MiniArm != null ? MiniArm.grabberTransform : null;
        bool grabbing = cfg.Grabber != MiniSemibotGrabberVisual.CleanArm
                        // Menu/preview/expression minis NEVER beam: their wearer's playerAvatar can point at a REAL player, so a grab would draw a world-space beam from the parked preview spot.
                        && (Follow == null || !MiniSemibotSpawner.IsMenuOrPreviewWearer(Follow.WearerVisuals))
                        && (Follow == null || !Follow.BodyHidden) // mini hidden on the kart → no beam
                        && cfg.Position == MiniSemibotPosition.Front  // behind you'd barely see it → skip
                        && _wearerBeam != null && _wearerBeam.lineRenderer != null
                        && _wearerBeam.lineRenderer.enabled         // wearer's beam live == grabbing
                        && _wearerBeam.PhysGrabPoint != null && origin != null;

        if (!grabbing)
        {
            if (_line != null && _line.enabled) _line.enabled = false;
            OverchargeUpdate(0f, Vector3.zero, 1f);
            return;
        }

        if (!_built) Build();
        if (_line == null) return;
        if (!_line.enabled) _line.enabled = true;

        Vector3 start = origin!.position;
        Vector3 end = _wearerBeam!.PhysGrabPoint!.position;
        // Curve toward the wearer beam's live PULLER point (vanilla DrawCurve's control) — a midpoint control would draw a straight line.
        Vector3 ctrl = _wearerBeam.PhysGrabPointPuller != null
            ? _wearerBeam.PhysGrabPointPuller.position
            : Vector3.Lerp(start, end, 0.5f);
        for (int i = 0; i < Res; i++)
        {
            float t = (float)i / (Res - 1);
            _pts[i] = Bezier(t, start, ctrl, end);
        }
        _line.positionCount = Res;
        _line.SetPositions(_pts);

        // Beam colour lives in the MATERIAL (the shader ignores startColor) and CGC tints the wearer's shared material — so the mini keeps its OWN instance, texture-synced, tinted independently.
        var lr = _wearerBeam.lineRenderer!;
        var wearerMat = lr.sharedMaterial;
        EnsureBeamMaterial(wearerMat);
        // Copy the wearer beam's widthCurve AND scale the multiplier — thickness comes from the curve, not just the multiplier. Result = MiniScale of your beam everywhere.
        _line.widthCurve = lr.widthCurve;
        _line.widthMultiplier = lr.widthMultiplier * cfg.Scale * 1.5f;

        if (_miniBeamMat != null && wearerMat != null)
        {
            // keep the scrolling-texture animation in lockstep with the wearer beam
            _miniBeamMat.mainTexture = wearerMat.mainTexture;
            _miniBeamMat.mainTextureOffset = wearerMat.mainTextureOffset;

            Color hue = ResolveBeamRgb(cfg, wearerMat);
            ApplyBeamHue(_miniBeamMat, hue, wearerMat);
        }

        // Overcharge: same 0..1 charge the vanilla visuals read (byte 0..200 → /2/100), zeroed while disabled.
        var grabber = WearerAvatar != null && !WearerAvatar.isDisabled ? WearerAvatar.physGrabber : null;
        float charge = grabber != null ? grabber.physGrabBeamOverCharge / 2f / 100f : 0f;
        OverchargeUpdate(charge, start, cfg.Scale);
    }

    // ── Overcharge glow ──────────────────────────────────────────────────────────

    // Flicker / spark emission / hum formulas mirror PlayerAvatarOverchargeVisuals.Update, with light, size and volume scaled to the mini.
    private void OverchargeUpdate(float charge, Vector3 origin, float scale)
    {
        if (charge <= 0f)
        {
            if (_overchargeLight != null && _overchargeLight.enabled)
            {
                _overchargeLight.enabled = false;
                if (_overchargeParticles != null) _overchargeParticles.Stop();
            }
            _overchargeSound?.PlayLoop(playing: false, 0.5f, 0.5f);
            return;
        }

        EnsureOverchargeRig();
        if (_overchargeGO == null || _overchargeLight == null) return;

        _overchargeGO.transform.position = origin;

        float eval = _overchargeCurve != null ? _overchargeCurve.Evaluate(charge) : charge;
        if (!_overchargeLight.enabled)
        {
            _overchargeLight.enabled = true;
            if (_overchargeParticles != null) _overchargeParticles.Play();
        }
        _overchargeLight.intensity = (8f * eval + charge * Mathf.Sin(Time.time * (10f + 20f * eval))) * scale;

        if (_overchargeParticles != null)
        {
            var emission = _overchargeParticles.emission;
            emission.rateOverTime = eval * 50f;
            _overchargeParticles.transform.localScale = Vector3.one * ((0.1f + 0.8f * eval) * scale);
        }

        if (_overchargeSound != null)
        {
            _overchargeSound.LoopVolumeCurrent = 0.5f * eval * Mathf.Clamp01(scale);
            _overchargeSound.PlayLoop(playing: true, 0.5f, 0.5f, 1f + 2f * eval);
        }
    }

    // Clones the wearer's overcharge rig once, stripping the vanilla script BEFORE its Start runs (it would NRE without a PhysGrabBeam parent) — its serialized curve + Sound (and the Sound's cloned AudioSource) survive the strip.
    private void EnsureOverchargeRig()
    {
        if (_overchargeTried) return;
        _overchargeTried = true;
        try
        {
            var src = FindWearerOvercharge();
            if (src == null) return;

            _overchargeGO = Object.Instantiate(src.gameObject);
            _overchargeGO.name = "MHB_MiniOvercharge";
            var script = _overchargeGO.GetComponent<PlayerAvatarOverchargeVisuals>();
            if (script != null)
            {
                _overchargeCurve = script.overchargeIntensityCurve;
                _overchargeSound = script.soundOverchargeLoop;
                Object.DestroyImmediate(script);
            }
            _overchargeLight = _overchargeGO.GetComponentInChildren<Light>(true);
            _overchargeParticles = _overchargeGO.GetComponentInChildren<ParticleSystem>(true);
            if (_overchargeLight != null) _overchargeLight.enabled = false;
            if (_overchargeParticles != null) _overchargeParticles.Stop();

            // A Sound whose AudioSource didn't clone with the rig would drive the WEARER's source — drop the hum, keep light + sparks.
            if (_overchargeSound != null && (_overchargeSound.Source == null
                || !_overchargeSound.Source.transform.IsChildOf(_overchargeGO.transform)))
                _overchargeSound = null;
        }
        catch (System.Exception ex)
        {
            BridgeLog.Debug($"Mini-Semibot overcharge rig unavailable: {ex.Message}");
            if (_overchargeGO != null) { Object.Destroy(_overchargeGO); _overchargeGO = null; }
            _overchargeLight = null;
            _overchargeParticles = null;
            _overchargeSound = null;
        }
    }

    // The wearer's live rig: pre-Start it still sits under the PhysGrabBeam; post-Start it re-parents next to the avatar, so fall back to matching by its playerAvatar field.
    private PlayerAvatarOverchargeVisuals? FindWearerOvercharge()
    {
        if (WearerAvatar == null || WearerAvatar.physGrabber == null) return null;
        var beam = WearerAvatar.physGrabber.physGrabBeamComponent;
        var v = beam != null ? beam.GetComponentInChildren<PlayerAvatarOverchargeVisuals>(true) : null;
        if (v != null) return v;
        foreach (var cand in Object.FindObjectsOfType<PlayerAvatarOverchargeVisuals>())
            if (cand != null && cand.playerAvatar == WearerAvatar) return cand;
        return null;
    }

    // Resolves the mini beam's HUE (brightness/alpha is borrowed from the wearer beam): no CGC → mirror the wearer; MiniGrabber → the mini's own grabber slot; SameAsPlayer → your custom grabber RGB, else the wearer beam's colour.
    private Color ResolveBeamRgb(in MiniSemibotConfig cfg, Material? wearerMat)
    {
        Color wearerColor = wearerMat != null ? wearerMat.color : Color.white;
        if (!MiniSemibotModCompat.HasCustomGrabColor) return wearerColor;

        if (cfg.Beam == MiniSemibotBeamColor.MiniGrabber && TryMiniGrabberColor(out var mini)) return mini;

        // The custom store only holds the LOCAL player's colours, so only honour it for the local wearer.
        if (WearerAvatar != null && WearerAvatar.isLocal && Plugin.EnableVanillaCustomColors.Value &&
            PerCosmeticColors.TryGetCustomColor(VanillaTintHelper.BaseMeshAssetId((int)SemiFunc.CosmeticType.ArmRightMesh), out var wearerCustom))
            return wearerCustom;

        return wearerColor;
    }

    // The mini's OWN instance of the wearer beam material (re-cloned if the wearer swaps, e.g. overcharge) — tintable without affecting, or being overridden by, the CGC-tinted shared material.
    private void EnsureBeamMaterial(Material? wearerMat)
    {
        if (_line == null || wearerMat == null) return;
        if (_miniBeamMat != null && _srcMat == wearerMat) return;
        if (_miniBeamMat != null) Object.Destroy(_miniBeamMat);
        _miniBeamMat = new Material(wearerMat);
        _srcMat = wearerMat;
        _line.sharedMaterial = _miniBeamMat;
        _line.startColor = Color.white;   // colour now comes from the material; keep the vertex colour neutral
        _line.endColor = Color.white;
    }

    // Recolours to <hue> while borrowing the wearer beam's CURRENT brightness/alpha so it reads as a live beam; SameAsPlayer-with-no-custom reproduces the wearer beam exactly.
    private static void ApplyBeamHue(Material mat, Color hue, Material wearerMat)
    {
        Color wMain = wearerMat.color;
        Color wEm = wearerMat.HasProperty(EmissionId) ? wearerMat.GetColor(EmissionId) : wMain;

        float m = Mathf.Max(hue.r, Mathf.Max(hue.g, hue.b));
        if (m > 0.0001f) hue = new Color(hue.r / m, hue.g / m, hue.b / m, 1f);   // normalise brightest → 1

        float mainLum = Mathf.Max(wMain.r, Mathf.Max(wMain.g, wMain.b));
        float emLum = Mathf.Max(wEm.r, Mathf.Max(wEm.g, wEm.b));

        mat.color = new Color(hue.r * mainLum, hue.g * mainLum, hue.b * mainLum, wMain.a);
        if (mat.HasProperty(EmissionId))
            mat.SetColor(EmissionId, new Color(hue.r * emLum, hue.g * emLum, hue.b * emLum, wEm.a));
    }

    private PlayerCosmetics? _miniCosmetics;

    // The mini's OWN grabber colour, NOT the wearer's. Used by MiniGrabber mode.
    private bool TryMiniGrabberColor(out Color color)
    {
        color = Color.white;
        int slot = (int)SemiFunc.CosmeticType.ArmRightMesh;   // grabber colour slot (== 13)

        _miniCosmetics ??= GetComponentInChildren<PlayerCosmetics>(true);
        var cos = _miniCosmetics;
        if (cos == null) return false;

        // Read the colour ON the mini's grabber mesh — it already carries palette OR custom RGB, clone OR preset (the palette index alone would miss a custom).
        if (cos.playerMaterials != null)
        {
            foreach (var pm in cos.playerMaterials)
            {
                if (pm == null || pm.cosmetic != null || (int)pm.cosmeticType != slot) continue;
                if (pm.material == null) break;   // not set up yet → fall through to palette
                var c = pm.material.GetColor(PerCosmeticColors.PropAlbedo);
                color = new Color(c.r, c.g, c.b, 1f);
                return true;
            }
        }

        // Fallback: the mini's equipped palette index → MetaManager colour.
        var meta = MetaManager.instance;
        if (cos.colorsEquipped == null || meta == null || meta.colors == null) return false;
        if (slot < 0 || slot >= cos.colorsEquipped.Length) return false;
        int idx = cos.colorsEquipped[slot];
        if (idx < 0 || idx >= meta.colors.Count) return false;
        color = meta.colors[idx].color;
        return true;
    }

    private void Build()
    {
        _built = true;
        try
        {
            // Scene root (scale 1) + world-space points, so the mini's 0.33 scale doesn't distort it.
            var go = new GameObject("MHB_MiniBeam");
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            var lr = _wearerBeam != null ? _wearerBeam.lineRenderer : null;
            if (lr != null)
            {
                // The material is our OWN per-frame instance (EnsureBeamMaterial) so we can tint it independently of the wearer's CGC-tinted shared material — don't share it here.
                _line.widthCurve = lr.widthCurve;
                _line.widthMultiplier = lr.widthMultiplier * MiniSemibotSettings.Scale * 1.5f;
                _line.textureMode = lr.textureMode;
            }
            _line.enabled = false;
        }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"Mini-Semibot beam build failed: {ex.Message}");
            _line = null;
        }
    }

    private static Vector3 Bezier(float t, Vector3 p0, Vector3 p1, Vector3 p2)
        => Mathf.Pow(1f - t, 2f) * p0 + 2f * (1f - t) * t * p1 + Mathf.Pow(t, 2f) * p2;

    private void OnDestroy()
    {
        if (_line != null) Object.Destroy(_line.gameObject);
        if (_miniBeamMat != null) Object.Destroy(_miniBeamMat);
        if (_overchargeGO != null) Object.Destroy(_overchargeGO);
    }
}
