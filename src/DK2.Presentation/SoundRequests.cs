// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>One request to the player. <paramref name="Stop"/> ends the voice <paramref name="Voice"/> names and plays nothing: DK2's stop command on a
/// handle (slot <c>+0x38</c>); category and id are unused.</summary>
public readonly record struct SoundRequest(SoundCategory Category, SoundId Id, Vector3? World = null, SoundVoiceId Voice = default, bool Subtitle = false, bool Stop = false, Vector3? VolumeAnchor = null);

/// <summary>Retained requests; the host consumes and clears them after the frame's input/capture.</summary>
public sealed class SoundRequests
{
    private readonly List<SoundRequest> _pending = new();
    public IReadOnlyList<SoundRequest> Pending => _pending;
    public void Add(SoundRequest request) => _pending.Add(request);
    public void Clear() => _pending.Clear();
}
