using BS_Utils.Utilities;
using IPA;
using IPA.Config;
using IPA.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using IPALogger = IPA.Logging.Logger;

namespace BeatBrain.Mod
{
    public class Plugin : IBeatSaberPlugin
    {
        internal static Ref<PluginConfig> config;
        internal static IConfigProvider configProvider;

        public void Init(IPALogger logger, [IPA.Config.Config.Prefer("json")] IConfigProvider cfgProvider)
        {
            Logger.log = logger;
            configProvider = cfgProvider;

            config = cfgProvider.MakeLink<PluginConfig>((p, v) =>
            {
                if (v.Value == null || v.Value.RegenerateConfig)
                    p.Store(v.Value = new PluginConfig() { RegenerateConfig = false });
                config = v;
            });
        }

        public void OnApplicationStart()
        {
            BSEvents.OnLoad();
            BSEvents.menuSceneLoadedFresh += BSEvents_menuSceneLoadedFresh;
        }

        public void OnApplicationQuit()
        {
        }

        public void OnFixedUpdate()
        {
        }

        public void OnUpdate()
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

        public void OnSceneLoaded(Scene scene, LoadSceneMode sceneMode)
        {
        }

        public void OnSceneUnloaded(Scene scene)
        {
        }
    }   
}
