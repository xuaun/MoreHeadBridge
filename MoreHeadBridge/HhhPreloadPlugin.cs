using BepInEx;

namespace MoreHeadBridge;

// Its GUID sorts first in BepInEx's order, so HhhPreloader is ready before MoreHead opens its files.
[BepInPlugin(MyPluginInfo.PRELOAD_GUID, MyPluginInfo.PLUGIN_NAME + " Preload", MyPluginInfo.PLUGIN_VERSION)]
public sealed class HhhPreloadPlugin : BaseUnityPlugin
{
    private void Awake() => HhhPreloader.Begin(Logger);

    // Plugin.Awake already calls it, unless it didn't get that far.
    private void Start() => HhhPreloader.Finish();
}
