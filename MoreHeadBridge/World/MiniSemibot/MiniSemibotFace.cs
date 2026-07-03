using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MoreHeadBridge;

// Drives the mini's FACE from the wearer: jaw movement while the owner talks, and routing the mini's PlayerExpression onto the owner's synced expression state.
// Mouth: vanilla PlayerAvatarTalkAnimation bails on worldAvatars, so replicate the jaw drive per the owner-synced MouthMode — Never / Random chatter / WhenITalk (the wearer's live voice loudness) / MimicClips (Mimic-recorded .wavs, see MiniSemibotMimicAudio). Local AND remote minis.
// Expressions/blink: PlayerExpression mirrors the wearer in non-local mode (its else branch reads playerAvatar.playerExpressions, filled on every client). Pointed at the wearer in Spawn; isLocal forced false after Start so even our own mini follows the synced state (no input, no duplicate RPCs, no UI popup).
internal sealed class MiniSemibotFace : MonoBehaviour
{
    internal PlayerAvatar? WearerAvatar;
    internal MiniSemibotFollow? Follow;      // mini is hidden (e.g. on the kart) → go silent too
    internal Transform? MouthObject;        // the mini talk-anim's objectToRotate (jaw)
    internal float MouthMaxAngle = 45f;
    internal PlayerExpression? Expression;  // the mini's cloned expression component
    internal bool ExpressionPreview;        // preview mini → resolve the local player as the source lazily
    internal int  ForcedExpression = -1;    // >= 0 → hold this expression index (death crouch-wait); -1 = mirror

    private bool _exprFixed;
    private static System.Reflection.FieldInfo? _isLocalField;
    private static System.Reflection.FieldInfo? _stopExpressingField;
    private static System.Reflection.FieldInfo? _timerField;
    private static System.Reflection.FieldInfo? _isExpressingField;

    private int _registeredActor = -1;       // actor this face is registered under (for audio routing)
    private AudioSource? _audio;             // mimic-clip playback source (lazy)
    private float _mimicTimer;               // owner-side: countdown to the next clip
    private bool _wasInLevel;                // owner-side: tracks level entry to seed the startup warm-up

    // Seconds to wait after entering a level before the FIRST mimic clip, so the mini doesn't chatter during the load / countdown before the phase begins.
    private const float MimicLevelWarmupMin = 10f;
    private const float MimicLevelWarmupMax = 20f;
    private float[]? _clipSamples;           // raw decoded samples of the playing clip (for the jaw envelope)
    private int _clipChannels = 1;
    private float _clipInvPeak = 1f;         // 1 / peak amplitude → per-clip auto-normalisation

    private void LateUpdate()
    {
        if (ExpressionPreview)
        {
            // Preview mini: re-assert the synced read-path EVERY frame — the wearer resolves lazily (may not exist at spawn), and the clone's own Start()/Update() can re-set isLocal=true after a one-time fix, freezing the face.
            if (WearerAvatar == null) WearerAvatar = PlayerAvatar.instance;
            if (Expression != null && WearerAvatar != null)
            {
                Expression.playerAvatar = WearerAvatar;
                Expression.onlyVisualRepresentation = true;
                SetIsLocalFalse(Expression);
            }
        }
        // One-time (after PlayerExpression.Start ran): force the synced read-path for world minis.
        else if (!_exprFixed && Expression != null && WearerAvatar != null)
        {
            _exprFixed = true;
            Expression.playerAvatar = WearerAvatar;
            Expression.onlyVisualRepresentation = true;
            SetIsLocalFalse(Expression);
        }

        EnsureRegistered();

        // Mirror the wearer's per-expression "stopExpressing" flags onto the clone: the network stop only sets them on the WEARER's PlayerExpression, so without this the mini stays frozen on your last expression after you clear it.
        MirrorStopExpressing();

        // Death crouch-wait: hold a fixed expression (set by MiniSemibotFollow) instead of mirroring the wearer. Re-stamped every frame so the vanilla decay can't fade it before revive.
        if (ForcedExpression >= 0) ForceExpression(ForcedExpression);

        // Per-mode jaw drive; mode is owner-authoritative, so it matches on everyone's screen. Null wearer (ESC/Cosmetics menu avatars) resolves to the LOCAL config so the menu mini's mouth animates too.
        if (MouthObject != null)
        {
            var cfg = MiniSemibotSync.Resolve(WearerAvatar);

            // The mini goes SILENT (no voice, no mimic clips) while the wearer is dead/downed OR the mini is hidden (e.g. tucked away on the kart) — a hidden mini shouldn't talk from thin air.
            bool wearerDead = WearerAvatar != null && (WearerAvatar.isDisabled || WearerAvatar.deadSet);
            bool muted = wearerDead || (Follow != null && Follow.BodyHidden);
            if (muted && _audio != null && _audio.isPlaying) _audio.Stop();

            // MimicClips: only the OWNER picks + broadcasts (reads its own AudioFiles). Null wearer = our own menu mini → treated as local; TickOwnerMimic no-ops outside a level.
            if (!muted && cfg.Mouth == MiniSemibotMouthMode.MimicClips && (WearerAvatar == null || WearerAvatar.isLocal))
                TickOwnerMimic(cfg);

            float loud = muted ? 0f : cfg.Mouth switch
            {
                MiniSemibotMouthMode.Never      => 0f,
                MiniSemibotMouthMode.Random     => RandomLoudness(),
                MiniSemibotMouthMode.MimicClips => MimicLoudness(),
                _                          => WearerVoiceLoudness(),   // WhenITalk
            };
            float x = Mathf.Lerp(0f, -MouthMaxAngle, Mathf.Clamp01(loud));
            MouthObject.localRotation = Quaternion.Slerp(
                MouthObject.localRotation, Quaternion.Euler(x, 0f, 0f), 100f * Time.deltaTime);
        }
    }

