// Mirrors the wearer's state-driven visuals onto the mini under the single "State Effects" toggle: body flash (hurt/heal/upgrade _ColorOverlay on every PlayerMaterial), eye/pupil overlay (EyeMaterialOverride colours: Love/Red/Green/Inverted/CeilingEye), pupil dilation (PlayerEyes.pupilSizeMultiplier, via reflection). The mini has no override driver, so we copy live values each frame.

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MoreHeadBridge;

internal sealed class MiniStateEffects : MonoBehaviour
{
    private static readonly int OverlayColorId  = Shader.PropertyToID("_ColorOverlay");
    private static readonly int OverlayAmountId = Shader.PropertyToID("_ColorOverlayAmount");

    // PlayerEyes.pupilSizeMultiplier is internal → reflect once.
    private static readonly FieldInfo? PupilMultField =
        AccessTools.Field(typeof(PlayerEyes), "pupilSizeMultiplier");

    internal PlayerAvatar? WearerAvatar;
    internal MiniSemibotFollow? Follow;
    internal PlayerCosmetics? MiniCosmetics;

    private float _appliedAmount = -1f;
    private Color _appliedColor;

    // Cached eye/pupil materials (wearer = read, mini = write); resolved lazily, body parts (not cosmetics) so they survive re-dress.
    private Material? _wearerEyeMat, _wearerPupilMat, _miniEyeMat, _miniPupilMat;
    private bool _eyeMatsResolved;
    private float _appliedEyeAmount = -1f, _appliedPupilAmount = -1f;
    private Color _appliedEyeColor, _appliedPupilColor;
    private float _appliedPupilMult = 1f;

    private void LateUpdate()
    {
        if (MiniCosmetics == null) return;

        // Dead/downed wearer: vanilla freezes a faint red overlay while deadSet, which would paint the crouch-wait/death-head mini red — clear everything and skip while dead / off / body hidden.
        bool wearerDead = WearerAvatar != null && (WearerAvatar.deadSet || WearerAvatar.isDisabled);

        // The expression-preview mini is a static portrait — state reactions don't belong (they'd flash it red). Clear and skip while pinned in the preview.
        bool inPreview = Follow != null && Follow.ExpressionPreview;

        if (!MiniSemibotVisualPrefs.StateEffects || wearerDead || inPreview
            || (Follow != null && Follow.BodyHidden))
        {
            if (_appliedAmount > 0f) ApplyBody(0f, _appliedColor);
            ClearEyes();
            return;
        }

        MirrorBody();
        MirrorEyesAndPupil();
    }

    // ── Body flash ──────────────────────────────────────────────────────────────
    private void MirrorBody()
    {
        var srcPc = WearerAvatar != null ? WearerAvatar.playerCosmetics : null;
        Material? src = null;
        if (srcPc != null)
            foreach (var pm in srcPc.playerMaterials)
                if (pm != null && pm.material != null) { src = pm.material; break; }
        if (src == null || !src.HasProperty(OverlayAmountId)) return;

        float amount = src.GetFloat(OverlayAmountId);
        Color color = src.GetColor(OverlayColorId);
        if (Mathf.Approximately(amount, _appliedAmount) && color == _appliedColor) return;
        ApplyBody(amount, color);
    }

    private void ApplyBody(float amount, Color color)
    {
        _appliedAmount = amount;
        _appliedColor = color;
        foreach (var pm in MiniCosmetics!.playerMaterials)
        {
            if (pm == null || pm.material == null || !pm.material.HasProperty(OverlayAmountId)) continue;
            pm.material.SetColor(OverlayColorId, color);
            pm.material.SetFloat(OverlayAmountId, amount);
        }
    }

