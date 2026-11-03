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
        private float _master = 1, _speechVolume = 1, _musicVolume = 1, _effectVolume = 1;
        private bool _speechOn = true, _musicOn = true, _effectsOn = true;
        private sealed class LinkedChannel
        {
            public AudioSource Source;
            public AudioClip Clip;
            public PcmAudioQueue Samples;
            public MusicSegmentPicker Picker;
            public SoundResolver Resolver;
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
                _music.Source.volume = _musicOn ? _master * _musicVolume : 0;
                Fill(_music, frame.MusicIntensity);
            }
            if (_ambience != null)
            {
                _ambience.Source.volume = _effectsOn ? _master * _effectVolume : 0;
                Fill(_ambience, frame.AmbienceIntensity);
            }
        }
        private void Fill(LinkedChannel channel, int intensity)
        {
            channel.Picker.SetIntensity(intensity);
            if (channel.Samples.RemainingFrames > 22050) return;
            SoundItem item = channel.Picker.Pick();
            if (item == null) return;
            ResolvedSound? reference = channel.Resolver.ResolveItem(item, _random);
            if (!reference.HasValue) return;
            PcmClip clip = _assets.Clip(reference.Value);
            if (clip != null) channel.Samples.Enqueue(clip, item.Unknown0x08.Length >= 5 ? item.Unknown0x08[4] / 100f : 1);
        }
        private AudioSource _speech;
        private readonly SpeechQueue _speechQueue = new SpeechQueue();
        public bool SpeechEnabled => _speechOn;
        public SoundRequest? CurrentSpeech => _speech.isPlaying ? _speechQueue.Current : null;
        public bool SpeechBusy => _speechQueue.Current.HasValue || _speechQueue.WaitingCount > 0;
        public void QueueSpeech(SoundCategory category, SoundId id, bool clearWaiting = false, bool subtitle = false)
        {
            if (clearWaiting) _speechQueue.ClearWaiting();
            if (_speechOn) _speechQueue.Enqueue(new SoundRequest(category, id, Subtitle: subtitle));
        }
        public void QueueSpeech(SoundRequests requests)
        {
            foreach (SoundRequest request in requests.Pending) QueueSpeech(request.Category, request.Id, subtitle: request.Subtitle);
            requests.Clear();
        }
        public void StopSpeech(long nowMilliseconds)
        {
            _speech.Stop(); _speechQueue.Stop(nowMilliseconds);
        }
        public void UpdateSpeech(long nowMilliseconds)
        {
            SoundRequest? next = _speechQueue.Update(nowMilliseconds, _speech.isPlaying);
            if (!next.HasValue) return;
            AudioClip clip = Resolve(next.Value.Category, next.Value.Id);
            if (clip == null) return;
            _speech.clip = clip; _speech.volume = _speechOn ? _master * _speechVolume : 0;
            _speech.Play();
        }
        public ClassicSoundPlayer(Transform parent, IClassicSoundAssets assets)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
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
            _musicVolume = Mathf.Clamp01(music / 100f); _effectVolume = Mathf.Clamp01(effects / 100f);
            if (!speechOn && _speechOn) StopSpeech((long)(Time.unscaledTimeAsDouble * 1000));
            _speechOn = speechOn; _musicOn = musicOn; _effectsOn = effectsOn;
            foreach (AudioSource source in _effects) source.volume = _effectsOn ? _master * _effectVolume : 0;
            _speech.volume = _speechOn ? _master * _speechVolume : 0;
            if (_music != null) _music.Source.volume = _musicOn ? _master * _musicVolume : 0;
            if (_ambience != null) _ambience.Source.volume = _effectsOn ? _master * _effectVolume : 0;
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
                if (stopped != null) stopped.Stop();
                return;
            }
            if (!_effectsOn || request.Category.IsNone || !_throttle.Admit(request.Id)) return;
            SoundResolver resolver = Resolver(request.Category);
            SoundPriority? priority = resolver.PriorityOf(request.Id);
            if (!priority.HasValue || _voices.CanRetain(request.Voice, priority.Value, source => source.isPlaying)) return;
            ResolvedSound? reference = resolver.Resolve(request.Id, _random);
            AudioClip clip = reference.HasValue ? Clip(reference.Value) : null;
            if (clip == null) return;
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
            source.volume = _master * _effectVolume;
            source.Play();
            _voices.Assign(request.Voice, request.Id, source, priority.Value);
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
                if (!_terrainBedsWanted.Contains(entry.Key) && _voices.Release(entry.Value) is AudioSource ended) ended.Stop();
        }

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
        private AudioClip Resolve(SoundCategory category, SoundId id)
        {
            ResolvedSound? reference = Resolver(category).Resolve(id, _random);
            return reference.HasValue ? Clip(reference.Value) : null;
        }

        private AudioClip Clip(ResolvedSound reference)
        {
            PcmClip pcm = _assets.Clip(reference);
            if (pcm == null) return null;
            if (_clips.TryGetValue(pcm, out AudioClip clip)) return clip;
            clip = AudioClip.Create(reference.Archive + ":" + reference.Entry.Value,
                pcm.Samples.Length / pcm.Channels, pcm.Channels, pcm.SampleRate, false);
            var samples = new float[pcm.Samples.Length];
            for (int i = 0; i < samples.Length; i++) samples[i] = pcm.Samples[i] / 32768f;
            clip.SetData(samples, 0); _clips.Add(pcm, clip);
            return clip;
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
