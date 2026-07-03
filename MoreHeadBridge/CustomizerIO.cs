// Export/import helper for per-cosmetic override data: Export merges the current overrides (or one
// cosmetic's) into BepInEx/config/MoreHeadBridge/overrides_export.json; Import merges that file back
// into the local store (keeping local-only overrides). Also driven by the ExportCosmeticCustomizer/ImportCosmeticCustomizer config triggers.

using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MoreHeadBridge;

internal static class CustomizerIO
{
    internal static string ExportFolder   => Path.Combine(Paths.ConfigPath, "MoreHeadBridge");
    internal static string ExportFilePath => Path.Combine(ExportFolder, "overrides_export.json");

    /// Exports all local overrides to the shared export file (merging with existing content).
    internal static void ExportAll()
    {
        var allData = CustomizerStore.GetAllData();
        if (allData.Count == 0)
        {
            BceConsole.LogInfo("CustomizerIO: no overrides to export.");
            return;
        }
        MergeWrite(allData);
    }

    /// Exports a single cosmetic's override to the shared export file (merging).
    /// No-op if the cosmetic has no saved override.
    internal static void ExportSingle(string assetId)
    {
        if (!CustomizerStore.TryGet(assetId, out var data))
        {
            BceConsole.LogWarning($"CustomizerIO: no saved override for '{assetId}' — save first.");
            return;
        }
        MergeWrite(new Dictionary<string, CosmeticOverrideData> { [assetId] = data });
    }

    /// Reads the export file and merges its contents into the local override store.
    /// Local overrides not present in the file are preserved (true merge, not replace).
    internal static void ImportMerge()
    {
        try
        {
            if (!File.Exists(ExportFilePath))
            {
                BceConsole.LogWarning("CustomizerIO: export file not found — nothing to import.");
                return;
            }

            string json = File.ReadAllText(ExportFilePath);
            var data = JsonConvert.DeserializeObject<Dictionary<string, CosmeticOverrideData>>(json);
            if (data == null || data.Count == 0)
            {
                BceConsole.LogWarning("CustomizerIO: export file is empty — nothing to import.");
                return;
            }

            CustomizerStore.ImportBatch(data);
            BceConsole.LogInfo($"CustomizerIO: imported {data.Count} override(s) from {ExportFilePath}");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"CustomizerIO: import failed — {ex.Message}");
        }
    }

    // Reads existing file, merges incoming entries over it, writes back.
    private static void MergeWrite(Dictionary<string, CosmeticOverrideData> incoming)
    {
        try
        {
            Directory.CreateDirectory(ExportFolder);

            var existing = new Dictionary<string, CosmeticOverrideData>();
            if (File.Exists(ExportFilePath))
            {
                string existingJson = File.ReadAllText(ExportFilePath);
                var existingData = JsonConvert.DeserializeObject<Dictionary<string, CosmeticOverrideData>>(existingJson);
                if (existingData != null)
                    existing = existingData;
            }

            foreach (var kvp in incoming)
                existing[kvp.Key] = kvp.Value;

            string json = JsonConvert.SerializeObject(existing, Formatting.Indented);
            AtomicJson.Write(ExportFilePath, json);
            BceConsole.LogInfo($"CustomizerIO: exported {incoming.Count} override(s) to {ExportFilePath}");
        }
        catch (Exception ex)
        {
            BceConsole.LogWarning($"CustomizerIO: export failed — {ex.Message}");
        }
    }
}
