using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HMUI;
using BeatSaberMarkupLanguage;
using System.Collections;
using System.IO;

namespace BeatBrain.Mod
{
    /// <summary>
    /// Behaviour which triggers/displays the disclaimer UI.
    /// </summary>
    public class Disclaimer : MonoBehaviour
    {
        private string _path;

        private static readonly int currentDisclaimerVersion = 3;

        public void Awake()
        {
            var folder =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "BeatBrainDisclaimerAcceptances");
            Directory.CreateDirectory(folder);

            var steamid = Steamworks.SteamUser.GetSteamID().m_SteamID.ToString();
            _path = Path.Combine(folder, $"disclaimer_{currentDisclaimerVersion}_steamid_{steamid}");

            if (File.Exists(_path))
            {
                return;
            }

            // kick off disclaimer process
            StartCoroutine(DisclaimerCo());
        }

        private void LogAcceptance()
        {
            File.Create(_path).Dispose();
        }

        public IEnumerator DisclaimerCo()
        {
            // wait for menu to initialise
            yield return new WaitForSeconds(0.5f);

            // get flow controller for menu
            var fc = Resources.FindObjectsOfTypeAll<MainFlowCoordinator>().First();

            // create our disclaimer view controller
            var vc = BeatSaberUI.CreateViewController<BeatBrainDisclaimerViewController>();
            vc.Done = () =>
             {
                 LogAcceptance();
                 typeof(FlowCoordinator).GetMethod("DismissViewController", BindingFlags.Instance | BindingFlags.NonPublic)
                         .Invoke(fc, new object[]
                         {
                            (ViewController)vc,
                            (Action)null, // finishedCallback
                            false // immediately
                         });
             };

            typeof(FlowCoordinator).GetMethod("PresentViewController", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fc, new object[]
                {
                    (ViewController)vc,
                    (Action)null,
                    true // immediately
                });
        }
    }
}