    // Real voice loudness (the vanilla talk-anim source). 0 when not talking / suppressed.
    private float WearerVoiceLoudness()
    {
        // Menu avatars have no in-world playerAvatar → fall back to the local player so the menu mini's mouth still tracks the local voice (e.g. talking in the ESC menu mid-game).
        var avatar = WearerAvatar != null ? WearerAvatar : PlayerAvatar.instance;
        var vc = avatar?.voiceChat;
        if (vc == null || vc.overrideNoTalkAnimationTimer > 0f || vc.clipLoudness <= 0.005f) return 0f;
        return vc.clipLoudness * 4f;
    }

    // ── Mimic clips ──────────────────────────────────────────────────────────────
    private void EnsureRegistered()
    {
        int actor = MiniSemibotSync.ActorOf(WearerAvatar);
        if (actor == _registeredActor) return;
        if (_registeredActor >= 0) MiniSemibotMimicAudio.UnregisterFace(_registeredActor, this);
        _registeredActor = actor;
        if (actor >= 0) MiniSemibotMimicAudio.RegisterFace(actor, this);
    }

    // Owner-only: every few seconds, while not already playing, grab a recorded clip, play it locally and (in multiplayer) broadcast its bytes so every copy of this mini speaks the same clip.
    private void TickOwnerMimic(in MiniSemibotConfig cfg)
    {
        // Only play clips inside an actual level — not in the lobby, shop, main menu, or between phases.
        if (!SemiFunc.RunIsLevel()) { _wasInLevel = false; return; }

        // First frame after entering a new level: delay the first clip a few seconds so the mini stays quiet through the load / countdown instead of blurting a line the instant the level loads.
        if (!_wasInLevel)
        {
            _wasInLevel = true;
            _mimicTimer = Random.Range(MimicLevelWarmupMin, MimicLevelWarmupMax);
        }

        if (_audio != null && _audio.isPlaying) return;
        _mimicTimer -= Time.deltaTime;
        if (_mimicTimer > 0f) return;

        var bytes = MiniSemibotMimicAudio.PickRandomClipBytes();
        if (bytes == null) { _mimicTimer = 2f; return; }   // nothing recorded yet → retry soon

        PlayMimicClip(bytes);
        MiniSemibotMimicAudio.BroadcastClip(bytes);
        _mimicTimer = Random.Range(cfg.MimicMinDelay, cfg.MimicMaxDelay);   // cadence tier
    }

    // Decode + play a clip on the mini's positional AudioSource. Called by the owner (local pick) and by MiniSemibotMimicAudio.OnChunk (remote minis).
    internal void PlayMimicClip(byte[] wav)
    {
        if (!MiniSemibotMimicAudio.TryDecodeWav(wav, out var samples, out int channels, out int freq)) return;
        EnsureAudio();
        if (_audio == null) return;
        int frames = samples.Length / Mathf.Max(1, channels);
        if (frames <= 0) return;

        // Apply the owner's synced volume / range (resolved on every client from the synced tiers).
        var cfg = MiniSemibotSync.Resolve(WearerAvatar);
        _audio.volume = cfg.MimicVol;
        _audio.maxDistance = cfg.MimicMaxDistance;

        // Keep the raw samples + per-clip peak so the jaw rides the (auto-normalised) speech envelope — quiet clips still open the mouth fully, loud ones unchanged.
        _clipSamples = samples;
        _clipChannels = Mathf.Max(1, channels);
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = samples[i] < 0f ? -samples[i] : samples[i];
            if (a > peak) peak = a;
        }
        _clipInvPeak = 1f / Mathf.Max(peak, 0.08f);   // floor caps gain (~12×) so near-silent clips don't blow up

