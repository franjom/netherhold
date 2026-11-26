// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free sound player (<see cref="ClassicSoundMixer"/>, moved from Unity's ClassicSoundPlayer): where and how loud
/// a request plays on the engine's sources (DK2's distance mapping, qmixer-distance.md), voice retention by priority, the stop
/// command, loops that end with their thing, the speech duck (<c>0x00568490</c>), the source pool and the streams.</summary>
public sealed class ClassicSoundMixerTests
{
    private static readonly SoundCategory Effects = new("effects");
    private static readonly SoundCategory Speech = new("speech_test");
    private const uint Placed = 0x100, HeadRelative = 0x101, Looping = 0x102, Low = 0x103;

    private static FakeAssets Assets() => new FakeAssets()
        .With(Effects, SoundMapBytes.Maps("Global\\Effects",
            new SoundMapBytes.Group(Placed, 5, 0, new SoundMapBytes.Item(20f, [(1, 1, 0)])),
            new SoundMapBytes.Group(HeadRelative, 5, 0x200, new SoundMapBytes.Item(20f, [(2, 1, 0)])),
            new SoundMapBytes.Group(Looping, 5, 0x08, new SoundMapBytes.Item(20f, [(3, 1, 0)])),
            new SoundMapBytes.Group(Low, 1, 0, new SoundMapBytes.Item(20f, [(4, 1, 0)]))))
        .With(Speech, SoundMapBytes.Maps("Speech\\Test", new SoundMapBytes.Group(1, 1, 0, new SoundMapBytes.Item(2f, [(1, 1, 0)]))));

    private static async Task<(ClassicSoundMixer Mixer, FakeOutput Output)> Mixer()
    {
        var output = new FakeOutput();
        var mixer = new ClassicSoundMixer(output, Assets(), 0);
        await mixer.Prefetch([Effects, Speech], 1);
        return (mixer, output);
    }

    [Fact]
    public async Task APlacedRequestSoundsAtItsOffsetFromTheListenerAtTheMappingsGain()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.ListenerFocus = new Vector3(10f, 0f, -10f);

        mixer.Play(new SoundRequest(Effects, new SoundId(Placed), new Vector3(13f, 1f, -10f)));

