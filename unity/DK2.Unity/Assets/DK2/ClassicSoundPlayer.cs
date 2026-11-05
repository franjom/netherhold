// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using DK2.Assets;
using DK2.Data.Media;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Session-owned sources and in-memory clips, supplied through the sound asset provider.</summary>
    public sealed class ClassicSoundPlayer : IDisposable
    {
        private readonly GameObject _owner;
        private readonly IClassicSoundAssets _assets;
        private readonly SoundClipDecoder _decoder;
        private float[] _floats = Array.Empty<float>();
        private readonly List<ResolvedSound> _warm = new List<ResolvedSound>();
        private readonly SoundRand _random = new SoundRand(1);
        private readonly Dictionary<SoundCategory, SoundResolver> _resolvers = new Dictionary<SoundCategory, SoundResolver>();
        private readonly Dictionary<PcmClip, AudioClip> _clips = new Dictionary<PcmClip, AudioClip>();
        private readonly List<AudioSource> _effects = new List<AudioSource>();
        private readonly SoundVoiceBindings<AudioSource> _voices = new SoundVoiceBindings<AudioSource>();
        private readonly SoundThrottle _throttle = new SoundThrottle(NowMs());
        private static uint NowMs() => (uint)(long)(Time.realtimeSinceStartupAsDouble * 1000);
        /// <summary>The in-game manager's update runs this after the world frame, so a frame's
        /// requests are judged by the frame before (<c>0x00567500</c>). The front end never calls it.</summary>
        public void UpdateThrottle() => _throttle.BeginFrame(NowMs());
        private float _master = 1, _speechVolume = 1;
        private int _musicPercent = 100, _effectPercent = 100;
        private bool _ducked;
        /// <summary>While a speech line plays DK2 runs music and effects at two thirds of their settings, set when the line is asked and restored when it ends (<c>0x00568490</c>, <c>0x005681D0</c>).</summary>
        private float MusicVolume => (_ducked ? SpeechDuck.Percent(_musicPercent) : _musicPercent) / 100f;
        private float EffectVolume => (_ducked ? SpeechDuck.Percent(_effectPercent) : _effectPercent) / 100f;
        private bool _speechOn = true, _musicOn = true, _effectsOn = true;
        private sealed class LinkedChannel
        {
            public AudioSource Source;
            public AudioClip Clip;
            public PcmAudioQueue Samples;
            public MusicSegmentPicker Picker;
            public SoundResolver Resolver;
            /// <summary>The segment picked and waiting for its decode, and its gain.</summary>
            public ResolvedSound? Decoding;
            public float DecodingGain;
        }
        private LinkedChannel _music, _ambience;
        public int SoundQuality { get; set; }
        public bool MusicEnabled => _musicOn;
        public void StartMusic(byte track)
        {
            uint id = track == 3 ? 345u : track <= 1 ? 343u : 344u;
            _music = Linked(SoundCategory.Music, new SoundId(id), "Music");
            _ambience = Linked(SoundCategory.Ambience, new SoundId(341), "Ambience");
        }
        private LinkedChannel Linked(SoundCategory category, SoundId groupId, string name)
        {
            SoundMaps maps = _assets.Maps(category);
            SoundGroup group = maps.Effects.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return null;
            var samples = new PcmAudioQueue(22050);
            var channel = new LinkedChannel
            {
                Source = NewSource(name), Samples = samples,
                Picker = new MusicSegmentPicker(group, _random.Next),
                Resolver = new SoundResolver(maps.Banks, maps.Effects),
                Clip = AudioClip.Create(name, 22050 * 60, 2, 22050, true, samples.Read, _ => { }),
            };
            channel.Source.clip = channel.Clip; channel.Source.loop = true; channel.Source.Play();
            return channel;
        }
        public void UpdateMusic(MusicFrame frame)
        {
            if (_music != null)
            {
                _music.Source.volume = _musicOn ? _master * MusicVolume : 0;
                Fill(_music, frame.MusicIntensity);
            }
            if (_ambience != null)
            {
                _ambience.Source.volume = _effectsOn ? _master * EffectVolume : 0;
                Fill(_ambience, frame.AmbienceIntensity);
            }
        }
        /// <summary>The next segment is picked when a second of audio is left, as before; its decode (up to 270 ms for a
        /// minute-long segment) runs on a worker and the segment is queued when it is done, inside that second, so the
        /// stream does not gap. Only the level's first segment is heard late, by its decode time.</summary>
        private void Fill(LinkedChannel channel, int intensity)
        {
            channel.Picker.SetIntensity(intensity);
            if (channel.Decoding is ResolvedSound decoding)
            {
                if (!_decoder.TryGet(decoding, out PcmClip decoded)) return;
                channel.Decoding = null;
                if (decoded != null) channel.Samples.Enqueue(decoded, channel.DecodingGain);
                return;
            }
            if (channel.Samples.RemainingFrames > 22050) return;
            SoundItem item = channel.Picker.Pick();
            if (item == null) return;
            ResolvedSound? reference = channel.Resolver.ResolveItem(item, _random);
            if (!reference.HasValue) return;
            float gain = item.Unknown0x08.Length >= 5 ? item.Unknown0x08[4] / 100f : 1;
            if (!_decoder.TryGet(reference.Value, out PcmClip clip)) { channel.Decoding = reference; channel.DecodingGain = gain; return; }
            if (clip != null) channel.Samples.Enqueue(clip, gain);
        }
        private AudioSource _speech;
        private readonly SpeechQueue _speechQueue = new SpeechQueue();
        public bool SpeechEnabled => _speechOn;
        /// <summary>The line the queue handed out whose clip is still decoding; the queue is not advanced until it plays.</summary>
        private SoundRequest? _speechDecoding;
        private ResolvedSound? _speechReference;
        public SoundRequest? CurrentSpeech => _speech.isPlaying ? _speechQueue.Current : null;
        public bool SpeechBusy => _speechQueue.Current.HasValue || _speechQueue.WaitingCount > 0;
        public void QueueSpeech(SoundCategory category, SoundId id, bool clearWaiting = false, bool subtitle = false)
        {
            if (clearWaiting) _speechQueue.ClearWaiting();
            if (_speechOn && _speechQueue.Enqueue(new SoundRequest(category, id, Subtitle: subtitle)))
            {
                // The line's clips start decoding now, while it waits its turn (at least a second after the line
                // before), so a queued line plays on time. The variant is still drawn when it plays.
                Resolver(category).SamplesOf(id, _warm);
                for (int i = 0; i < _warm.Count; i++) _decoder.TryGet(_warm[i], out _);
            }
        }
        public void QueueSpeech(SoundRequests requests)
        {
            foreach (SoundRequest request in requests.Pending) QueueSpeech(request.Category, request.Id, subtitle: request.Subtitle);
            requests.Clear();
        }
        public void StopSpeech(long nowMilliseconds)
        {
            _speech.Stop(); _speechQueue.Stop(nowMilliseconds); _speechDecoding = null; _speechReference = null;
        }
        public void UpdateSpeech(long nowMilliseconds)
        {
            // A line whose clip is decoding holds the queue: it is the queue's current line, and its audio is not
            // playing yet, which the queue would read as the line having ended.
            SoundRequest? next = _speechDecoding ?? _speechQueue.Update(nowMilliseconds, _speech.isPlaying);
            _speechDecoding = null;
            ResolvedSound? reference = null;
            if (next.HasValue) reference = _speechReference ?? Resolver(next.Value.Category).Resolve(next.Value.Id, _random);
            _speechReference = null;
            if (reference.HasValue)
            {
                if (!TryClip(reference.Value, out AudioClip clip))
                {
                    _speechDecoding = next; _speechReference = reference;
                }
                else if (clip != null)
                {
                    _speech.clip = clip; _speech.volume = _speechOn ? _master * _speechVolume : 0;
                    _speech.Play();
                }
            }
            if (_speech.isPlaying == _ducked) return;
            _ducked = _speech.isPlaying;
            foreach (AudioSource source in _effects) source.volume = _effectsOn ? _master * EffectVolume : 0;
            if (_music != null) _music.Source.volume = _musicOn ? _master * MusicVolume : 0;
            if (_ambience != null) _ambience.Source.volume = _effectsOn ? _master * EffectVolume : 0;
        }
        public ClassicSoundPlayer(Transform parent, IClassicSoundAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _decoder = new SoundClipDecoder(assets);
            _owner = new GameObject("Classic sound"); _owner.transform.SetParent(parent, false);
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();
            _speech = NewSource("Mentor");
        }

        private AudioSource NewSource(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(_owner.transform, false);
            var source = child.AddComponent<AudioSource>(); source.playOnAwake = false;
            return source;
        }

        public void SetVolumes(int master, int speech, int music, int effects, bool speechOn, bool musicOn, bool effectsOn)
        {
            _master = Mathf.Clamp01(master / 100f); _speechVolume = Mathf.Clamp01(speech / 100f);
            _musicPercent = Mathf.Clamp(music, 0, 100); _effectPercent = Mathf.Clamp(effects, 0, 100);
            if (!speechOn && _speechOn) StopSpeech((long)(Time.unscaledTimeAsDouble * 1000));
            _speechOn = speechOn; _musicOn = musicOn; _effectsOn = effectsOn;
            foreach (AudioSource source in _effects) source.volume = _effectsOn ? _master * EffectVolume : 0;
            _speech.volume = _speechOn ? _master * _speechVolume : 0;
            if (_music != null) _music.Source.volume = _musicOn ? _master * MusicVolume : 0;
            if (_ambience != null) _ambience.Source.volume = _effectsOn ? _master * EffectVolume : 0;
        }

        public void Play(SoundRequests requests)
        {
            foreach (SoundRequest request in requests.Pending) Play(request);
            requests.Clear();
        }

        public void Play(SoundRequest request)
        {
            if (request.Stop)
            {
                // DK2's stop command on a handle (slot +0x38): the voice's source ends and is free. It is not a sound, so no throttle or priority applies.
                AudioSource stopped = _voices.Release(request.Voice);
                _anchors.Remove(request.Voice);
                DropDecoding(request.Voice);
                if (stopped != null) stopped.Stop();
                return;
            }
            if (!_effectsOn || request.Category.IsNone) return;
            // The same sound already waiting on its voice is that request still on its way, not a new one
            // (the ambient pass asks each frame until its bed plays).
            if (request.Voice != SoundVoiceId.None && IsDecoding(request.Voice, request.Id)) return;
            if (!_throttle.Admit(request.Id)) return;
            SoundResolver resolver = Resolver(request.Category);
            SoundPriority? priority = resolver.PriorityOf(request.Id);
            if (!priority.HasValue || _voices.CanRetain(request.Voice, priority.Value, source => source.isPlaying)) return;
            // A sound waiting for its decode holds its voice as a playing one would.
            if (request.Voice != SoundVoiceId.None && DecodingPriority(request.Voice) is SoundPriority waiting && waiting.Value >= priority.Value.Value) return;
            ResolvedSound? reference = resolver.Resolve(request.Id, _random);
            if (!reference.HasValue) return;
            if (!TryClip(reference.Value, out AudioClip clip))
            {
                DropDecoding(request.Voice);
                _decoding.Add(new DecodingSound { Request = request, Reference = reference.Value, Priority = priority.Value, Since = NowMs() });
                return;
            }
            if (clip != null) Start(request, clip, resolver, priority.Value);
        }

        /// <summary>A sound whose clip was not decoded when it was asked for: it plays when the decode is done
        /// (<see cref="PlayDecoded"/>), unless its voice is stopped or asked again first, or it waited longer
        /// than <see cref="MaxDecodeWaitMs"/>.</summary>
        private struct DecodingSound
        {
            public SoundRequest Request;
            public ResolvedSound Reference;
            public SoundPriority Priority;
            public uint Since;
        }
        private readonly List<DecodingSound> _decoding = new List<DecodingSound>();

        /// <summary>Remake policy: an effect is heard late by its decode, normally one or two frames for an effect clip
        /// of 2-10 ms; one that would come more than half a second late is dropped rather than heard out of step.</summary>
        private const uint MaxDecodeWaitMs = 500;

        private bool IsDecoding(SoundVoiceId voice, SoundId id)
        {
            for (int i = 0; i < _decoding.Count; i++)
                if (_decoding[i].Request.Voice == voice && _decoding[i].Request.Id == id) return true;
            return false;
        }

        private SoundPriority? DecodingPriority(SoundVoiceId voice)
        {
            for (int i = 0; i < _decoding.Count; i++)
                if (_decoding[i].Request.Voice == voice) return _decoding[i].Priority;
            return null;
        }

        private void DropDecoding(SoundVoiceId voice)
        {
            if (voice == SoundVoiceId.None) return;
            for (int i = _decoding.Count - 1; i >= 0; i--)
                if (_decoding[i].Request.Voice == voice) _decoding.RemoveAt(i);
        }

        /// <summary>Once a frame, before the frame's own requests: starts the sounds whose clips have finished decoding,
        /// and logs any clip that failed. A looping sound's thing may have gone meanwhile; <see cref="FollowVoices"/> stops it
        /// on its next pass. Allocation-free when nothing is waiting and nothing failed.</summary>
        public void PlayDecoded()
        {
            while (_decoder.TryTakeFailure(out string failure)) Debug.LogWarning($"DK2: sound clip could not be decoded - {failure}");
            if (_decoding.Count == 0) return;
            uint now = NowMs();
            for (int i = 0; i < _decoding.Count; i++)
            {
                DecodingSound waiting = _decoding[i];
                bool ready = TryClip(waiting.Reference, out AudioClip clip);
                bool late = now - waiting.Since > MaxDecodeWaitMs;
                if (!ready && !late) continue;
                _decoding.RemoveAt(i--);
                if (ready && !late && clip != null && _effectsOn)
                    Start(waiting.Request, clip, Resolver(waiting.Request.Category), waiting.Priority);
            }
        }

        /// <summary>Decodes the level's likely clips on workers while it loads (<see cref="LevelSoundPrefetch"/>).</summary>
        public System.Threading.Tasks.Task Prefetch(IReadOnlyCollection<SoundCategory> categories, int workers) =>
            _decoder.Prefetch(categories, workers);

        /// <summary>Megabytes of 16-bit samples decoded so far, for the startup log.</summary>
        public double DecodedMegabytes => _decoder.DecodedSamples * 2 / (1024d * 1024d);

        private void Start(SoundRequest request, AudioClip clip, SoundResolver resolver, SoundPriority priority)
        {
            AudioSource source = null;
            _voices.TryGet(request.Voice, out _, out source);
            if (source == null) source = _effects.Find(s => !s.isPlaying);
            if (source == null && _effects.Count < 32) { source = NewSource("Effect"); _effects.Add(source); }
            if (source == null) source = _effects[0];
            // A group whose flag word has bit 0x08 repeats on its own (repeat count 9999, 0x0060CDB0), until its thing's next request or stop
            // replaces it or its thing is gone (FollowVoices); every other sound plays its sample out and is asked for again by its thing.
            source.Stop(); source.clip = clip; source.loop = resolver.IsLooping(request.Id);
            // Remake policy, not DK2's: Unity's default 3D rolloff and no voice budget. DK2's distances,
            // voice limits and fades are unread (Q678), so none are copied from OpenKeeper.
            source.spatialBlend = request.World.HasValue ? 1 : 0;
            if (request.World is System.Numerics.Vector3 world) source.transform.position = new Vector3(world.X, world.Y, world.Z);
            // A request made at the camera's point with a distance volume (the Casino party's music, 0x004D63D0): nonspatial, scaled by FollowVoices.
            if (request.VolumeAnchor is System.Numerics.Vector3 anchor && request.Voice != SoundVoiceId.None) _anchors[request.Voice] = anchor;
            else _anchors.Remove(request.Voice);
            source.volume = _master * EffectVolume * (request.VolumeAnchor is System.Numerics.Vector3 near ? AnchoredVolume.Of(near, ListenerFocus) : 1f);
            source.Play();
            _voices.Assign(request.Voice, request.Id, source, priority);
        }

        private readonly Dictionary<string, SoundVoiceId> _terrainBeds = new Dictionary<string, SoundVoiceId>();
        private readonly HashSet<string> _terrainBedsWanted = new HashSet<string>();

        /// <summary>Whether a category has an ambient group (<c>0x2EA</c>) this player can play: one item with samples.</summary>
        public bool HasAmbience(string category) =>
            !string.IsNullOrEmpty(category) && Resolver(new SoundCategory(category.ToLowerInvariant())).IsSingleItem(new SoundId(0x2EA));

        /// <summary>The service's ambient pass (<c>0x00609A30</c>): each emitter's category plays group <c>0x2EA</c> with its own voice, moved to the emitter's point
        /// while it plays and asked again once the sample ends (DK2's dead handle is dropped and the next scan builds the emitter again), and a category
        /// that has left the grid is stopped. DK2 stops it with a command carrying <c>0x46</c>; ours stops it at once (remake policy). <c>0x2EB</c> is not asked.</summary>
        public void UpdateAmbience(List<AmbienceGrid.Emitter> emitters)
        {
            _terrainBedsWanted.Clear();
            foreach (AmbienceGrid.Emitter emitter in emitters)
            {
                _terrainBedsWanted.Add(emitter.Category);
                if (!_terrainBeds.TryGetValue(emitter.Category, out SoundVoiceId voice))
                    _terrainBeds[emitter.Category] = voice = new SoundVoiceId(0x80000000u | (0x7Eu << 24) | (uint)_terrainBeds.Count);
                var position = new System.Numerics.Vector3(emitter.Position.X, emitter.Position.Y, emitter.Position.Z);
                if (_voices.TryGet(voice, out _, out AudioSource source) && source != null && source.isPlaying)
                    source.transform.position = new Vector3(position.X, source.transform.position.y, position.Z);
                else
                    Play(new SoundRequest(new SoundCategory(emitter.Category.ToLowerInvariant()), new SoundId(0x2EA), position, voice));
            }
            foreach (KeyValuePair<string, SoundVoiceId> entry in _terrainBeds)
                if (!_terrainBedsWanted.Contains(entry.Key))
                {
                    DropDecoding(entry.Value);
                    if (_voices.Release(entry.Value) is AudioSource ended) ended.Stop();
                }
        }

        private readonly Dictionary<SoundVoiceId, System.Numerics.Vector3> _anchors = new Dictionary<SoundVoiceId, System.Numerics.Vector3>();

        /// <summary>The camera's point in the view's space, which a distance-volume request measures from; none gives full volume.</summary>
        public System.Numerics.Vector3? ListenerFocus { get; set; }

        private readonly List<SoundVoiceId> _live = new List<SoundVoiceId>();

        /// <summary>DK2 replays a playing handle at its thing's position on each update of the thing (slot +0x30, which returns 0 once the instance has
        /// ended): ours moves each playing voice to where its thing now is, once a frame. A looping voice whose thing has left the world is
        /// stopped, so no loop outlives its thing. Remake policy, not DK2's (Q678): the voice budget and fades are not copied from OpenKeeper.</summary>
        public void FollowVoices(WorldState world)
        {
            _voices.Voices(_live);
            for (int i = 0; i < _live.Count; i++)
            {
                SoundVoiceId voice = _live[i];
                if (!_voices.TryGet(voice, out _, out AudioSource source) || source == null) continue;
                if (_anchors.TryGetValue(voice, out System.Numerics.Vector3 anchor))
                    source.volume = _master * EffectVolume * AnchoredVolume.Of(anchor, ListenerFocus);
                VoiceLocator.Fate fate = VoiceLocator.Locate(world, voice, out float x, out float? height, out float z);
                if (fate == VoiceLocator.Fate.Gone)
                {
                    if (!source.loop) continue;
                    source.Stop();
                    _voices.Release(voice);
                }
                else if (fate == VoiceLocator.Fate.Moves && source.isPlaying)
                    source.transform.position = new Vector3(x, height ?? source.transform.position.y, z);
            }
        }

        private SoundResolver Resolver(SoundCategory category)
        {
            if (!_resolvers.TryGetValue(category, out SoundResolver resolver))
            {
                SoundMaps maps = _assets.Maps(category);
                resolver = new SoundResolver(maps.Banks, maps.Effects); _resolvers.Add(category, resolver);
            }
            return resolver;
        }
        /// <summary>The clip for a sample, false while its decode runs on a worker (started here). A decoded sample becomes
        /// an AudioClip on first use through one reused float buffer: no decode on this thread.</summary>
        private bool TryClip(ResolvedSound reference, out AudioClip clip)
        {
            clip = null;
            if (!_decoder.TryGet(reference, out PcmClip pcm)) return false;
            if (pcm == null) return true;
            if (_clips.TryGetValue(pcm, out clip)) return true;
            clip = AudioClip.Create(reference.Archive + ":" + reference.Entry.Value,
                pcm.Samples.Length / pcm.Channels, pcm.Channels, pcm.SampleRate, false);
            if (_floats.Length < pcm.Samples.Length) _floats = new float[pcm.Samples.Length];
            for (int i = 0; i < pcm.Samples.Length; i++) _floats[i] = pcm.Samples[i] / 32768f;
            clip.SetData(new ReadOnlySpan<float>(_floats, 0, pcm.Samples.Length), 0); _clips.Add(pcm, clip);
            return true;
        }

        public void Dispose()
        {
            foreach (AudioSource source in _effects) source.Stop();
            _speech.Stop();
            if (_music != null) { _music.Source.Stop(); UnityEngine.Object.Destroy(_music.Clip); }
            if (_ambience != null) { _ambience.Source.Stop(); UnityEngine.Object.Destroy(_ambience.Clip); }
            foreach (AudioClip clip in _clips.Values) UnityEngine.Object.Destroy(clip);
            _owner.SetActive(false); UnityEngine.Object.Destroy(_owner);
        }
    }
}