        var clip = AudioClip.Create("MHB_MimicClip", frames, channels, freq, false);
        clip.SetData(samples, 0);
        _audio.clip = clip;
        _audio.Play();
    }

    private void EnsureAudio()
    {
        if (_audio != null) return;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.spatialBlend = 1f;     // 3D — comes from the mini's position
        _audio.dopplerLevel = 0f;
        _audio.minDistance = 1f;
        _audio.maxDistance = 20f;
        _audio.volume = 1f;
    }

    // Jaw from the speech envelope at the playback position, AUTO-NORMALISED per clip (quiet recordings open the mouth fully). Read from the raw samples, so it's independent of playback volume / 3D distance.
    private float MimicLoudness()
    {
        if (_audio == null || !_audio.isPlaying || _clipSamples == null) return 0f;
        int ch = _clipChannels;
        int start = _audio.timeSamples * ch;          // timeSamples = current frame (per channel)
        int win = 256 * ch;                            // ~a few ms window
        float sum = 0f;
        int n = 0;
        for (int i = start; i < start + win && i < _clipSamples.Length; i++) { sum += _clipSamples[i] * _clipSamples[i]; n++; }
        if (n == 0) return 0f;
        float rms = Mathf.Sqrt(sum / n);
        return Mathf.Clamp01(rms * _clipInvPeak * 1.6f);
    }

    private void OnDestroy()
    {
        if (_registeredActor >= 0) MiniSemibotMimicAudio.UnregisterFace(_registeredActor, this);
    }

    // ── Procedural "Random" chatter ─────────────────────────────────────────────
    private float _randTimer;     // countdown to the next talk/silence flip
    private bool _randTalking;    // currently in a talk burst?
    private float _randSeed;      // per-burst noise offset, so flaps differ each time

    private float RandomLoudness()
    {
        _randTimer -= Time.deltaTime;
        if (_randTimer <= 0f)
        {
            _randTalking = !_randTalking;
            _randTimer = _randTalking ? Random.Range(0.4f, 1.6f) : Random.Range(1.5f, 5f);
            _randSeed = Random.value * 100f;
        }
        if (!_randTalking) return 0f;
        float n = Mathf.PerlinNoise(Time.time * 9f, _randSeed);   // natural-looking flap
        return Mathf.Clamp01(0.15f + n * 0.85f);
    }

    // Copies the wearer's real PlayerExpression.expressions[i].stopExpressing onto the mini's clone so the mini drops an expression exactly when the owner does. The flag is internal → reflected.
    private void MirrorStopExpressing()
    {
        if (Expression == null || WearerAvatar == null) return;
        var src = WearerAvatar.playerExpression;
        if (src == null || src == Expression) return;
        var srcList = src.expressions;
        var dstList = Expression.expressions;
        if (srcList == null || dstList == null) return;
        try
        {
            _stopExpressingField ??= typeof(ExpressionSettings).GetField(
                "stopExpressing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (_stopExpressingField == null) return;
            int n = System.Math.Min(srcList.Count, dstList.Count);
            for (int i = 0; i < n; i++)
            {
                if (srcList[i] == null || dstList[i] == null) continue;
                _stopExpressingField.SetValue(dstList[i], _stopExpressingField.GetValue(srcList[i]));
            }
        }
        catch { /* reflection unavailable → mini just keeps vanilla mirroring, no harm */ }
    }

    // Forces one expression on the clone, independent of the synced state (weight + internal timer/isExpressing so PlayerExpression keeps blending it). The wearer's dict is empty while dead — nothing fights; re-stamped each frame to outlast vanilla decay.
    private void ForceExpression(int index)
    {
        var exprs = Expression != null ? Expression.expressions : null;
        if (exprs == null || index < 0 || index >= exprs.Count) return;
        var e = exprs[index];
        if (e == null) return;
        e.weight = 100f;
        try
        {
            _timerField ??= typeof(ExpressionSettings).GetField(
                "timer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _isExpressingField ??= typeof(ExpressionSettings).GetField(
                "isExpressing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _timerField?.SetValue(e, 0.25f);
            _isExpressingField?.SetValue(e, true);
        }
        catch { /* reflection unavailable → the weight alone still nudges the blend */ }
    }

    private static void SetIsLocalFalse(PlayerExpression pe)
    {
        try
        {
            _isLocalField ??= typeof(PlayerExpression).GetField(
                "isLocal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _isLocalField?.SetValue(pe, false);
        }
        catch { /* reflection unavailable → mini just idles/blinks, no harm */ }
    }
}
