// "Mimic Clips" mouth mode: reuses the Mimic mod's recorded .wavs (no assembly/type/RPC dependency). The OWNER picks a clip and broadcasts its bytes over the BridgeNetMux in 8 KB chunks; every client reassembles, decodes the WAV and plays it at that mini, driving the jaw from the loudness.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoreHeadBridge;

internal static class MiniSemibotMimicAudio
{
    private const int ChunkSize = 8192;          // matches Mimic's chunk size
    private const int MaxChunks = 128;           // safety cap (≈1 MB) against malformed/abusive payloads
    private const int MaxClipBytes = 1024 * 1024;

    // Photon actor number → that player's mini face on THIS client (every client has one per player).
    private static readonly Dictionary<int, MiniSemibotFace> _faces = new();

    // Per-sender reassembly state, keyed by actor number.
    private static readonly Dictionary<int, Incoming> _incoming = new();

    private sealed class Incoming
    {
        public byte[]?[] Chunks = Array.Empty<byte[]?>();
        public int Received;
        public int Total;
    }

    // The folder the Mimic mod records into (same hard-coded location Mimic uses).
    internal static string AudioFolder => Path.Combine(Application.dataPath, "AudioFiles");

    // ── Registry (called by MiniSemibotFace) ─────────────────────────────────────
    internal static void RegisterFace(int actor, MiniSemibotFace face)
    {
        if (actor >= 0) _faces[actor] = face;
    }

    internal static void UnregisterFace(int actor, MiniSemibotFace face)
    {
        if (actor >= 0 && _faces.TryGetValue(actor, out var cur) && cur == face)
        {
            _faces.Remove(actor);
            _incoming.Remove(actor);
        }
    }

    // ── Owner side: pick a random recorded clip ─────────────────────────────────
    internal static byte[]? PickRandomClipBytes()
    {
        try
        {
            if (!Directory.Exists(AudioFolder)) return null;
            var files = Directory.GetFiles(AudioFolder, "*.wav");
            if (files.Length == 0) return null;
            var path = files[UnityEngine.Random.Range(0, files.Length)];
            var bytes = File.ReadAllBytes(path);
            return bytes.Length is > 44 and <= MaxClipBytes ? bytes : null;
        }
        catch (System.Exception ex) { BridgeLog.Debug($"MiniSemibotMimicAudio: could not read a recorded clip — {ex.Message}"); return null; }
    }

    // Chunk + broadcast a clip's raw WAV bytes to the OTHER clients (owner plays its own copy directly).
    internal static void BroadcastClip(byte[] wav)
    {
        if (!SemiFunc.IsMultiplayer() || wav == null || wav.Length <= 44) return;

        int total = (wav.Length + ChunkSize - 1) / ChunkSize;
        if (total > MaxChunks) return;

        for (int i = 0; i < total; i++)
        {
            int offset = i * ChunkSize;
            int len = Mathf.Min(ChunkSize, wav.Length - offset);
            var chunk = new byte[len];
            Array.Copy(wav, offset, chunk, 0, len);
            // SendReliable → reliable + ordered, so chunks arrive in sequence.
            BridgeNetMux.SendTransient(BridgeNetMux.ChMimicAudio, new object[] { total, i, chunk });
        }
    }

    // ── Receiver side (called by BridgeNetMux for "MimicAudio" transients) ──────
    internal static void OnChunk(int actor, object data)
    {
        if (data is not object[] parts || parts.Length != 3) return;

        int total, index;
        byte[] chunk;
        try
        {
            total = (int)parts[0];
            index = (int)parts[1];
            chunk = (byte[])parts[2];
        }
        catch { return; }

        if (total <= 0 || total > MaxChunks || index < 0 || index >= total || chunk == null) return;

        if (!_incoming.TryGetValue(actor, out var inc) || inc.Total != total || index == 0)
        {
            inc = new Incoming { Chunks = new byte[]?[total], Received = 0, Total = total };
            _incoming[actor] = inc;
        }

        if (inc.Chunks[index] == null)
        {
            inc.Chunks[index] = chunk;
            inc.Received++;
        }

        if (inc.Received < inc.Total) return;
        _incoming.Remove(actor);

        // Reassemble + decode + play on that actor's mini.
        var wav = Concat(inc.Chunks!);
        if (wav.Length > MaxClipBytes) return;
        if (_faces.TryGetValue(actor, out var face) && face != null)
            face.PlayMimicClip(wav);
    }

    private static byte[] Concat(byte[]?[] chunks)
    {
        int n = 0;
        foreach (var c in chunks) n += c?.Length ?? 0;
        var outBytes = new byte[n];
        int o = 0;
        foreach (var c in chunks)
        {
            if (c == null) continue;
            Array.Copy(c, 0, outBytes, o, c.Length);
            o += c.Length;
        }
        return outBytes;
    }

    // ── WAV decode (Mimic writes plain PCM-16; we scan subchunks to be safe) ─────
    internal static bool TryDecodeWav(byte[] wav, out float[] samples, out int channels, out int frequency)
    {
        samples = Array.Empty<float>();
        channels = 1;
        frequency = 16000;
        try
        {
            if (wav.Length < 44) return false;
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return false;
            if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E') return false;

            int bits = 16;
            int dataOffset = -1, dataSize = 0;
            int p = 12;
            while (p + 8 <= wav.Length)
            {
                int size = BitConverter.ToInt32(wav, p + 4);
                int body = p + 8;
                if (size < 0 || body + size > wav.Length + 1) break;

                if (IsId(wav, p, 'f', 'm', 't', ' '))
                {
                    channels = BitConverter.ToInt16(wav, body + 2);
                    frequency = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                }
                else if (IsId(wav, p, 'd', 'a', 't', 'a'))
                {
                    dataOffset = body;
                    dataSize = Mathf.Min(size, wav.Length - body);
                    break;
                }
                p = body + size + (size & 1);   // chunks are word-aligned
            }

            if (dataOffset < 0 || dataSize <= 0 || bits != 16) return false;
            if (channels < 1) channels = 1;
            if (frequency < 8000 || frequency > 48000) frequency = 16000;

            int count = dataSize / 2;
            samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = (short)(wav[dataOffset + i * 2] | (wav[dataOffset + i * 2 + 1] << 8));
                samples[i] = s / 32768f;
            }
            return count > 0;
        }
        catch { return false; }
    }

    private static bool IsId(byte[] b, int off, char a, char c, char d, char e)
        => b[off] == a && b[off + 1] == c && b[off + 2] == d && b[off + 3] == e;
}
