// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>Decodes sound clips off the frame's thread. A first-use MPEG decode costs 2-10 ms for an effect, up to
/// 120 ms for a speech line and up to 270 ms for a music segment, so no frame waits for one: <see cref="TryGet"/>
/// answers from what is decoded and starts a background decode of what is not, and <see cref="Prefetch"/> decodes
/// a level's likely clips while its loading screen is up. Remake engineering; how DK2 loads its samples is not read.</summary>
public sealed class SoundClipDecoder
{
    private readonly IClassicSoundAssets _assets;
    private readonly ConcurrentDictionary<ResolvedSound, PcmClip?> _ready = new();
    private readonly ConcurrentDictionary<ResolvedSound, byte> _decoding = new();
    private readonly ConcurrentQueue<string> _failures = new();
    private long _decodedSamples;

    public SoundClipDecoder(IClassicSoundAssets assets) => _assets = assets ?? throw new ArgumentNullException(nameof(assets));

    /// <summary>Samples (16-bit, all channels) this decoder holds decoded, for reporting its memory.</summary>
    public long DecodedSamples => Interlocked.Read(ref _decodedSamples);

    /// <summary>The clip, when it is decoded (null for an entry with no audio, or one that failed to decode); false
    /// while it is not, after starting its decode on a worker. Allocation-free once the clip is decoded.</summary>
    public bool TryGet(ResolvedSound reference, out PcmClip? clip)
    {
        if (_ready.TryGetValue(reference, out clip)) return true;
        if (_decoding.TryAdd(reference, 0)) StartDecode(reference);
        return false;
    }

    // Apart from TryGet so that its hit path allocates no closure.
    private void StartDecode(ResolvedSound reference) => Task.Run(() => Decode(reference));

    /// <summary>A failure to read or decode a clip since the last call, for the adapter to log; such a clip plays as silence.</summary>
    public bool TryTakeFailure(out string message) => _failures.TryDequeue(out message!);

    /// <summary>Decodes every sample the <paramref name="categories"/>' groups name, on a worker that reads the maps and
    /// then on up to <paramref name="workers"/> threads; the caller decides whether to wait. Clips already decoded or
    /// decoding are skipped. A category the install does not ship has no samples.</summary>
    public Task Prefetch(IReadOnlyCollection<SoundCategory> categories, int workers) => Task.Run(() =>
    {
        var samples = new HashSet<ResolvedSound>();
        foreach (SoundCategory category in categories)
        {
            try
            {
                SamplesOf(category, samples);
            }
            catch (Exception error)
            {
                // One unreadable category costs its own clips, not the level's prefetch.
                _failures.Enqueue($"{category.Name}: {error.Message}");
            }
        }
        var todo = new List<ResolvedSound>(samples.Count);
        foreach (ResolvedSound reference in samples)
            if (!_ready.ContainsKey(reference) && _decoding.TryAdd(reference, 0)) todo.Add(reference);
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers) }, Decode);
    });

    private void SamplesOf(SoundCategory category, HashSet<ResolvedSound> into)
    {
        if (category.IsNone) return;
        SoundMaps maps = _assets.Maps(category);
        foreach (SoundGroup group in maps.Effects.Groups)
            foreach (SoundItem item in group.Items)
                foreach (SoundSample sample in item.Sounds)
                    if (SoundResolver.Locate(maps.Banks, sample) is ResolvedSound found) into.Add(found);
    }

    private void Decode(ResolvedSound reference)
    {
        // A TryGet that missed just before a decode finished starts a second one; it finds the first's result here.
        if (_ready.ContainsKey(reference)) { _decoding.TryRemove(reference, out _); return; }
        PcmClip? clip = null;
        try
        {
            clip = _assets.Clip(reference);
        }
        catch (Exception error)
        {
            // A worker has no caller to throw to; the clip is kept as silence so it is not decoded again.
            _failures.Enqueue($"{reference.Archive}:{reference.Entry.Value}: {error.Message}");
        }
        if (clip != null) Interlocked.Add(ref _decodedSamples, clip.Samples.Length);
        _ready[reference] = clip;
        _decoding.TryRemove(reference, out _);
    }
}
