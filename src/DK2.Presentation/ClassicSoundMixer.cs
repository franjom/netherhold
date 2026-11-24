// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The classic sound player's decisions without an engine (ADR-0805): which request plays on which pooled source, at
/// what place and volume, the speech queue and its duck, the music and ambience streams and the terrain beds. The engine's
/// sources come through <see cref="ISoundOutput"/>. Unity's <c>ClassicSoundPlayer</c> makes the same decisions and keeps its own
/// copy until Unity is removed (migration plan step 5). A frame calls <see cref="PlayDecoded"/> and <see cref="UpdateSpeech"/>
/// first, then the level's driver (<see cref="LevelSoundDriver.Update"/>) and the frame's requests. Clips are decoded off the
/// frame's thread (<see cref="SoundClipDecoder"/>); a frame that plays known clips allocates nothing.</summary>
public sealed class ClassicSoundMixer : IDisposable
{
    /// <summary>The effect sources the pool grows to; past it the first is taken over. Remake policy: DK2's voice limits are unread (Q678).</summary>
    public const int MaxEffectSources = 32;

    /// <summary>Remake policy: an effect is heard late by its decode, normally one or two frames for an effect clip of
    /// 2-10 ms; one that would come more than half a second late is dropped rather than heard out of step.</summary>
    private const uint MaxDecodeWaitMs = 500;

    /// <summary>The music and ambience segments' rate; the queue starts its next segment with a second left.</summary>
    private const int StreamRate = 22050;

    private static readonly Func<Source, bool> s_playing = source => source.Engine.IsPlaying;

    private readonly ISoundOutput _output;
    private readonly IClassicSoundAssets _assets;
    private readonly SoundClipDecoder _decoder;
    private readonly List<ResolvedSound> _warm = new();
    private readonly SoundRand _random = new(1);
    private readonly Dictionary<SoundCategory, SoundResolver> _resolvers = new();
    private readonly SoundCategoryNames _categories = new();
    private readonly List<Source> _effects = new();
    private readonly SoundVoiceBindings<Source> _voices = new();
    private readonly SoundThrottle _throttle;
    private readonly List<DecodingSound> _decoding = new();
    private readonly List<SoundVoiceId> _live = new();
    private readonly Dictionary<string, SoundVoiceId> _terrainBeds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terrainBedsWanted = new(StringComparer.Ordinal);
    private readonly Source _speech;
    private readonly SpeechQueue _speechQueue = new();
    private LinkedChannel? _music, _ambience;
    private float _master = 1, _speechVolume = 1;
    private int _musicPercent = 100, _effectPercent = 100;
    private bool _ducked, _speechOn = true, _musicOn = true, _effectsOn = true;
    /// <summary>The line the queue handed out whose clip is still decoding; the queue is not advanced until it plays.</summary>
    private SoundRequest? _speechDecoding;
    private ResolvedSound? _speechReference;

