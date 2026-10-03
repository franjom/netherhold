// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

[Flags]
public enum CreatureAnimationFlags : uint
{
    None = 0,
    Present = 1,
    StartSegment = 8,
    EndSegment = 16,
}

/// <summary>Definition metadata used by the integer action clock; no rendering resource is retained.</summary>
public readonly record struct CreatureAnimationTiming
{
    public byte ResourceType { get; init; }
    public CreatureAnimationFlags Flags { get; init; }
    public ushort FrameCount { get; init; }
    public ushort FramesPerSecond { get; init; }
    public byte StartSegmentFrames { get; init; }
    public byte EndSegmentFrames { get; init; }
}
