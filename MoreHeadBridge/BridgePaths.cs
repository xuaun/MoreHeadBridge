using System.IO;
using BepInEx;

namespace MoreHeadBridge;

// All of the mod's JSON state lives in BepInEx/config/MoreHeadBridge/. 
// Legacy files (config/MoreHeadBridge_*.json) are moved here once, on startup.
internal static class BridgePaths
{
    internal static readonly string DataDir = Path.Combine(Paths.ConfigPath, "MoreHeadBridge");

    internal static string Of(string fileName) => Path.Combine(DataDir, fileName);

    private static bool _migrated;

    internal static void Init()
    {
        if (_migrated) return;
        _migrated = true;
        try
        {
            Directory.CreateDirectory(DataDir);
            foreach (string legacy in Directory.GetFiles(Paths.ConfigPath, "MoreHeadBridge_*.json"))
            {
                string dest = Path.Combine(DataDir, Path.GetFileName(legacy).Substring("MoreHeadBridge_".Length));
                if (!File.Exists(dest)) File.Move(legacy, dest);
            }
        }
        catch (System.Exception ex)
        {
            BceConsole.LogWarning($"BridgePaths: could not move legacy save files into MoreHeadBridge/ — {ex.Message}");
        }
    }
}
