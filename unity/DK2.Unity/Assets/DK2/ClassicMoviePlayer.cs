// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DK2.Data;
using DK2.Data.Media;
using Unity.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DK2.Unity
{
    /// <summary>
    /// Plays one of the install's EA movies (<c>Data\Movies\*.tgq</c>) full screen, as the front end
    /// does (<c>docs/research/movie-format.md</c> §4.7, §5). The file is read from the install at run
    /// time and decoded in memory by <c>DK2.Data.Media</c>; nothing derived is written to disk. This
    /// component only shows frames and plays samples: the audio streams through a callback clip, the
    /// next video frame is decoded ahead on a worker so a due frame only needs uploading (frames three
    /// or more behind are dropped), and the 320x480 picture is drawn at 640x480 proportions on black.
    /// Esc, Enter, Space or a mouse button ends it.
    /// </summary>
    public sealed class ClassicMoviePlayer : MonoBehaviour
    {
        [Tooltip("DK2 install folder; empty uses DK2_DIR or the known default paths.")]
        [SerializeField] private string installDirectory = "";
        [Tooltip("The movie, relative to the install folder.")]
        [SerializeField] private string moviePath = BullfrogIntro;

        public const string BullfrogIntro = "Data/Movies/BullfrogIntro.tgq";

        private const float StartDelaySeconds = 0.1f;

        private EaMovieTimeline _timeline;
        private EaAudioStream _audio;
        private readonly object _audioLock = new object();
        private readonly TqiDecoder _decoder = new TqiDecoder();
        private byte[] _rgba;
        private byte[] _ahead;
        private Task<double> _decodeAhead;
        private int _aheadIndex = -1;
        private Texture2D _texture;
        // One summary line at the end, so a sync complaint can be measured rather than guessed.
        private int _framesShown, _framesDropped;
        private double _decodeMsSum, _decodeMsMax, _lagMsSum, _lagMsMax;
        private int _frameWidth, _frameHeight;
        private Camera _camera;
        private AudioSource _source;
        private AudioClip _clip;
        private double _startDsp;
        private float _startTime;
        private int _shown = -1;
        /// <summary>True once the first picture is up; until then the caller keeps the screen covered.</summary>
        public bool HasFrame => _shown >= 0 && _texture != null;
        private bool _finished;

        /// <summary>Raised once when the movie ends or is skipped, just before the player is destroyed.</summary>
        public event Action Finished;

        /// <summary>Starts <paramref name="relativePath"/> on a new object; null install finds one.</summary>
        public static ClassicMoviePlayer Play(string relativePath = BullfrogIntro, string install = null)
        {
            var host = new GameObject("DK2 Movie");
            host.SetActive(false);
            var player = host.AddComponent<ClassicMoviePlayer>();
            player.moviePath = relativePath;
            player.installDirectory = install ?? "";
            host.SetActive(true);
            return player;
        }

        private void Start()
        {
            string install = string.IsNullOrWhiteSpace(installDirectory) ? Dk2Install.Find() : installDirectory;
            if (install == null)
            {
                Debug.LogError("DK2 movie: no Dungeon Keeper 2 installation found. Set DK2_DIR or the Install Directory field.");
                Finish();
                return;
            }

            string path = Path.Combine(install, moviePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                _timeline = new EaMovieTimeline(EaMovie.Open(path));
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                Debug.LogError($"DK2 movie: could not open {path} — {ex.Message}");
                Finish();
                return;
            }

            CreateCamera();
            if (_timeline.VideoFrames.Count > 0)
            {
                (_frameWidth, _frameHeight) = TqiDecoder.FrameSize(_timeline.Movie.Payload(_timeline.VideoFrames[0]));
                _rgba = new byte[_frameWidth * _frameHeight * 4];
                _ahead = new byte[_rgba.Length];
                DecodeAhead(0);
                _texture = new Texture2D(_frameWidth, _frameHeight, TextureFormat.RGBA32, false)
                {
                    name = "DK2 Movie Frame",
                    filterMode = FilterMode.Point, // DK2 doubles each pixel; it does not filter
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            _startTime = Time.unscaledTime + StartDelaySeconds;
            if (_timeline.HasAudio) StartAudio();
        }

        private void CreateCamera()
        {
            // Clears the screen to black under the IMGUI picture; renders no layer.
            var cameraObject = new GameObject("DK2 Movie Camera");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = 0;
            _camera.depth = 100;
            // Unity plays no sound without a listener; the empty scene the menu opens has none.
            // A running scene's own listener is kept, since two would warn every frame.
            if (FindAnyObjectByType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
        }

        private void StartAudio()
        {
            _audio = new EaAudioStream(_timeline);
            int frames = (int)Math.Min(int.MaxValue, _timeline.TotalAudioFrames);
            _clip = AudioClip.Create("DK2 Movie Audio", frames, 2, _timeline.Movie.SampleRate, true, ReadAudio, SeekAudio);
            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = _clip;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _startDsp = AudioSettings.dspTime + StartDelaySeconds;
            _source.PlayScheduled(_startDsp);
        }

        // Audio thread.
        private void ReadAudio(float[] data)
        {
            lock (_audioLock) _audio.Read(data);
        }

        // Audio thread (or main, when Unity rewinds the clip on play).
        private void SeekAudio(int frame)
        {
            lock (_audioLock) _audio.Seek(frame);
        }

        private void Update()
        {
            if (_finished) return;
            if (SkipPressed()) { Finish(); return; }

            bool audioDone;
            int showable;
            if (_timeline.HasAudio)
            {
                long played = Played();
                showable = played < 0 ? 0 : _timeline.ShowableFrames(played);
                audioDone = played >= _timeline.TotalAudioFrames;
            }
            else
            {
                showable = _timeline.ShowableFramesWithoutAudio(Time.unscaledTime - _startTime);
                audioDone = true;
            }

            int next = EaMovieTimeline.NextFrameToDecode(_shown, showable);
            if (next >= 0)
            {
                _framesDropped += next - _shown - 1;
                ShowFrame(next);
                if (_timeline.HasAudio)
                {
                    double lagMs = (Played() - _timeline.DueAudioFrame(next)) * 1000.0 / _timeline.Movie.SampleRate;
                    _lagMsSum += lagMs;
                    _lagMsMax = Math.Max(_lagMsMax, lagMs);
                }
            }

            if (audioDone && _shown >= _timeline.VideoFrames.Count - 1) Finish();
        }

        private static bool SkipPressed() =>
            Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) ||
            Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);

        private long Played() => (long)((AudioSettings.dspTime - _startDsp) * _timeline.Movie.SampleRate);

        // The worker owns _ahead and the decoder until its task completes; the main thread waits
        // for it before touching either.
        private void DecodeAhead(int index)
        {
            _aheadIndex = -1;
            _decodeAhead = null;
            if (index >= _timeline.VideoFrames.Count) return;
            EaMovieChunk chunk = _timeline.VideoFrames[index];
            var (width, height) = TqiDecoder.FrameSize(_timeline.Movie.Payload(chunk));
            if (width != _frameWidth || height != _frameHeight) return; // a size change is not expected in DK2's movies
            _aheadIndex = index;
            byte[] target = _ahead;
            _decodeAhead = Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                _decoder.Decode(_timeline.Movie.Payload(chunk), target);
                return watch.Elapsed.TotalMilliseconds;
            });
        }

        private void ShowFrame(int index)
        {
            _shown = index;
            if (_decodeAhead == null) return;
            double decodeMs = _decodeAhead.Result; // waits only if the worker is still decoding
            if (_aheadIndex != index)
            {
                // Frames were dropped past the one decoded ahead: decode the due one now.
                DecodeAhead(index);
                if (_decodeAhead == null) return;
                decodeMs = _decodeAhead.Result;
            }
            _decodeMsSum += decodeMs;
            _decodeMsMax = Math.Max(_decodeMsMax, decodeMs);
            _framesShown++;
            (_rgba, _ahead) = (_ahead, _rgba);
            DecodeAhead(index + 1);

            // The decoder writes rows top-down; Unity textures start at the bottom row.
            NativeArray<byte> pixels = _texture.GetPixelData<byte>(0);
            int stride = _frameWidth * 4;
            for (int row = 0; row < _frameHeight; row++)
                NativeArray<byte>.Copy(_rgba, row * stride, pixels, (_frameHeight - 1 - row) * stride, stride);
            _texture.Apply(false);
        }

        private void OnGUI()
        {
            if (_shown < 0 || _texture == null || Event.current.type != EventType.Repaint) return;

            // Each pixel is doubled horizontally and lines are not (0x005AABF0): the picture's
            // shape is (2 * width) x height, centred and scaled uniformly to fit the screen.
            float aspect = 2f * _frameWidth / _frameHeight;
            float w = Screen.width, h = Screen.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill, false);
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_source != null) _source.Stop();
            if (_framesShown > 0)
            {
                AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
                double mixAheadMs = 1000.0 * bufferLength * bufferCount / AudioSettings.outputSampleRate;
                Debug.Log($"DK2 movie {moviePath}: {_framesShown} frames shown, {_framesDropped} dropped; " +
                          $"decode {_decodeMsSum / _framesShown:F1} ms average, {_decodeMsMax:F1} ms max; " +
                          $"shown {_lagMsSum / _framesShown:F0} ms average after due by the mixer clock, {_lagMsMax:F0} ms max; " +
                          $"the mixer runs up to {mixAheadMs:F0} ms ahead of the speakers");
            }
            Finished?.Invoke();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _decodeAhead?.Wait(); // the worker writes into buffers this object owns
            if (_source != null) _source.Stop();
            if (_clip != null) Destroy(_clip);
            if (_texture != null) Destroy(_texture);
        }
    }
}
