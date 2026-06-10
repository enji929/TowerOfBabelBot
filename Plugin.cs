using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace TowerOfBabelBot;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo("TowerOfBabelBot loaded");
        NativeHook.Apply();
        AddComponent<BotController>();
    }
}
