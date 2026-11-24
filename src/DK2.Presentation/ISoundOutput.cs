// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>What a source plays: an effect (a pooled world, GUI or ambience voice) or the speech line.</summary>
public enum SoundSourceRole : byte { Effect, Speech }

/// <summary>What a stream carries: the level's music or its ambience bed (<see cref="ClassicSoundMixer.StartMusic"/>).</summary>
public enum SoundStreamRole : byte { Music, Ambience }

/// <summary>The engine's audio, behind which <see cref="ClassicSoundMixer"/> decides what plays, where and how loud
/// (ADR-0805: the decisions are shared, an adapter only plays). Every call comes from the frame's thread.</summary>
public interface ISoundOutput
{
    /// <summary>A new source, silent until played; the mixer keeps it for the session.</summary>
    ISoundSource CreateSource(SoundSourceRole role);

    /// <summary>A stereo stream the engine pulls from <paramref name="samples"/> at its <see cref="PcmAudioQueue.SampleRate"/>,
    /// silence while it is empty, until it is disposed. The engine reads it on its own schedule and never decodes.</summary>
    ISoundStream CreateStream(SoundStreamRole role, PcmAudioQueue samples);

    /// <summary>A problem worth the log (a clip that failed to decode); the sound plays as silence.</summary>
    void Warn(string message);
}

/// <summary>One engine source. The mixer sets its place and volume before it plays and again while it plays.</summary>
public interface ISoundSource
{
    bool IsPlaying { get; }

    /// <summary>Starts <paramref name="clip"/> from its first sample, repeating it while <paramref name="loop"/>; what played
    /// before stops. The engine converts the clip once and keeps it.</summary>
    void Play(PcmClip clip, bool loop);

    void Stop();

    /// <summary>Linear gain, 0 to 1, with the distance already in it: the engine adds no attenuation of its own.</summary>
    void SetVolume(float volume);

    /// <summary>Where the source sounds from: null without a position (no pan), else its offset on the ground from the
    /// listener's point, in tiles along the view space's x and z (<c>DungeonScene.Ground</c>'s, (x, height, -y)). The engine
    /// sets it beside its listener, the camera, so the pan follows the camera's turn (ADR-0772).</summary>
    void Place(Vector2? offset);
}

/// <summary>A stream the engine pulls (<see cref="ISoundOutput.CreateStream"/>); disposing it stops it.</summary>
public interface ISoundStream : IDisposable
{
    /// <summary>Linear gain, 0 to 1.</summary>
    void SetVolume(float volume);
}
