// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

public readonly record struct SoundVoiceId(uint Value)
{
    public static SoundVoiceId None => default;
}

/// <summary>Tracks exclusive ownership of pooled sources; assigning a source always revokes its old voice.</summary>
public sealed class SoundVoiceBindings<TSource> where TSource : class
{
    private readonly Dictionary<SoundVoiceId, (SoundId Sound, SoundPriority Priority, TSource Source)> _voices = new();
    public void Assign(SoundVoiceId voice, SoundId sound, TSource source, SoundPriority priority = default)
    {
        SoundVoiceId? previous = null;
        foreach (var entry in _voices)
            if (ReferenceEquals(entry.Value.Source, source)) { previous = entry.Key; break; }
        if (previous.HasValue) _voices.Remove(previous.Value);
        if (voice != SoundVoiceId.None) _voices[voice] = (sound, priority, source);
    }
    public bool TryGet(SoundVoiceId voice, out SoundId sound, out TSource? source)
    {
        if (_voices.TryGetValue(voice, out var found)) { sound = found.Sound; source = found.Source; return true; }
        sound = default; source = null; return false;
    }
    public bool CanRetain(SoundVoiceId voice, SoundPriority priority, Func<TSource, bool> playing) =>
        _voices.TryGetValue(voice, out var found) && found.Priority.Value >= priority.Value && playing(found.Source);
}