    /// <param name="nowMilliseconds">The frame clock the throttle starts from (<see cref="Now"/>).</param>
    public ClassicSoundMixer(ISoundOutput output, IClassicSoundAssets assets, long nowMilliseconds)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _decoder = new SoundClipDecoder(assets);
        _throttle = new SoundThrottle(unchecked((uint)nowMilliseconds));
        Now = nowMilliseconds;
        _speech = new Source(output.CreateSource(SoundSourceRole.Speech));
    }

    /// <summary>The frame clock in milliseconds, as the last frame call passed it.</summary>
    public long Now { get; private set; }

    public int SoundQuality { get; set; }

    /// <summary>Raised when a request starts on a source, with the sample it plays (the speech line's too): the adapters' sound
    /// log (Godot's <c>--sound-log</c>). Nothing subscribes in play.</summary>
    public event Action<SoundRequest, ResolvedSound>? Started;
    public bool MusicEnabled => _musicOn;
    public bool SpeechEnabled => _speechOn;

    /// <summary>The line playing now, or null; subtitles are timed by it (<see cref="ScriptedViewDirector.SetSpeechPlayback"/>).</summary>
    public SoundRequest? CurrentSpeech => _speech.Engine.IsPlaying ? _speechQueue.Current : null;

    /// <summary>Whether a line plays, decodes or waits its turn.</summary>
    public bool SpeechBusy => _speechQueue.Current.HasValue || _speechQueue.WaitingCount > 0;

    /// <summary>The camera's point in the view's space, which a placed source and a distance-volume request measure from;
    /// none plays a placed source at the listener at full volume.</summary>
    public Vector3? ListenerFocus { get; set; }

    /// <summary>While a speech line plays DK2 runs music and effects at two thirds of their settings, set when the line is asked and restored when it ends (<c>0x00568490</c>, <c>0x005681D0</c>).</summary>
    private float MusicVolume => (_ducked ? SpeechDuck.Percent(_musicPercent) : _musicPercent) / 100f;
    private float EffectVolume => (_ducked ? SpeechDuck.Percent(_effectPercent) : _effectPercent) / 100f;

    /// <summary>The level's music (group 343, 344 or 345 by its track) and ambience bed (group 341), each a linked group
    /// streamed segment by segment (docs/research/music.md).</summary>
    public void StartMusic(byte track)
    {
        _music?.Stream.Dispose();
        _ambience?.Stream.Dispose();
        uint id = track == 3 ? 345u : track <= 1 ? 343u : 344u;
        _music = Linked(SoundCategory.Music, new SoundId(id), SoundStreamRole.Music);
        _ambience = Linked(SoundCategory.Ambience, new SoundId(341), SoundStreamRole.Ambience);
        ApplyVolumes();
    }

    private LinkedChannel? Linked(SoundCategory category, SoundId groupId, SoundStreamRole role)
    {
        SoundMaps maps = _assets.Maps(category);
        SoundGroup? group = null;
        foreach (SoundGroup candidate in maps.Effects.Groups)
            if (candidate.Id == groupId) { group = candidate; break; }
        if (group == null) return null;
        var samples = new PcmAudioQueue(StreamRate);
        return new LinkedChannel(_output.CreateStream(role, samples), samples, new MusicSegmentPicker(group, _random.Next),
            new SoundResolver(maps.Banks, maps.Effects));
    }

    public void UpdateMusic(MusicFrame frame)
    {
        if (_music != null) Fill(_music, frame.MusicIntensity);
        if (_ambience != null) Fill(_ambience, frame.AmbienceIntensity);
    }

    /// <summary>The next segment is picked when a second of audio is left; its decode (up to 270 ms for a minute-long segment)
    /// runs on a worker and the segment is queued when it is done, inside that second, so the stream does not gap. Only the
    /// level's first segment is heard late, by its decode time.</summary>
    private void Fill(LinkedChannel channel, int intensity)
    {
        channel.Picker.SetIntensity(intensity);
        if (channel.Decoding is ResolvedSound decoding)
        {
            if (!_decoder.TryGet(decoding, out PcmClip? decoded)) return;
            channel.Decoding = null;
            if (decoded != null) channel.Samples.Enqueue(decoded, channel.DecodingGain);
            return;
        }
        if (channel.Samples.RemainingFrames > StreamRate) return;
        SoundItem? item = channel.Picker.Pick();
        if (item == null) return;
        ResolvedSound? reference = channel.Resolver.ResolveItem(item, _random);
        if (!reference.HasValue) return;
        float gain = item.Unknown0x08.Length >= 5 ? item.Unknown0x08[4] / 100f : 1;
        if (!_decoder.TryGet(reference.Value, out PcmClip? clip)) { channel.Decoding = reference; channel.DecodingGain = gain; return; }
        if (clip != null) channel.Samples.Enqueue(clip, gain);
    }

    public void QueueSpeech(SoundCategory category, SoundId id, bool clearWaiting = false, bool subtitle = false)
    {
        if (clearWaiting) _speechQueue.ClearWaiting();
        if (_speechOn && _speechQueue.Enqueue(new SoundRequest(category, id, Subtitle: subtitle)))
        {
            // The line's clips start decoding now, while it waits its turn (at least a second after the line before), so a
            // queued line plays on time. The variant is still drawn when it plays.
            Resolver(category).SamplesOf(id, _warm);
            for (int i = 0; i < _warm.Count; i++) _decoder.TryGet(_warm[i], out _);
        }
    }

    /// <summary>Queues and clears <paramref name="requests"/>.</summary>
    public void QueueSpeech(SoundRequests requests)
    {
        IReadOnlyList<SoundRequest> pending = requests.Pending;
        for (int i = 0; i < pending.Count; i++) QueueSpeech(pending[i].Category, pending[i].Id, subtitle: pending[i].Subtitle);
        requests.Clear();
    }

    public void StopSpeech(long nowMilliseconds)
    {
        _speech.Engine.Stop();
        _speechQueue.Stop(nowMilliseconds);
        _speechDecoding = null;
        _speechReference = null;
    }

    /// <summary>Once a frame: the next line when the last has ended and a second has passed, then the duck.</summary>
    public void UpdateSpeech(long nowMilliseconds)
    {
        Now = nowMilliseconds;
        // A line whose clip is decoding holds the queue: it is the queue's current line, and its audio is not playing yet,
        // which the queue would read as the line having ended.
        SoundRequest? next = _speechDecoding ?? _speechQueue.Update(nowMilliseconds, _speech.Engine.IsPlaying);
        _speechDecoding = null;
        ResolvedSound? reference = null;
        if (next.HasValue) reference = _speechReference ?? Resolver(next.Value.Category).Resolve(next.Value.Id, _random);
        _speechReference = null;
        if (reference.HasValue)
        {
            if (!_decoder.TryGet(reference.Value, out PcmClip? clip))
            {
                _speechDecoding = next;
                _speechReference = reference;
            }
            else if (clip != null)
            {
                _speech.SetVolume(_speechOn ? _master * _speechVolume : 0);
                _speech.Engine.Place(null);
                _speech.Engine.Play(clip, loop: false);
                Started?.Invoke(next!.Value, reference.Value);
            }
        }
        bool speaking = _speech.Engine.IsPlaying;
        if (speaking == _ducked) return;
        _ducked = speaking;
        ApplyVolumes();
    }

    /// <summary>The settings' volumes (percent) and switches; turning speech off stops the line and its queue.</summary>
    public void SetVolumes(int master, int speech, int music, int effects, bool speechOn, bool musicOn, bool effectsOn)
    {
        _master = Clamp01(master / 100f);
        _speechVolume = Clamp01(speech / 100f);
        _musicPercent = Math.Min(Math.Max(music, 0), 100);
        _effectPercent = Math.Min(Math.Max(effects, 0), 100);
        if (!speechOn && _speechOn) StopSpeech(Now);
        _speechOn = speechOn;
        _musicOn = musicOn;
        _effectsOn = effectsOn;
        ApplyVolumes();
    }

    private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private void ApplyVolumes()
    {
        for (int i = 0; i < _effects.Count; i++) _effects[i].SetVolume(EffectGain(_effects[i]));
        _speech.SetVolume(_speechOn ? _master * _speechVolume : 0);
        _music?.Stream.SetVolume(_musicOn ? _master * MusicVolume : 0);
        _ambience?.Stream.SetVolume(_effectsOn ? _master * EffectVolume : 0);
    }

    private float EffectGain(Source source) => _effectsOn ? _master * EffectVolume * source.AnchorGain * source.DistanceGain : 0;

    /// <summary>Plays and clears <paramref name="requests"/>.</summary>
    public void Play(SoundRequests requests)
    {
        IReadOnlyList<SoundRequest> pending = requests.Pending;
        for (int i = 0; i < pending.Count; i++) Play(pending[i]);
        requests.Clear();
    }

    public void Play(in SoundRequest request)
    {
        if (request.Stop)
        {
            // DK2's stop command on a handle (slot +0x38): the voice's source ends and is free. It is not a sound, so no
            // throttle or priority applies.
            Source? stopped = _voices.Release(request.Voice);
            DropDecoding(request.Voice);
            if (stopped != null) { stopped.Engine.Stop(); stopped.FollowAnchor = null; }
            return;
        }
        if (!_effectsOn || request.Category.IsNone) return;
        // The same sound already waiting on its voice is that request still on its way, not a new one (the ambient pass asks
        // each frame until its bed plays).
        if (request.Voice != SoundVoiceId.None && IsDecoding(request.Voice, request.Id)) return;
        if (!_throttle.Admit(request.Id)) return;
        SoundResolver resolver = Resolver(request.Category);
        SoundPriority? priority = resolver.PriorityOf(request.Id);
        if (!priority.HasValue || _voices.CanRetain(request.Voice, priority.Value, s_playing)) return;
        // A sound waiting for its decode holds its voice as a playing one would.
        if (request.Voice != SoundVoiceId.None && DecodingPriority(request.Voice) is SoundPriority waiting
            && waiting.Value >= priority.Value.Value) return;
        ResolvedSound? reference = resolver.Resolve(request.Id, _random);
        if (!reference.HasValue) return;
        if (!_decoder.TryGet(reference.Value, out PcmClip? clip))
        {
            DropDecoding(request.Voice);
            _decoding.Add(new DecodingSound(request, reference.Value, priority.Value, unchecked((uint)Now)));
            return;
        }
        if (clip != null) Start(request, reference.Value, clip, resolver, priority.Value);
    }

    /// <summary>A sound whose clip was not decoded when it was asked for: it plays when the decode is done
    /// (<see cref="PlayDecoded"/>), unless its voice is stopped or asked again first, or it waited longer than
    /// <see cref="MaxDecodeWaitMs"/>.</summary>
    private readonly struct DecodingSound(SoundRequest request, ResolvedSound reference, SoundPriority priority, uint since)
    {
        public SoundRequest Request { get; } = request;
        public ResolvedSound Reference { get; } = reference;
        public SoundPriority Priority { get; } = priority;
        public uint Since { get; } = since;
    }

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

    /// <summary>Once a frame, before the frame's own requests: starts the sounds whose clips have finished decoding, and
    /// reports any clip that failed. A looping sound's thing may have gone meanwhile; <see cref="FollowVoices"/> stops it on
    /// its next pass. Allocation-free when nothing is waiting and nothing failed.</summary>
    public void PlayDecoded(long nowMilliseconds)
    {
        Now = nowMilliseconds;
        while (_decoder.TryTakeFailure(out string failure)) _output.Warn("sound clip could not be decoded - " + failure);
        if (_decoding.Count == 0) return;
        uint now = unchecked((uint)nowMilliseconds);
        for (int i = 0; i < _decoding.Count; i++)
        {
            DecodingSound waiting = _decoding[i];
            bool ready = _decoder.TryGet(waiting.Reference, out PcmClip? clip);
            bool late = now - waiting.Since > MaxDecodeWaitMs;
            if (!ready && !late) continue;
            _decoding.RemoveAt(i--);
            if (ready && !late && clip != null && _effectsOn)
                Start(waiting.Request, waiting.Reference, clip, Resolver(waiting.Request.Category), waiting.Priority);
        }
    }

    /// <summary>The in-game manager's update runs this after the world frame, so a frame's requests are judged by the frame
    /// before (<c>0x00567500</c>). The front end never calls it.</summary>
    public void UpdateThrottle(long nowMilliseconds)
    {
        Now = nowMilliseconds;
        _throttle.BeginFrame(unchecked((uint)nowMilliseconds));
    }

    /// <summary>Decodes the level's likely clips on workers while it loads (<see cref="LevelSoundPrefetch"/>).</summary>
    public Task Prefetch(IReadOnlyCollection<SoundCategory> categories, int workers) => _decoder.Prefetch(categories, workers);

    /// <summary>Megabytes of 16-bit samples decoded so far, for the startup log.</summary>
    public double DecodedMegabytes => _decoder.DecodedSamples * 2 / (1024d * 1024d);

    private void Start(in SoundRequest request, ResolvedSound reference, PcmClip clip, SoundResolver resolver, SoundPriority priority)
    {
        _voices.TryGet(request.Voice, out _, out Source? source);
        source ??= IdleEffect();
        if (source == null && _effects.Count < MaxEffectSources)
        {
            source = new Source(_output.CreateSource(SoundSourceRole.Effect));
            _effects.Add(source);
        }
        source ??= _effects[0];
        source.Engine.Stop();
        // DK2's distance mapping (0x0060CDB0 into QMixer, qmixer-distance.md): full volume within a tile, 2 / (tiles + 1) out
        // to the item's own maximum, silence past it. A head-relative group (flag 0x200) has none and plays without a
        // position. No voice budget: DK2's voice limits and fades are unread (Q678).
        source.Mapping = request.World.HasValue ? resolver.DistanceMappingOf(request.Id) : null;
        source.Flat = request.World is Vector3 world ? new Vector2(world.X, world.Z) : null;
        // A request made at the camera's point with a distance volume (the Casino party's music, 0x004D63D0): nonspatial,
        // and its volume follows the camera while its voice plays.
        source.AnchorGain = request.VolumeAnchor is Vector3 near ? AnchoredVolume.Of(near, ListenerFocus) : 1f;
        source.FollowAnchor = request.Voice != SoundVoiceId.None ? request.VolumeAnchor : null;
        Place(source);
        source.SetVolume(EffectGain(source));
        // A group whose flag word has bit 0x08 repeats on its own (repeat count 9999, 0x0060CDB0), until its thing's next
        // request or stop replaces it or its thing is gone (FollowVoices); every other sound plays its sample out and is
        // asked for again by its thing.
        source.Loop = resolver.IsLooping(request.Id);
        source.Engine.Play(clip, source.Loop);
        _voices.Assign(request.Voice, request.Id, source, priority);
        Started?.Invoke(request, reference);
    }

    private Source? IdleEffect()
    {
        for (int i = 0; i < _effects.Count; i++)
            if (!_effects[i].Engine.IsPlaying) return _effects[i];
        return null;
    }

    // DK2 hands the mixer each source flat and relative to its listener point, the camera's focus (0x005678F0: x and y less
    // the listener's +0x4C/+0x50, the vertical slot 0), and QMixer's listener sits at the origin. The engine's listener is the
    // camera, above and behind that focus, so each placed source stands beside it at its flat offset from the focus and its
    // gain is the mapping's at that distance: the distance is DK2's, and the pan follows the camera's turn (ADR-0772).
    private void Place(Source source)
    {
        if (source.Mapping is not SoundDistanceMapping mapping || source.Flat is not Vector2 flat)
        {
            source.DistanceGain = 1f;
            source.PlaceEngine(null);
            return;
        }
        Vector2 offset = ListenerFocus is Vector3 focus ? new Vector2(flat.X - focus.X, flat.Y - focus.Z) : Vector2.Zero;
        source.DistanceGain = mapping.Factor(offset.Length() * SoundDistanceMapping.UnitsPerTile);
        source.PlaceEngine(offset);
    }

    private SoundCategory Category(string name) => _categories.Of(name);

    /// <summary>Whether a category has an ambient group (<c>0x2EA</c>) this player can play: one item with samples.</summary>
    public bool HasAmbience(string category) =>
        !string.IsNullOrEmpty(category) && Resolver(Category(category)).IsSingleItem(new SoundId(0x2EA));

    /// <summary>The service's ambient pass (<c>0x00609A30</c>): each emitter's category plays group <c>0x2EA</c> with its own voice,
    /// moved to the emitter's point while it plays and asked again once the sample ends (DK2's dead handle is dropped and the next
    /// scan builds the emitter again), and a category that has left the grid is stopped. DK2 stops it with a command carrying
    /// <c>0x46</c>; ours stops it at once (remake policy). <c>0x2EB</c> is not asked.</summary>
    public void UpdateAmbience(List<AmbienceGrid.Emitter> emitters)
    {
        _terrainBedsWanted.Clear();
        for (int i = 0; i < emitters.Count; i++)
        {
            AmbienceGrid.Emitter emitter = emitters[i];
            _terrainBedsWanted.Add(emitter.Category);
            if (!_terrainBeds.TryGetValue(emitter.Category, out SoundVoiceId voice))
                _terrainBeds[emitter.Category] = voice = new SoundVoiceId(0x80000000u | (0x7Eu << 24) | (uint)_terrainBeds.Count);
            if (_voices.TryGet(voice, out _, out Source? source) && source != null && source.Engine.IsPlaying)
            {
                source.Flat = new Vector2(emitter.Position.X, emitter.Position.Z);
                Place(source);
                source.SetVolume(EffectGain(source));
            }
            else Play(new SoundRequest(Category(emitter.Category), new SoundId(0x2EA), emitter.Position, voice));
        }
        foreach (KeyValuePair<string, SoundVoiceId> entry in _terrainBeds)
            if (!_terrainBedsWanted.Contains(entry.Key))
            {
                DropDecoding(entry.Value);
                _voices.Release(entry.Value)?.Engine.Stop();
            }
    }

    /// <summary>DK2 replays a playing handle at its thing's position on each update of the thing (slot +0x30, which returns 0
    /// once the instance has ended): ours moves each playing voice to where its thing now is, once a frame. A looping voice
    /// whose thing has left the world is stopped, so no loop outlives its thing. Each playing placed source then follows the
    /// listener. Remake policy, not DK2's (Q678): the voice budget and fades are not copied from OpenKeeper.</summary>
    public void FollowVoices(WorldState world)
    {
        _voices.Voices(_live);
        for (int i = 0; i < _live.Count; i++)
        {
            SoundVoiceId voice = _live[i];
            if (!_voices.TryGet(voice, out _, out Source? source) || source == null) continue;
            if (source.FollowAnchor is Vector3 anchor) source.AnchorGain = AnchoredVolume.Of(anchor, ListenerFocus);
            VoiceLocator.Fate fate = VoiceLocator.Locate(world, voice, out float x, out _, out float z);
            if (fate == VoiceLocator.Fate.Gone)
            {
                if (!source.Loop) continue;
                source.Engine.Stop();
                _voices.Release(voice);
            }
            else if (fate == VoiceLocator.Fate.Moves && source.Engine.IsPlaying)
                source.Flat = new Vector2(x, z);
        }
        for (int i = 0; i < _effects.Count; i++)
        {
            Source source = _effects[i];
            if (!source.Engine.IsPlaying) continue;
            Place(source);
            source.SetVolume(EffectGain(source));
        }
    }

    private SoundResolver Resolver(SoundCategory category)
    {
        if (!_resolvers.TryGetValue(category, out SoundResolver? resolver))
        {
            SoundMaps maps = _assets.Maps(category);
            resolver = new SoundResolver(maps.Banks, maps.Effects);
            _resolvers.Add(category, resolver);
        }
        return resolver;
    }

    public void Dispose()
    {
        for (int i = 0; i < _effects.Count; i++) _effects[i].Engine.Stop();
        _speech.Engine.Stop();
        _music?.Stream.Dispose();
        _ambience?.Stream.Dispose();
        _music = _ambience = null;
    }

    /// <summary>A pooled engine source and what the mixer knows of its sound: its place, mapping and gains, and the last
    /// volume and offset handed to the engine, so a frame that changes nothing calls the engine for nothing.</summary>
    private sealed class Source(ISoundSource engine)
    {
        private float _volume = float.NaN;
        private Vector2? _offset;
        private bool _placed;

        public ISoundSource Engine { get; } = engine;
        public bool Loop { get; set; }
        public SoundDistanceMapping? Mapping { get; set; }
        public Vector2? Flat { get; set; }
        public float AnchorGain { get; set; } = 1f;
        public Vector3? FollowAnchor { get; set; }
        public float DistanceGain { get; set; } = 1f;

        public void SetVolume(float volume)
        {
            if (volume == _volume) return;
            _volume = volume;
            Engine.SetVolume(volume);
        }

        public void PlaceEngine(Vector2? offset)
        {
            if (_placed && offset == _offset) return;
            _placed = true;
            _offset = offset;
            Engine.Place(offset);
        }
    }

    private sealed class LinkedChannel(ISoundStream stream, PcmAudioQueue samples, MusicSegmentPicker picker, SoundResolver resolver)
    {
        public ISoundStream Stream { get; } = stream;
        public PcmAudioQueue Samples { get; } = samples;
        public MusicSegmentPicker Picker { get; } = picker;
        public SoundResolver Resolver { get; } = resolver;
        /// <summary>The segment picked and waiting for its decode, and its gain.</summary>
        public ResolvedSound? Decoding { get; set; }
        public float DecodingGain { get; set; }
    }
}
