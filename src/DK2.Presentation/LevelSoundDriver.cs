// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A level's sound, driven once a frame over a <see cref="ClassicSoundMixer"/>: the simulation's thing sounds
/// (<see cref="WorldSounds"/>), the voices following their things, the terrain ambience around the listener, the throttle's
/// frame, the speech line the scripted view times its subtitles by, the music level once a settled turn and the streams. It
/// also turns the scene's beats, steps and party effects into requests. The adapter supplies the listener's tile and
/// the clock.</summary>
public sealed class LevelSoundDriver
{
    private readonly ClassicSoundMixer _mixer;
    private readonly PlayerId _viewer;
    private readonly GameDatabase _database;
    private readonly MusicDirector _musicDirector = new MusicDirector();
    private readonly SoundCategoryNames _categories = new SoundCategoryNames();
    private readonly SoundCategory _heartCategory;
    private readonly string[] _ambienceCells = new string[AmbienceGrid.Side * AmbienceGrid.Side];
    private readonly List<AmbienceGrid.Emitter> _ambienceEmitters = new List<AmbienceGrid.Emitter>();
    private readonly Func<string, bool> _hasAmbience;
    private ScriptedViewDirector? _scriptedView;
    private MusicFrame _musicFrame;
    private long _musicTick = -1;
    private TilePos? _ambienceTile;
    private long _ambienceTick = -1;

    public LevelSoundDriver(ClassicSoundMixer mixer, GameDatabase database, PlayerId viewer)
    {
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _viewer = viewer;
        Music = new MusicWorldObserver(viewer, _musicDirector);
        LevelSpeech = LevelSpeechCategory(database);
        _heartCategory = _categories.Of(HeartBeatSound.CategoryOf(database));
        _hasAmbience = mixer.HasAmbience;
    }

    public ClassicSoundMixer Mixer
    {
        get
        {
            return _mixer;
        }
    }

    /// <summary>The battles and music raises captured from each settled turn; the battle tab reads its records.</summary>
    public MusicWorldObserver Music { get; }

    /// <summary>The level's speech bank: its record's category, else the Mentor's.</summary>
    public SoundCategory LevelSpeech { get; }

    /// <summary>The simulation's thing sounds, once <see cref="Observe"/> has put it in the event chain.</summary>
    public WorldSoundDirector? WorldSounds { get; private set; }

    public static SoundCategory LevelSpeechCategory(GameDatabase database)
    {
        return new SoundCategory((database ?? throw new ArgumentNullException(nameof(database))).Level?.SoundCategory.ToLowerInvariant()
            ?? SoundCategory.Mentor.Name);
    }

    /// <summary>The sink the simulation observes for its thing sounds, forwarding every event to <paramref name="downstream"/>.</summary>
    public WorldSoundDirector Observe(ISimulationEventSink? downstream)
    {
        return WorldSounds = new WorldSoundDirector(downstream, _viewer);
    }

    /// <summary>Wire music and the scripted view's speech, Mentor lines and skips.</summary>
    public void WireSpeech(ScriptedViewDirector scriptedView)
    {
        if (_scriptedView != null)
        {
            throw new InvalidOperationException("The speech is already wired.");
        }

        _scriptedView = scriptedView ?? throw new ArgumentNullException(nameof(scriptedView));
        _mixer.StartMusic(_database.Level?.SoundTrack ?? 0);
        SoundCategory category = LevelSpeech;
        scriptedView.SpeechRequested += (id, clear) => _mixer.QueueSpeech(category, new SoundId((uint)id), clear, subtitle: true);
        // The time-limit countdown speaks from the Mentor's own bank, count001..count018 (ADR-0767).
        scriptedView.MentorSpeechRequested += (id, clear) => _mixer.QueueSpeech(SoundCategory.Mentor, new SoundId((uint)id), clear);
        scriptedView.SpeechSkipped += () => _mixer.StopSpeech(_mixer.Now);
    }

    /// <summary>Update world sounds after presentation. <paramref name="listener"/> is the tile under
    /// the screen's centre, null over the HUD or off the map, where the ambience and the listener's point keep their last.</summary>
    public void Update(WorldState world, TilePos? listener, long nowMilliseconds)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (WorldSounds != null)
        {
            while (WorldSounds.TryTake(out SoundRequest sound))
            {
                _mixer.Play(sound);
            }
        }

        _mixer.FollowVoices(world);
        UpdateAmbience(world, listener);
        _mixer.UpdateThrottle(nowMilliseconds);
        SoundRequest? speech = _mixer.CurrentSpeech;
        SpeechLineId? line = speech is SoundRequest playing && playing.Subtitle && playing.Category == LevelSpeech
            ? new SpeechLineId((int)playing.Id.Value - 1) : null;
        _scriptedView?.SetSpeechPlayback(_mixer.SpeechEnabled, line);
        CaptureMusic(world);
        _mixer.UpdateMusic(_musicFrame);
    }

    /// <summary>The music level, once a settled turn (<see cref="MusicWorldObserver"/>, <see cref="MusicDirector.AdvanceFrame"/>);
    /// <see cref="Update"/> calls it, and a host that draws the battle tab before it may call it first.</summary>
    public void CaptureMusic(WorldState world)
    {
        if (_musicTick == world.CurrentTick.Value)
        {
            return;
        }

        _musicTick = world.CurrentTick.Value;
        Music.Capture(world, _database, world.CurrentTick);
        _musicFrame = _musicDirector.AdvanceFrame(_mixer.MusicEnabled, _mixer.SoundQuality);
    }

    /// <summary>The terrain ambience grid (<c>0x0044E2A0</c>): the 11-by-11 tiles around the point the camera looks at, rebuilt
    /// when that tile or the turn changes, handed to the player's ambient pass. The listener is the screen's centre tile (DK2
    /// reads its camera record), a marked deviation.</summary>
    private void UpdateAmbience(WorldState world, TilePos? listener)
    {
        if (listener is not TilePos tile)
        {
            return;
        }

        _mixer.ListenerFocus = new Vector3(tile.X + .5f, 0f, -(tile.Y + .5f));
        if (listener != _ambienceTile || _ambienceTick != world.CurrentTick.Value)
        {
            _ambienceTile = listener;
            _ambienceTick = world.CurrentTick.Value;
            AmbienceGrid.Cells(world, _database, _viewer, tile, _ambienceCells);
            AmbienceGrid.Emitters(_ambienceCells, tile, _hasAmbience, _ambienceEmitters);
        }
        _mixer.UpdateAmbience(_ambienceEmitters);
    }

    /// <summary>A Heart's beat the scene let through (<see cref="HeartBeatSound"/>).</summary>
    public void HeartBeat(in ObjectView heart)
    {
        if (HeartBeatSound.For(_heartCategory, heart) is SoundRequest beat)
        {
            _mixer.Play(beat);
        }
    }

    /// <summary>A creature's footstep at its drawn point in the view's space (<see cref="CreatureFootsteps"/>); the host asks
    /// only for a creature the camera sees, as DK2's pass draws only those.</summary>
    public void Footstep(int creatureId, string category, bool wading, int crossing, Vector3 position)
    {
        if (CreatureFootsteps.For(_categories.Of(category), creatureId, crossing, wading, position) is SoundRequest step)
        {
            _mixer.Play(step);
        }
    }

    /// <summary>An effect the effect model created: a Casino party's pop (<see cref="PartyPopSound"/>).</summary>
    public void EffectCreated(ViewEffectRequest effect)
    {
        if (PartyPopSound.For(effect) is SoundRequest pop)
        {
            _mixer.Play(pop);
        }
    }
}
