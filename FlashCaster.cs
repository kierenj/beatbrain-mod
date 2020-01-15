using System.Collections;
using System.Linq;
using UnityEngine;

namespace BeatBrain.Mod
{
    /// <summary>
    /// Looks for devices on the LAN which want to listen for note-hit events,
    /// sends packets to them when those events happen: game part.
    /// </summary>
    public class FlashCaster : MonoBehaviour
    {
        private ScoreController sc;
        private FlashCast FlashCast;

        void Awake()
        {
            StartCoroutine(GrabRequired());
            FlashCast = new FlashCast();
            FlashCast.Initialise();
        }

        IEnumerator GrabRequired()
        {
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<ScoreController>().Any());
            sc = Resources.FindObjectsOfTypeAll<ScoreController>().FirstOrDefault();
            Init();
        }

        void Update()
        {
        }

        void Init()
        {
            sc.noteWasCutEvent += Sc_noteWasCutEvent;
        }

        void OnDestroy()
        {
            sc.noteWasCutEvent -= Sc_noteWasCutEvent;
            FlashCast.Dispose();
        }

        private void Sc_noteWasCutEvent(NoteData arg1, NoteCutInfo arg2, int arg3)
        {
            if (arg1.noteType == NoteType.NoteA)
                FlashCast.HitLeft();
            else if (arg1.noteType == NoteType.NoteB)
                FlashCast.HitRight();
        }
    }
}