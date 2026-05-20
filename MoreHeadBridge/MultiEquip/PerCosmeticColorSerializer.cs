using System.Collections.Generic;
using System.Text;

namespace MoreHeadBridge;

// Serializes/deserializes { assetId → colorIndex } to/from a single string.
internal static class PerCosmeticColorSerializer
{
    private const char Sep = '\x1F';

    internal static string Serialize(IReadOnlyDictionary<string, int> colors)
    {
        if (colors.Count == 0) return "";

        var sb = new StringBuilder(colors.Count * 48);
        foreach (var kv in colors)
        {
            sb.Append(kv.Key);
            sb.Append(Sep);
            sb.Append(kv.Value);
            sb.Append(Sep);
        }
        return sb.ToString();
    }

    internal static Dictionary<string, int> Deserialize(string data)
    {
        var result = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(data)) return result;

        var parts = data.Split(Sep, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (int.TryParse(parts[i + 1], out int colorIdx))
                result[parts[i]] = colorIdx;
        }
        return result;
    }
}
