// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Data.Media;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner
    {
        private ClassicSoundPlayer _soundPlayer;
        private WorldSoundDirector _worldSounds;
        public ClassicSoundPlayer SoundPlayer => _soundPlayer;
        private readonly MusicDirector _musicDirector = new MusicDirector();
        private MusicWorldObserver _musicObserver;
        private MusicFrame _musicFrame;
        private long _musicTick = -1;
        private SoundCategory _levelSpeechCategory;
        private void UpdateWorldSound(WorldState world, GameDatabase database)
        {
            if (_musicObserver == null || _soundPlayer == null) return;
            while (_worldSounds != null && _worldSounds.TryTake(out SoundRequest sound)) _soundPlayer.Play(sound);
            _soundPlayer.FollowVoices(world);
            UpdateAmbience(world, database);
            _soundPlayer.UpdateThrottle();
            SoundRequest? speech = _soundPlayer.CurrentSpeech;
            SpeechLineId? line = speech.HasValue && speech.Value.Subtitle && speech.Value.Category == _levelSpeechCategory
                ? new SpeechLineId((int)speech.Value.Id.Value - 1) : (SpeechLineId?)null;
            _scriptedView.SetSpeechPlayback(_soundPlayer.SpeechEnabled, line);
            if (_musicTick != world.CurrentTick.Value)
            {
                _musicTick = world.CurrentTick.Value;
                _musicObserver.Capture(world, database, world.CurrentTick);
                _musicFrame = _musicDirector.AdvanceFrame(_soundPlayer.MusicEnabled, _soundPlayer.SoundQuality);
            }
            _soundPlayer.UpdateMusic(_musicFrame);
        }
        private TilePos? _ambienceTile;
        private long _ambienceTick = -1;
        private System.Collections.Generic.List<AmbienceGrid.Emitter> _ambienceEmitters = new System.Collections.Generic.List<AmbienceGrid.Emitter>();

        /// <summary>The terrain ambience grid (<c>0x0044E2A0</c>): the 11-by-11 tiles around the point the camera looks at, rebuilt when that tile or the turn
        /// changes, handed to the player's ambient pass. The listener is the screen's centre tile (DK2 reads its camera record), a marked deviation.</summary>
        private void UpdateAmbience(WorldState world, GameDatabase database)
        {
            TilePos? listener = UnityEngine.Camera.main != null
                ? ScreenToTile(new UnityEngine.Vector3(UnityEngine.Screen.width * .5f, UnityEngine.Screen.height * .5f, 0f)) : null;
            if (!listener.HasValue) return;
            _soundPlayer.ListenerFocus = new System.Numerics.Vector3(listener.Value.X + .5f, 0f, -(listener.Value.Y + .5f));
            if (listener == _ambienceTile && _ambienceTick == world.CurrentTick.Value) { _soundPlayer.UpdateAmbience(_ambienceEmitters); return; }
            _ambienceTile = listener;
            _ambienceTick = world.CurrentTick.Value;
            var viewer = new PlayerId((byte)viewingPlayerId);
            _ambienceEmitters = AmbienceGrid.Emitters(AmbienceGrid.Cells(world, database, viewer, listener.Value), listener.Value, _soundPlayer.HasAmbience);
            _soundPlayer.UpdateAmbience(_ambienceEmitters);
        }

        private void WireSpeech(GameDatabase database)
        {
            _musicObserver = new MusicWorldObserver(new PlayerId((byte)viewingPlayerId), _musicDirector);
            _soundPlayer.StartMusic(database.Level?.SoundTrack ?? 0);
            var category = new SoundCategory(database.Level?.SoundCategory.ToLowerInvariant() ?? "speech_mentor");
            _levelSpeechCategory = category;
            _scriptedView.SpeechRequested += (id, clear) => _soundPlayer.QueueSpeech(category, new SoundId((uint)id), clear, subtitle: true);
            _scriptedView.SpeechSkipped += () => _soundPlayer.StopSpeech((long)(UnityEngine.Time.unscaledTimeAsDouble * 1000));
        }
    }
}
