using System.Net;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.XR;
using System.Threading;
using System.Linq;
using System;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeatBrain.Mod
{
    /// <summary>
    /// Collects player and song header info, and movement data.
    /// When finished, sends to the BeatBrain server via API in binary format.
    /// </summary>
    public class TelemetryCollector : MonoBehaviour
    {
        private Stopwatch _timer;
        private ScoreController _scoreController;
        private int _leftHitDelta;
        private int _rightHitDelta;
        private int _leftMissDelta;
        private int _rightMissDelta;
        private int? _rawScore;
        private int? _modifiedScore;
        private VRCenterAdjust _vrca;
        private ClientWebSocket _ws;

        private readonly object _sync = new object();
        private readonly CancellationTokenSource _wsCancelSrc = new CancellationTokenSource();
        private readonly AutoResetEvent _newData = new AutoResetEvent(false);
        private CancellationToken _wsCancel;
        private int _sendPos;
        private int? _finalPos;
        private MemoryStream _ms;
        private BinaryWriter _writer;

#if DEBUG_TEXT
        private TextMeshPro _text;
#endif

        [Flags]
        public enum TelemetryFrameContents
        {
            AbsoluteMovement = 0x1,
            AbsoluteScore = 0x2,
            LeftHitDelta = 0x4,
            RightHitDelta = 0x8,
            LeftMissDelta = 0x10,
            RightMissDelta = 0x20
        }

        void Awake()
        {
            StartCoroutine(GrabRequired());
        }

        IEnumerator GrabRequired()
        {
            yield return new WaitUntil(() => BS_Utils.Plugin.LevelData.IsSet);
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<ScoreController>().Any());

            _scoreController = Resources.FindObjectsOfTypeAll<ScoreController>().FirstOrDefault();
            _vrca = Resources.FindObjectsOfTypeAll<VRCenterAdjust>().FirstOrDefault();
            
            Init();
        }

        void Init()
        {
#if DEBUG_TEXT
            var tmp = Resources.FindObjectsOfTypeAll<TextMeshPro>().First();

            var go = new GameObject();

            _text = Instantiate(tmp);
            _text.transform.SetParent(go.transform);
            
            _text.text = "BeatBrain...";
            _text.fontSize = 2;
            _text.color = Color.white;
            _text.font = _text.font ?? Plugin.mainFont;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.enableWordWrapping = false;
            _text.richText = true;
            _text.alignment = TextAlignmentOptions.Center;
            go.transform.localPosition = new Vector3(0, 2.3f, 7f);
            _text.ForceMeshUpdate();
#endif
            _wsCancel = _wsCancelSrc.Token;

            _ms = new MemoryStream(1024 * 1024 * 4); // 4mb should be plenty in most cases, but this can auto expand
            _writer = new BinaryWriter(_ms);

            DateTime lastPosLog = DateTime.UtcNow;
            Task.Run(async () =>
            {
                try
                {
                    ArraySegment<byte> seg = default;
                    int toWrite;

                    _ws = new ClientWebSocket();
                    var uri = new Uri("wss://beatbrain-api.brainbazooka.com/ws?action=upload-telemetry");
                    await _ws.ConnectAsync(uri, _wsCancel);
                    while (_finalPos == null && !(_sendPos >= _finalPos))
                    {
                        _newData.WaitOne(5);
                        _wsCancel.ThrowIfCancellationRequested();
                        lock (_ms)
                        {
                            if ((DateTime.UtcNow - lastPosLog).TotalSeconds > 10.0)
                            {
                                lastPosLog = DateTime.UtcNow;
                            }
                            toWrite = (int)Math.Min(_ms.Position - _sendPos, 32768);
                            if (toWrite > 0)
                            {
                                seg = new ArraySegment<byte>(_ms.GetBuffer(), _sendPos, toWrite);
                            }
                        }
                        if (toWrite > 0)
                        {
                            await _ws.SendAsync(seg, WebSocketMessageType.Binary, false, _wsCancel);
                            _sendPos += toWrite;
                        }
                    }
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logger.log.Error(ex);
                }
            });

            var setup = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData;
            var dbm = setup.difficultyBeatmap;
            var lvl = dbm.level;
            var m = setup.gameplayModifiers;
            var characteristic = setup.difficultyBeatmap.parentDifficultyBeatmapSet.beatmapCharacteristic.characteristicNameLocalizationKey;

            // header information - here's what we collect to send to the server
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
                m.strictAngles, m.songSpeed, m.enabledObstacleType, m.energyType,
                characteristic };

            _writer.Write(0xb00b003); // version 3

            _writer.Write(mods.Length);
            foreach (var mod in mods)
            {
                _writer.Write(mod.ToString());
            }

            _timer = Stopwatch.StartNew();
            _scoreController.scoreDidChangeEvent += ScoreChanged;
            _scoreController.noteWasCutEvent += NoteCut;
            _scoreController.noteWasMissedEvent += NoteMissed;
            _rawScore = null;
            _modifiedScore = null;
            _leftHitDelta = 0;
            _rightHitDelta = 0;
            _leftMissDelta = 0;
            _rightMissDelta = 0;
        }

        void OnDestroy()
        {
            _scoreController.scoreDidChangeEvent -= ScoreChanged;
            _scoreController.noteWasCutEvent -= NoteCut;
            _scoreController.noteWasMissedEvent -= NoteMissed;

            _writer.Flush();
            _finalPos = (int)_writer.BaseStream.Position;

            _writer = null;

            // start a lower-priority background thread to upload the data in one go
            if (_sendPos == 0)
            {
                Logger.log.Debug("SendPos is zero: uploading telemetry the old-fashioned way");
                _wsCancelSrc.Cancel();
                var uploadThread = new Thread(new ThreadStart(() =>
                {
                    var c = new WebClient();
                    c.Headers[HttpRequestHeader.ContentType] = "application/x-beat-brain";
                    c.UploadData("https://beatbrain-api.brainbazooka.com/v1/telemetry", _ms.ToArray());
                }))
                {
                    Name = "BeatBrain data uploader",
                    IsBackground = true,
                    Priority = System.Threading.ThreadPriority.BelowNormal
                };
                uploadThread.Start();
            }
        }