    // ── Eye/pupil colour overlay + pupil dilation ─────────────────────────────────
    private void MirrorEyesAndPupil()
    {
        ResolveEyeMats();

        // Eye + pupil colour overlay (Love / Red / Green / Inverted / CeilingEye, and the hurt flash).
        if (_wearerEyeMat != null && _miniEyeMat != null)
        {
            float a = _wearerEyeMat.GetFloat(OverlayAmountId);
            Color c = _wearerEyeMat.GetColor(OverlayColorId);
            if (!Mathf.Approximately(a, _appliedEyeAmount) || c != _appliedEyeColor)
            {
                _appliedEyeAmount = a; _appliedEyeColor = c;
                _miniEyeMat.SetFloat(OverlayAmountId, a);
                _miniEyeMat.SetColor(OverlayColorId, c);
            }
        }
        if (_wearerPupilMat != null && _miniPupilMat != null)
        {
            float a = _wearerPupilMat.GetFloat(OverlayAmountId);
            Color c = _wearerPupilMat.GetColor(OverlayColorId);
            if (!Mathf.Approximately(a, _appliedPupilAmount) || c != _appliedPupilColor)
            {
                _appliedPupilAmount = a; _appliedPupilColor = c;
                _miniPupilMat.SetFloat(OverlayAmountId, a);
                _miniPupilMat.SetColor(OverlayColorId, c);
            }
        }

        // Pupil dilation: copy the wearer's pupilSizeMultiplier onto the mini's PlayerEyes (its PupilLogic re-applies it each frame).
        if (PupilMultField != null)
        {
            var srcEyes = WearerAvatar != null && WearerAvatar.playerAvatarVisuals != null
                ? WearerAvatar.playerAvatarVisuals.playerEyes : null;
            var miniEyes = Follow != null && Follow.MiniVisuals != null ? Follow.MiniVisuals.playerEyes : null;
            if (srcEyes != null && miniEyes != null)
            {
                float mult = (float)(PupilMultField.GetValue(srcEyes) ?? 1f);
                if (!Mathf.Approximately(mult, _appliedPupilMult))
                {
                    _appliedPupilMult = mult;
                    PupilMultField.SetValue(miniEyes, mult);
                }
            }
        }
    }

    private void ClearEyes()
    {
        if (_miniEyeMat != null && _appliedEyeAmount > 0f)
        {
            _appliedEyeAmount = 0f; _miniEyeMat.SetFloat(OverlayAmountId, 0f);
        }
        if (_miniPupilMat != null && _appliedPupilAmount > 0f)
        {
            _appliedPupilAmount = 0f; _miniPupilMat.SetFloat(OverlayAmountId, 0f);
        }
        if (PupilMultField != null && !Mathf.Approximately(_appliedPupilMult, 1f))
        {
            var miniEyes = Follow != null && Follow.MiniVisuals != null ? Follow.MiniVisuals.playerEyes : null;
            if (miniEyes != null) { PupilMultField.SetValue(miniEyes, 1f); _appliedPupilMult = 1f; }
        }
    }

    private void ResolveEyeMats()
    {
        if (_eyeMatsResolved
            && _wearerEyeMat != null && _wearerPupilMat != null
            && _miniEyeMat != null && _miniPupilMat != null) return;

        var wv = WearerAvatar != null ? WearerAvatar.playerAvatarVisuals : null;
        var mv = Follow != null ? Follow.MiniVisuals : null;
        if (wv == null || mv == null) return;

        _wearerEyeMat   = FindMat(wv, "Player Avatar - Eye");
        _wearerPupilMat = FindMat(wv, "Player Avatar - Pupil");
        _miniEyeMat     = FindMat(mv, "Player Avatar - Eye");
        _miniPupilMat   = FindMat(mv, "Player Avatar - Pupil");
        _eyeMatsResolved = _wearerEyeMat != null && _wearerPupilMat != null
                           && _miniEyeMat != null && _miniPupilMat != null;
    }

    // Live (instanced) material on the first renderer whose base name matches. .material returns the wearer's existing instance and a fresh per-mini instance (never mutating a shared asset).
    private static Material? FindMat(PlayerAvatarVisuals v, string baseName)
    {
        foreach (var r in v.GetComponentsInChildren<Renderer>(true))
        {
            var sm = r.sharedMaterial;
            if (sm == null) continue;
            string n = sm.name.Replace(" (Instance)", "");
            if (n == baseName) return r.material;
        }
        return null;
    }
}
