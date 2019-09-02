using System.Net;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using System.Threading;

namespace BeatBrain.Mod
{
    public class TelemetryCollector : MonoBehaviour
    {
        private Stopwatch _timer;
        private MemoryStream _ms;
        private BinaryWriter _writer;

        void Awake()
        {
            StartCoroutine(GrabRequired());
        }

        IEnumerator GrabRequired()
        {
            yield return new WaitUntil(() => BS_Utils.Plugin.LevelData.IsSet);

            Init();
        }

        void Init()
        {
            _ms = new MemoryStream(1024 * 1024 * 2); // 2mb should be plenty in most cases, but this can auto expand
            _writer = new BinaryWriter(_ms);
            
            var setup = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData;
            var dbm = setup.difficultyBeatmap;
            var lvl = dbm.level;
            var m = setup.gameplayModifiers;
            var mods = new object[]{
                Steamworks.SteamFriends.GetPersonaName(),
                Steamworks.SteamUser.GetSteamID().m_SteamID.ToString(),
                XRDevice.model,
                lvl.levelID,
                lvl.songName,
                lvl.songSubName,
                lvl.beatsPerMinute,
                dbm.difficulty.ShortName(),
                m.batteryEnergy,
                m.demoNoFail, m.demoNoObstacles, m.disappearingArrows, m.failOnSaberClash, m.fastNotes,
                m.ghostNotes, m.instaFail, m.noArrows, m.noBombs, m.noFail, m.noObstacles, m.songSpeedMul,
                m.strictAngles, m.songSpeed, m.enabledObstacleType, m.energyType };
            _writer.Write(0xb00b001);
            _writer.Write(mods.Length);
            foreach (var mod in mods)
            {
                _writer.Write(mod.ToString());
            }
            _timer = Stopwatch.StartNew();
        }

        void OnDestroy()
        {
            _writer.Flush();
            _writer = null;

            var uploadThread = new Thread(new ThreadStart(() =>
            {
                var c = new WebClient();
                c.Headers[HttpRequestHeader.ContentType] = "applicatoin/x-beat-brain";
                c.UploadData("https://beatbrain-api.brainbazooka.com/v1/telemetry", _ms.ToArray());

                /*
                var request = (HttpWebRequest)WebRequest.Create("https://beatbrain-api.brainbazooka.com/v1/telemetry");
                request.Method = "POST";
                request.ContentType = "application/x-beat-brain";
                request.AllowWriteStreamBuffering = false;
                request.SendChunked = true;
                var stream = _request.GetRequestStream();

                var response = (HttpWebResponse)_request.GetResponse();
                var responseStr = new StreamReader(response.GetResponseStream()).ReadToEnd();*/
            }))
            {
                Name = "BeatBrain data uploader",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            uploadThread.Start();
        }

        void Update()
        {
            if (_writer == null) return;
            _writer.Write(_timer.ElapsedTicks);
            Write(InputTracking.GetLocalPosition(XRNode.Head));
            Write(InputTracking.GetLocalPosition(XRNode.LeftHand));
            Write(InputTracking.GetLocalPosition(XRNode.RightHand));
            Write(InputTracking.GetLocalRotation(XRNode.Head));
            Write(InputTracking.GetLocalRotation(XRNode.LeftHand));
            Write(InputTracking.GetLocalRotation(XRNode.RightHand));
        }

        private void Write(Vector3 vector3)
        {
            _writer.Write(vector3.x);
            _writer.Write(vector3.y);
            _writer.Write(vector3.z);
        }

        private void Write(Quaternion quaternion)
        {
            _writer.Write(quaternion.w);
            _writer.Write(quaternion.x);
            _writer.Write(quaternion.y);
            _writer.Write(quaternion.z);
        }
    }
}