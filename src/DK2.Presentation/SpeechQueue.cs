// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

/// <summary>Sixteen FIFO speech slots, deduped by queued event and category; mentor-speech-and-notifications.md section2.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/mentor-speech-and-notifications.md#2-the-speech-queue")]
public sealed class SpeechQueue
{
    private readonly Queue<SoundRequest> _waiting = new();
    private long? _lastEndedMilliseconds;
    public SoundRequest? Current { get; private set; }
    public int WaitingCount => _waiting.Count;
    public bool Enqueue(SoundRequest line)
    {
        if (line.Id.Value == 0 || string.IsNullOrEmpty(line.Category.Name)) return false;
        if (_waiting.Count == 16 || _waiting.Any(q => q.Id == line.Id && q.Category == line.Category)) return false;
        _waiting.Enqueue(line);
        return true;
    }
    public void ClearWaiting() => _waiting.Clear();
    public void Stop(long nowMilliseconds)
    {
        _waiting.Clear(); Current = null; _lastEndedMilliseconds = nowMilliseconds;
    }
    public SoundRequest? Update(long nowMilliseconds, bool linePlaying)
    {
        if (Current.HasValue)
        {
            if (linePlaying) return null;
            Current = null; _lastEndedMilliseconds = nowMilliseconds;
        }
        if (_waiting.Count == 0 || _lastEndedMilliseconds is long ended && nowMilliseconds <= ended + 1000) return null;
        Current = _waiting.Dequeue();
        return Current;
    }
}
