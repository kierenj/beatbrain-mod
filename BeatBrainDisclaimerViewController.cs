using System;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSaberMarkupLanguage.Attributes;

namespace BeatBrain.Mod
{
    /// <summary>
    /// Disclaimer, displayed once per player Steam ID, on startup,
    /// once per disclaimer version.
    /// </summary>
    public class BeatBrainDisclaimerViewController : BSMLResourceViewController
    {
        public override string ResourceName => "BeatBrain.Mod.Disclaimer.bsml";

        public Action Done;


        [UIAction("agree")]
        private void Agree()
        {
            Done();
        }

        [UIAction("cancel")]
        private void Cancel()
        {
            UnityEngine.Application.Quit();
        }

        [UIValue("maintext")]
        public static string maintext = @"THANK YOU for installing the BeatBrain Mod!

When you play Beat Saber with the BeatBrain mod installed, some data is sent to the BeatBrain server in the cloud.  Click Agree if you're happy to continue, or Cancel to close Beat Saber (you can uninstall the mod to carry on). This message will only appear once, unless things are updated, so please read carefully.

This mod sends the following information to the BeatBrain server whenever you play a song:

- Your Steam ID and Steam Name
- A recording of your movements
- Information about the song
- Your score

Information is sent securely over SSL. At the moment data is just being collected and stored. In the future it may be used for these purposes:

- Scoreboards
- Replays
- AI experiments (analysing movement data)
- Statistics

If this changes, this message will appear again and you'll be asked to opt-in again.

If you have any questions, comments or suggestions for the mod please contact:
beatbrain@brainbazooka.com

Thank you, and have fun :-)

Disclaimer ver. 3 - 15th Jan 2020";
    }
}