#if DEBUG_TEXT
        int frames = 0;
#endif

        void Update()
        {
            if (_writer == null) return;

            lock (_ms)
            {
                // every frame, write to the buffer...
                _writer.Write(_timer.ElapsedTicks);

                // write flags to say whats included in the frame
                var content = TelemetryFrameContents.AbsoluteMovement;
                if (_rawScore != null && _modifiedScore != null) content |= TelemetryFrameContents.AbsoluteScore;
                if (_leftHitDelta > 0) content |= TelemetryFrameContents.LeftHitDelta;
                if (_leftMissDelta > 0) content |= TelemetryFrameContents.LeftMissDelta;
                if (_rightHitDelta > 0) content |= TelemetryFrameContents.RightHitDelta;
                if (_rightMissDelta > 0) content |= TelemetryFrameContents.RightMissDelta;
                _writer.Write((byte)content);

                // write frame contents: might be movement, might be note events, could be a score change
                if (content.HasFlag(TelemetryFrameContents.AbsoluteMovement))
                {
                    Write(_vrca.transform.TransformPoint(InputTracking.GetLocalPosition(XRNode.Head)));
                    Write(_vrca.transform.TransformPoint(InputTracking.GetLocalPosition(XRNode.LeftHand)));
                    Write(_vrca.transform.TransformPoint(InputTracking.GetLocalPosition(XRNode.RightHand)));
                    Write(InputTracking.GetLocalRotation(XRNode.Head) * _vrca.transform.rotation);
                    Write(InputTracking.GetLocalRotation(XRNode.LeftHand) * _vrca.transform.rotation);
                    Write(InputTracking.GetLocalRotation(XRNode.RightHand) * _vrca.transform.rotation);
                }
                if (content.HasFlag(TelemetryFrameContents.AbsoluteScore))
                {
                    _writer.Write(_rawScore.Value);
                    _writer.Write(_modifiedScore.Value);
                    _rawScore = _modifiedScore = null;
                }
                if (content.HasFlag(TelemetryFrameContents.LeftHitDelta))
                {
                    _writer.Write((byte)_leftHitDelta);
                    _leftHitDelta = 0;
                }
                if (content.HasFlag(TelemetryFrameContents.RightHitDelta))
                {
                    _writer.Write((byte)_rightHitDelta);
                    _rightHitDelta = 0;
                }
                if (content.HasFlag(TelemetryFrameContents.LeftMissDelta))
                {
                    _writer.Write((byte)_leftMissDelta);
                    _leftMissDelta = 0;
                }
                if (content.HasFlag(TelemetryFrameContents.RightMissDelta))
                {
                    _writer.Write((byte)_rightMissDelta);
                    _rightMissDelta = 0;
                }
                _writer.Flush();
            }

            _newData.Set();

#if DEBUG_TEXT
            _text.text = $"BeatBrain ACTIVE: {Steamworks.SteamFriends.GetPersonaName()}\n{frames++:0,0} frames";
            _text.ForceMeshUpdate();
#endif
        }

        private void NoteCut(NoteData arg1, NoteCutInfo arg2, int arg3)
        {
            if (arg1.noteType == NoteType.NoteA)
            {
                _leftHitDelta++;
            }
            else if (arg1.noteType == NoteType.NoteB)
            {
                _rightHitDelta++;
            }
        }

        private void NoteMissed(NoteData arg1, int arg3)
        {
            if (arg1.noteType == NoteType.NoteA)
            {
                _leftMissDelta++;
            }
            else if (arg1.noteType == NoteType.NoteB)
            {
                _rightMissDelta++;
            }
        }

        private void ScoreChanged(int rawScore, int scoreWithModifier)
        {
            _rawScore = rawScore;
            _modifiedScore = scoreWithModifier;
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