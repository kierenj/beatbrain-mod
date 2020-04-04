using BS_Utils.Utilities;
using IPA;
using IPA.Config;
using IPA.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using IPALogger = IPA.Logging.Logger;

namespace BeatBrain.Mod
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Ref<PluginConfig> config;
        internal static IConfigProvider configProvider;

        [Init]
        public Plugin(IPALogger logger, [IPA.Config.Config.Prefer("json")] IConfigProvider cfgProvider)
        {
            Logger.log = logger;
            configProvider = cfgProvider;
        }

        [OnStart]
        public void OnStart()
        {
            BSEvents.OnLoad();
            BSEvents.menuSceneLoadedFresh += BSEvents_menuSceneLoadedFresh;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        [OnExit]
        public void OnApplicationQuit()
        {
        }

        private void BSEvents_menuSceneLoadedFresh()
        {
            var _ = new GameObject("BeatBrain Disclaimer").AddComponent<Disclaimer>();
        }

        public void OnActiveSceneChanged(Scene prevScene, Scene nextScene)
        {
            if (nextScene.name == "GameCore")
            {
                new GameObject("FlashCast listener").AddComponent<FlashCaster>();
                new GameObject("BeatBrain collector").AddComponent<TelemetryCollector>();
            }
        }
    }   
}
