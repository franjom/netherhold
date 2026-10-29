// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>Main-thread decoded segments consumed by a stereo PCM audio callback; never decodes on that callback.</summary>
public sealed class PcmAudioQueue(int sampleRate)
{
    private readonly object _gate = new();
    private readonly Queue<(PcmClip Clip, float Gain)> _queued = new();
    private PcmClip? _current;
    private float _gain;
    private int _frame;
    private long _remaining;
    public long RemainingFrames { get { lock (_gate) return _remaining; } }
    public void Enqueue(PcmClip clip, float gain = 1)
    {
        if (clip.SampleRate != sampleRate || clip.Channels is not (1 or 2) || clip.Samples.Length % clip.Channels != 0)
            throw new ArgumentException("Queued audio must match the stream rate and be mono or stereo.", nameof(clip));
        lock (_gate) { _queued.Enqueue((clip, gain)); _remaining += clip.Samples.Length / clip.Channels; }
    }
    public void Read(float[] samples)
    {
        if ((samples.Length & 1) != 0) throw new ArgumentException("Stereo audio buffer must contain complete frames.", nameof(samples));
        lock (_gate)
        {
            for (int i = 0; i < samples.Length; i += 2)
            {
                if (_current is null || _frame >= _current.Samples.Length / _current.Channels)
                {
                    if (_queued.Count == 0) { _current = null; Array.Clear(samples, i, samples.Length - i); return; }
                    (_current, _gain) = _queued.Dequeue(); _frame = 0;
                    if (_current.Samples.Length == 0) { i -= 2; continue; }
                }
                int source = _frame++ * _current.Channels;
                samples[i] = _current.Samples[source] / 32768f * _gain;
                samples[i + 1] = _current.Samples[source + _current.Channels - 1] / 32768f * _gain;
                _remaining--;
            }
        }
    }
}
