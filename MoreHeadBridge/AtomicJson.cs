using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MoreHeadBridge;

// Shared atomic file write for the mod's JSON stores (overrides, per-cosmetic colours,
// favorites). Stages to a temp file then swaps it in, so a crash mid-write can never leave
// a truncated/corrupt JSON. Each store still owns its own serialised write queue; this is
// just the single source of truth for the swap itself.
internal static class AtomicJson
{
    internal static void Write(string path, string json)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json, Encoding.UTF8);
        try
        {
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
        catch
        {
            // Swap failed (file locked, etc.) — drop the staged temp so it doesn't linger; rethrow for QueueWrite to log.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    internal static Task QueueWrite(Task lastWrite, string path, string json, string errLabel)
        => lastWrite.ContinueWith(_ =>
        {
            try { Write(path, json); }
            catch (Exception ex) { BceConsole.LogWarning($"{errLabel}: {ex.Message}"); }
        }, TaskScheduler.Default);
}