        FakeSource source = Assert.Single(output.Effects);
        Assert.True(source.IsPlaying);
        Assert.False(source.Loop);
        Assert.Equal(new Vector2(3f, 0f), source.Offset);
        // Three tiles are six mixer units: 2 / (2 + 0.5 * (6 - 2)).
        Assert.Equal(.5f, source.Volume, 5);
    }

    [Fact]
    public async Task AHeadRelativeGroupPlaysWithoutAPlaceAtFullVolume()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.ListenerFocus = new Vector3(10f, 0f, -10f);

        mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative), new Vector3(30f, 0f, -10f)));

        FakeSource source = Assert.Single(output.Effects);
        Assert.Null(source.Offset);
        Assert.Equal(1f, source.Volume);
    }

    [Fact]
    public async Task ASourcePastTheMappingsMaximumIsSilentAndFollowsTheListener()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.ListenerFocus = new Vector3(0f, 0f, 0f);
        mixer.Play(new SoundRequest(Effects, new SoundId(Placed), new Vector3(20f, 0f, 0f)));
        FakeSource source = Assert.Single(output.Effects);
        Assert.Equal(0f, source.Volume);

        mixer.ListenerFocus = new Vector3(19f, 0f, 0f);
        mixer.FollowVoices(EmptyWorld());

        Assert.Equal(new Vector2(1f, 0f), source.Offset);
        Assert.Equal(1f, source.Volume);
    }

    [Fact]
    public async Task APlayingVoiceKeepsItsSoundAgainstAnEqualOrLowerPriorityAndAStopEndsIt()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        SoundVoiceId voice = WorldSoundDirector.CreatureVoice(42);
        mixer.Play(new SoundRequest(Effects, new SoundId(Placed), null, voice));
        FakeSource source = Assert.Single(output.Effects);

        mixer.Play(new SoundRequest(Effects, new SoundId(Low), null, voice));
        mixer.Play(new SoundRequest(Effects, new SoundId(Placed), null, voice));
        Assert.Equal(1, source.Plays);

        mixer.Play(new SoundRequest(default, default, null, voice, Stop: true));
        Assert.False(source.IsPlaying);
    }

    [Fact]
    public async Task ALoopingVoiceWhoseThingHasLeftTheWorldStops()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.Play(new SoundRequest(Effects, new SoundId(Looping), null, WorldSoundDirector.CreatureVoice(42)));
        FakeSource source = Assert.Single(output.Effects);
        Assert.True(source.Loop);

        mixer.FollowVoices(EmptyWorld());

        Assert.False(source.IsPlaying);
    }

    [Fact]
    public async Task APrefetchedLineStartsOnTheFrameItIsQueued()
    {
        var output = new FakeOutput();
        var mixer = new ClassicSoundMixer(output, Assets(), 0);

        await mixer.PrefetchSpeech(Speech, new SoundId(1));
        mixer.QueueSpeech(Speech, new SoundId(1));
        mixer.UpdateSpeech(5_000);

        Assert.True(output.Speech!.IsPlaying);
    }

    [Fact]
    public void AnUnprefetchedLineWaitsForItsDecode()
    {
        var output = new FakeOutput();
        var assets = new HeldAssets(Assets());
        var mixer = new ClassicSoundMixer(output, assets, 0);

        mixer.QueueSpeech(Speech, new SoundId(1));
        mixer.UpdateSpeech(5_000);
        bool playing = output.Speech!.IsPlaying;
        assets.Release.Set();

        Assert.False(playing);
        Assert.True(mixer.SpeechBusy);
    }

    /// <summary>Clips whose decode waits for <see cref="Release"/>, so a test sees the frame before a decode lands.</summary>
    private sealed class HeldAssets(FakeAssets inner) : IClassicSoundAssets
    {
        public ManualResetEventSlim Release { get; } = new();
        public SoundMaps Maps(SoundCategory category) => inner.Maps(category);

        public PcmClip? Clip(ResolvedSound reference)
        {
            Release.Wait();
            return inner.Clip(reference);
        }
    }

    [Fact]
    public async Task WhileALinePlaysEffectsRunAtTwoThirdsOfTheirSetting()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative)));
        FakeSource effect = Assert.Single(output.Effects);

        mixer.QueueSpeech(Speech, new SoundId(1), subtitle: true);
        mixer.UpdateSpeech(5_000);

        Assert.True(output.Speech!.IsPlaying);
        Assert.Equal(new SoundRequest(Speech, new SoundId(1), Subtitle: true), mixer.CurrentSpeech);
        Assert.Equal(SpeechDuck.Percent(100) / 100f, effect.Volume, 5);

        output.Speech.End();
        mixer.UpdateSpeech(5_100);
        Assert.Null(mixer.CurrentSpeech);
        Assert.Equal(1f, effect.Volume);
    }

    [Fact]
    public async Task ThePoolGrowsToItsCapThenTakesOverTheFirstSource()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        for (int i = 0; i < ClassicSoundMixer.MaxEffectSources + 1; i++)
        {
            mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative)));
        }

        Assert.Equal(ClassicSoundMixer.MaxEffectSources, output.Effects.Count);
        Assert.Equal(2, output.Effects[0].Plays);
    }

    [Fact]
    public async Task TheSwitchesSilenceTheirSources()
    {
        (ClassicSoundMixer mixer, FakeOutput output) = await Mixer();
        mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative)));
        mixer.SetVolumes(master: 50, speech: 100, music: 100, effects: 100, speechOn: true, musicOn: true, effectsOn: true);
        Assert.Equal(.5f, output.Effects[0].Volume);

        mixer.SetVolumes(master: 50, speech: 100, music: 100, effects: 100, speechOn: true, musicOn: true, effectsOn: false);
        Assert.Equal(0f, output.Effects[0].Volume);
        mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative)));
        Assert.Equal(1, output.Effects[0].Plays);
    }

    [Fact]
    public async Task AKnownClipPlaysWithoutAllocating()
    {
        (ClassicSoundMixer mixer, FakeOutput _) = await Mixer();
        WorldState world = EmptyWorld();
        var requests = new SoundRequests();
        mixer.ListenerFocus = Vector3.Zero;
        for (int i = 0; i < 40; i++)
        {
            requests.Add(new SoundRequest(Effects, new SoundId(Placed), new Vector3(i % 5, 0, 0)));
        }

        mixer.Play(requests);
        mixer.FollowVoices(world);
        mixer.UpdateSpeech(0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 1; frame <= 50; frame++)
        {
            mixer.PlayDecoded(frame * 33);
            mixer.UpdateSpeech(frame * 33);
            mixer.Play(new SoundRequest(Effects, new SoundId(Placed), new Vector3(frame % 7, 0, 0)));
            mixer.Play(new SoundRequest(Effects, new SoundId(HeadRelative)));
            mixer.FollowVoices(world);
            mixer.UpdateThrottle(frame * 33);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task TheMusicStreamQueuesItsFirstSegmentOnceItIsDecoded()
    {
        var output = new FakeOutput();
        FakeAssets assets = Assets().With(SoundCategory.Music, SoundMapBytes.Maps("Music\\Test",
            new SoundMapBytes.Group(343, 1, 0,
                new SoundMapBytes.Item(2f, [], [(2, 0, 127)]),
                new SoundMapBytes.Item(2f, [(1, 1, 0)], [(2, 0, 127)], Volume: 50))));
        var mixer = new ClassicSoundMixer(output, assets, 0);
        await mixer.Prefetch([SoundCategory.Music], 1);

        mixer.StartMusic(1);
        mixer.UpdateMusic(new MusicFrame(0, 0));

        FakeStream music = Assert.Single(output.Streams, s => s.Role == SoundStreamRole.Music);
        Assert.Equal(FakeAssets.ClipFrames, music.Samples.RemainingFrames);
        Assert.Equal(1f, music.Volume);
    }

    private static WorldState EmptyWorld()
    {
        TerrainDef terrain = new()
        {
            Name = "test", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        foreach (VariableTypeId id in Enum.GetValues<VariableTypeId>())
        {
            variables.Misc[id] = new MiscVariable(id, 0, 0, 0);
        }

        var database = new GameDatabase([terrain], [], [], variables);
        var map = new LevelMap(4, 4, [.. Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), 16)]);
        return WorldState.FromMap(database, map, [PlayerIds.Good, PlayerIds.FirstKeeper]);
    }

    internal sealed class FakeAssets : IClassicSoundAssets
    {
        public const int ClipFrames = 64;
        private readonly Dictionary<SoundCategory, SoundMaps> _maps = new();

        public FakeAssets With(SoundCategory category, SoundMaps maps)
        {
            _maps[category] = maps;
            return this;
        }

        public SoundMaps Maps(SoundCategory category) =>
            _maps.TryGetValue(category, out SoundMaps? maps) ? maps : new SoundMaps(BankMap.Empty, SfxMap.Empty);

        public PcmClip? Clip(ResolvedSound reference) => new(22050, 2, new short[ClipFrames * 2]);
    }

    internal sealed class FakeOutput : ISoundOutput
    {
        public List<FakeSource> Effects { get; } = new();
        public FakeSource? Speech { get; private set; }
        public List<FakeStream> Streams { get; } = new();

        public ISoundSource CreateSource(SoundSourceRole role)
        {
            var source = new FakeSource();
            if (role == SoundSourceRole.Speech)
            {
                Speech = source;
            }
            else
            {
                Effects.Add(source);
            }

            return source;
        }

        public ISoundStream CreateStream(SoundStreamRole role, PcmAudioQueue samples)
        {
            var stream = new FakeStream(role, samples);
            Streams.Add(stream);
            return stream;
        }

        public void Warn(string message) => throw new InvalidOperationException(message);
    }

    internal sealed class FakeSource : ISoundSource
    {
        public bool IsPlaying { get; private set; }
        public bool Loop { get; private set; }
        public int Plays { get; private set; }
        public float Volume { get; private set; } = float.NaN;
        public Vector2? Offset { get; private set; }

        public void Play(PcmClip clip, bool loop)
        {
            IsPlaying = true;
            Loop = loop;
            Plays++;
        }

        public void End() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void SetVolume(float volume) => Volume = volume;
        public void Place(Vector2? offset) => Offset = offset;
    }

    internal sealed class FakeStream(SoundStreamRole role, PcmAudioQueue samples) : ISoundStream
    {
        public SoundStreamRole Role { get; } = role;
        public PcmAudioQueue Samples { get; } = samples;
        public float Volume { get; private set; } = float.NaN;
        public void SetVolume(float volume) => Volume = volume;
        public void Dispose() { }
    }
}
