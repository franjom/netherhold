// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Data.Media;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Plays the mixer's sounds through Godot audio players. Positioned sounds use the camera-relative listener and update as the camera turns.</summary>
public partial class ClassicSoundOutput : Node, ISoundOutput
{
    /// <summary>Frames pushed to a stream at a time.</summary>
    private const int StreamChunk = 512;

    /// <summary>Godot scales a node's panning by the project's <c>audio/general/3d_panning_strength</c> (0.5); twice it pans fully
    /// at the side.</summary>
    private const float FullPan = 2f;

    private readonly Dictionary<PcmClip, AudioStreamWav> _plain = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PcmClip, AudioStreamWav> _looped = new(ReferenceEqualityComparer.Instance);
    private readonly List<GeneratedStream> _streams = new();
    private readonly Vector2[] _chunk = new Vector2[StreamChunk];
    private Node3D? _anchor;

    /// <summary>Whether effects are placed in the 3D world; set before the node enters the tree.</summary>
    public bool Spatial { get; init; }

    public override void _Ready()
    {
        if (!Spatial) return;
        _anchor = new Node3D { Name = "Listener" };
        AddChild(_anchor);
    }

    ISoundSource ISoundOutput.CreateSource(SoundSourceRole role)
    {
        if (Spatial && role == SoundSourceRole.Effect && _anchor != null)
        {
            var spatial = new AudioStreamPlayer3D
            {
                Name = "Effect",
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
                AttenuationFilterDb = 0,
                MaxDistance = 0,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
            _anchor.AddChild(spatial);
            return new SpatialSource(this, spatial);
        }
        var flat = new AudioStreamPlayer { Name = role == SoundSourceRole.Speech ? "Speech" : "Effect" };
        AddChild(flat);
        return new FlatSource(this, flat);
    }

    ISoundStream ISoundOutput.CreateStream(SoundStreamRole role, PcmAudioQueue samples)
    {
        var generator = new AudioStreamGenerator
        {
            MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
            MixRate = samples.SampleRate,
            BufferLength = .5f,
        };
        var player = new AudioStreamPlayer { Name = role.ToString(), Stream = generator };
        AddChild(player);
        player.Play();
        var stream = new GeneratedStream(this, player, (AudioStreamGeneratorPlayback)player.GetStreamPlayback(), samples);
        _streams.Add(stream);
        stream.Pump(_chunk);
        return stream;
    }

    void ISoundOutput.Warn(string message) => GD.PushWarning("DK2.Godot: " + message);

    public override void _Process(double delta)
    {
        // After the scene's own process, which placed the camera: the listener's anchor follows it.
        if (_anchor != null && GetViewport().GetCamera3D() is Camera3D camera) _anchor.GlobalPosition = camera.GlobalPosition;
        for (int i = 0; i < _streams.Count; i++) _streams[i].Pump(_chunk);
    }

    /// <summary>The clip as Godot plays it: 16-bit PCM at its own rate, looping over the whole clip when asked.</summary>
    private AudioStreamWav Clip(PcmClip clip, bool loop)
    {
        Dictionary<PcmClip, AudioStreamWav> cache = loop ? _looped : _plain;
        if (cache.TryGetValue(clip, out AudioStreamWav? wav)) return wav;
        wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = clip.SampleRate,
            Stereo = clip.Channels == 2,
            Data = MemoryMarshal.AsBytes(clip.Samples.AsSpan()).ToArray(),
        };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = clip.Samples.Length / Math.Max(1, clip.Channels);
        }
        cache.Add(clip, wav);
        return wav;
    }

    public override void _ExitTree()
    {
        for (int i = _streams.Count - 1; i >= 0; i--) _streams[i].Dispose();
    }

    private sealed class FlatSource(ClassicSoundOutput owner, AudioStreamPlayer player) : ISoundSource
    {
        public bool IsPlaying => player.Playing;

        public void Play(PcmClip clip, bool loop)
        {
            player.Stream = owner.Clip(clip, loop);
            player.Play();
        }

        public void Stop() => player.Stop();
        public void SetVolume(float volume) => player.VolumeLinear = volume;
        public void Place(System.Numerics.Vector2? offset) { }
    }

    private sealed class SpatialSource(ClassicSoundOutput owner, AudioStreamPlayer3D player) : ISoundSource
    {
        public bool IsPlaying => player.Playing;

        public void Play(PcmClip clip, bool loop)
        {
            player.Stream = owner.Clip(clip, loop);
            player.Play();
        }

        public void Stop() => player.Stop();
        public void SetVolume(float volume) => player.VolumeLinear = volume;

        /// <summary>The offset in the view space's x and z, which are Godot's world axes here (<see cref="RenderWorld"/>); a
        /// source with none sounds at the camera, unpanned.</summary>
        public void Place(System.Numerics.Vector2? offset)
        {
            player.PanningStrength = offset.HasValue ? FullPan : 0f;
            player.Position = offset is System.Numerics.Vector2 at ? new Vector3(at.X, 0f, at.Y) : Vector3.Zero;
        }
    }

    /// <summary>A generator fed from its queue: each frame the frames Godot has room for, in whole chunks.</summary>
    private sealed class GeneratedStream(ClassicSoundOutput owner, AudioStreamPlayer player, AudioStreamGeneratorPlayback playback,
        PcmAudioQueue samples) : ISoundStream
    {
        private bool _disposed;

        public void SetVolume(float volume) => player.VolumeLinear = volume;

        public void Pump(Vector2[] chunk)
        {
            if (_disposed) return;
            int room = playback.GetFramesAvailable();
            while (room >= chunk.Length)
            {
                samples.Read(MemoryMarshal.Cast<Vector2, float>(chunk.AsSpan()));
                playback.PushBuffer(new ReadOnlySpan<Vector2>(chunk));
                room -= chunk.Length;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            player.Stop();
            owner._streams.Remove(this);
            player.QueueFree();
        }
    }
}
