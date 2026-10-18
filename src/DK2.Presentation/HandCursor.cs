// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation;

public enum HandCursorKind { Default, Point, SmallPoint, Pickup, Dig }

public readonly record struct HandCursorSprite(string FileName, Vector2 FrameSize, Vector2 Hotspot,
    int FrameCount, int FramesPerSecond);

public readonly record struct HandCursorPlacement(Vector2 Origin, Vector2 Size);

[DK2.Core.Dk2Behavior(DK2.Core.Confidence.Confirmed, "docs/research/hand-cursor-rendering.md")]
public static class HandCursor
{
    public static HandCursorKind ForHover(bool tileTarget, bool excavationEligible, bool pickupEligible) =>
        tileTarget && excavationEligible ? HandCursorKind.Dig
            : pickupEligible ? HandCursorKind.Pickup : HandCursorKind.Point;

    public static HandCursorSprite Sprite(HandCursorKind kind) => kind switch
    {
        HandCursorKind.Point => new("Point.png", new(82, 53), new(2, 17), FrameCount: 41, FramesPerSecond: 30),
        HandCursorKind.SmallPoint => new("SmallPoint.png", new(42, 20), new(2, 2), FrameCount: 1, FramesPerSecond: 1),
        HandCursorKind.Pickup => new("Idle.png", new(83, 39), new(2, 4), FrameCount: 1, FramesPerSecond: 30),
        HandCursorKind.Dig => new("PickAxeHold.png", new(84, 86), new(4, 51), FrameCount: 1, FramesPerSecond: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static HandCursorPlacement Place(HandCursorKind kind, Vector2 pointerPixels, int screenHeight)
    {
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        HandCursorSprite sprite = Sprite(kind);
        float scale = screenHeight / 480f;
        return new(pointerPixels - sprite.Hotspot * scale, sprite.FrameSize * scale);
    }
}

public sealed class HandCursorAnimation
{
    private HandCursorKind _kind;
    private long _startedAt;
    public void Reset() => _kind = HandCursorKind.Default;
    public int Advance(HandCursorKind kind, long milliseconds)
    {
        if (_kind != kind) { _kind = kind; _startedAt = milliseconds; }
        HandCursorSprite sprite = HandCursor.Sprite(kind);
        long frame = Math.Max(0, milliseconds - _startedAt) * sprite.FramesPerSecond / 1000;
        if (frame >= sprite.FrameCount) { _startedAt = milliseconds; return 0; }
        return (int)frame;
    }
}
