// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Concurrent;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Each drawn creature's head (KMF point 5000) in native units, null while its clip has none. The scene
/// writes it after each draw; the effects read it on the runner's worker thread (ADR-0458), so it is concurrent.</summary>
public sealed class CreatureHeadTable
{
    private readonly ConcurrentDictionary<CreatureId, FpPosition3?> _heads = new();

    /// <summary>A creature the scene now draws, with no head yet; one already tracked keeps its head.</summary>
    public void Add(CreatureId creature) => _heads.TryAdd(creature, null);

    public void Set(CreatureId creature, FpPosition3? head) => _heads[creature] = head;

    public void Remove(CreatureId creature) => _heads.TryRemove(creature, out _);

    public void Clear() => _heads.Clear();

    /// <summary>An effect's tie to a creature (0x00434E80): gone with its object, else its head as last drawn, or not
    /// found for a clip without one or another point.</summary>
    public EffectAttachment Attachment(CreatureId creature, uint point)
    {
        if (!_heads.TryGetValue(creature, out FpPosition3? head)) return EffectAttachment.Gone;
        return point == KmfControlPoint.Head && head.HasValue ? EffectAttachment.At(head.Value) : EffectAttachment.NotFound;
    }
}
