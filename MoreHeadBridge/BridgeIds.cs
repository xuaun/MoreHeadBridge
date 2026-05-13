using System;

namespace MoreHeadBridge;

internal static class BridgeIds
{
  internal const string Prefix = "morehead-bridge:";

  internal static bool IsBridgeAsset(string? assetId)
      => !string.IsNullOrEmpty(assetId) && assetId.StartsWith(Prefix, StringComparison.Ordinal);

  internal static bool IsBridgeAsset(CosmeticAsset? asset)
      => asset != null && IsBridgeAsset(asset.assetId);
}
