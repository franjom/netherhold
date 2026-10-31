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
        private CreatureSoundDirector _creatureSounds;
        public ClassicSoundPlayer SoundPlayer => _soundPlayer;
        private readonly MusicDirector _musicDirector = new MusicDirector();
        private MusicWorldObserver _musicObserver;
        private MusicFrame _musicFrame;
        private long _musicTick = -1;
        private SoundCategory _levelSpeechCategory;
        private void UpdateWorldSound(WorldState world, GameDatabase database)
        {
            if (_musicObserver == null || _soundPlayer == null) return;
            while (_creatureSounds != null && _creatureSounds.TryTake(out SoundRequest sound)) _soundPlayer.Play(sound);
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
