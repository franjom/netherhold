// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public enum HandCursorKind { Default, Point, SmallPoint, Pickup, Dig, Tag, Tagging, HoldThing, DropThing, HoldGold, DropGold, Slap }

public readonly record struct HandCursorSprite(string FileName, Vector2 FrameSize, Vector2 Hotspot,
    int FrameCount, int FramesPerSecond);

public readonly record struct HandCursorPlacement(Vector2 Origin, Vector2 Size);

[DK2.Core.Dk2Behavior(DK2.Core.Confidence.Confirmed, "docs/research/hand-cursor-rendering.md")]
public static class HandCursor
{
    /// <summary>Whether the hovered tile takes the excavation cursor: tagged by the viewer, Taggable
    /// terrain (CONFIRMED: the picker <c>0x005992B0</c> sets its target bit from terrain flag
    /// <c>0x08</c>), or hidden by fog (LIKELY: the owner saw the dig cursor over "Fog of War",
    /// fog-of-war.md §9.2, and the picker gates on the explored bit <c>+9</c>&amp;<c>0x20</c> with
    /// register arguments unresolved). Fog is the explored layer, as the hover text and minimap
    /// read it. The tag command (PlayerInput, TileTagSystem) still tests the scanned layer; the
    /// native tag gate is unread, so the two are not yet reconciled.</summary>
    public static bool ShowsDig(WorldState world, GameDatabase database, TilePos tile, PlayerId viewer)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        var state = world.Tile(tile);
        return state.IsTaggedBy(viewer)
            || world.FogOfWarEnabled && !state.IsExploredBy(viewer)
            || database.TerrainById(state.TerrainId).IsTaggable;
    }

    public static HandCursorKind ForHover(bool tileTarget, bool excavationEligible, bool pickupEligible) =>
        tileTarget && excavationEligible ? HandCursorKind.Dig
            : pickupEligible ? HandCursorKind.Pickup : HandCursorKind.Point;

    public static HandCursorSprite Sprite(HandCursorKind kind) => kind switch
    {
        HandCursorKind.Point => new("Point.png", new(82, 53), new(2, 17), FrameCount: 41, FramesPerSecond: 30),
        HandCursorKind.SmallPoint => new("SmallPoint.png", new(42, 20), new(2, 2), FrameCount: 1, FramesPerSecond: 1),
        HandCursorKind.Pickup => new("Idle.png", new(83, 39), new(2, 4), FrameCount: 1, FramesPerSecond: 30),
        HandCursorKind.Dig => new("PickAxeHold.png", new(84, 86), new(4, 51), FrameCount: 1, FramesPerSecond: 1),
        HandCursorKind.Tag => new("PickAxeTag.png", new(88, 65), new(7, 30), FrameCount: 1, FramesPerSecond: 30),
        HandCursorKind.Tagging => new("PickAxeHoldTagging.png", new(88, 65), new(7, 30), FrameCount: 1, FramesPerSecond: 1),
        HandCursorKind.HoldThing => new("HoldThing.png", new(74, 70), new(8, 42), FrameCount: 1, FramesPerSecond: 15),
        HandCursorKind.DropThing => new("DropThing.png", new(88, 103), new(2, 64), FrameCount: 14, FramesPerSecond: 30),
        HandCursorKind.HoldGold => new("HoldGold.png", new(63, 43), new(-18, 7), FrameCount: 1, FramesPerSecond: 15),
        HandCursorKind.DropGold => new("DropGold.png", new(87, 95), new(5, 59), FrameCount: 16, FramesPerSecond: 30),
        HandCursorKind.Slap => new("Slap.png", new(126, 99), new(2, 64), FrameCount: 15, FramesPerSecond: 30),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // Point.png frame 0's claw tip, the claws at rest: the opaque pixel (alpha > 0) furthest along
    // (-1, 0.25), left and slightly down, the way the claws point. Checked against the shipped sheet
    // by HandCursorArtTests when the install is present.
    private static readonly Vector2 PointRestingTip = new(6, 33);

    /// <summary>The frame pixel drawn on the pointer: the claw or finger tip where the art points
    /// with one, otherwise DK2's hotspot. A deliberate deviation (owner, 2026-10-24): DK2 puts the
    /// hotspot on the pointer, up and left of Point's claw (hand-cursor-rendering.md, "Where the
    /// hotspot lands"). Point takes frame 0's tip, the claw at rest, in every frame, so the hand stays
    /// still while the claws flex as DK2's does (owner, 2026-10-24: per-frame tips moved the hand
    /// about the pointer). The pointing fingers of SmallPoint and the pickup hand reach two pixels left of
    /// their hotspots (faint edge pixels); HoldThing's hotspot already sits on its pinch. The pick (Dig, Tag, Tagging), the slap and
    /// the drops keep the hotspot: their art swings through it, and pinning a tip would stop the
    /// swing; HoldGold has no tip.</summary>
    public static Vector2 Anchor(HandCursorKind kind, int frame)
    {
        HandCursorSprite sprite = Sprite(kind);
        if ((uint)frame >= (uint)sprite.FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        return kind switch
        {
            HandCursorKind.Point => PointRestingTip,
            HandCursorKind.SmallPoint => new(0, 2),
            HandCursorKind.Pickup => new(0, 4),
            _ => sprite.Hotspot,
        };
    }

    /// <summary>How far right of the pointer the cursor can reach over its loop, in 640×480 units:
    /// frame width minus the least anchor X. The hover tooltip sits this far plus 18 to the right,
    /// clear of the hand.</summary>
    public static float Reach(HandCursorKind kind)
    {
        HandCursorSprite sprite = Sprite(kind);
        float least = float.MaxValue;
        for (int frame = 0; frame < sprite.FrameCount; frame++)
            least = Math.Min(least, Anchor(kind, frame).X);
        return sprite.FrameSize.X - least;
    }

    /// <summary>Draws <paramref name="frame"/> with its <see cref="Anchor"/> on the pointer, scaled
    /// uniformly by height/480 (ART_DIRECTION §13). The world pick stays at the pointer.</summary>
    public static HandCursorPlacement Place(HandCursorKind kind, int frame, Vector2 pointerPixels, int screenHeight)
    {
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        HandCursorSprite sprite = Sprite(kind);
        float scale = screenHeight / 480f;
        return new(pointerPixels - Anchor(kind, frame) * scale, sprite.FrameSize * scale);
    }

    /// <summary>DK2's tool-icon offset from the hand's native hotspot, translated with the drawn
    /// hand when its click anchor changes. Uses the hand's uniform height scale.</summary>
    public static Vector2 OverlayAnchor(HandCursorKind kind, Vector2 pointerPixels, int screenHeight,
        bool overHud = false)
    {
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        if (kind == HandCursorKind.Default) kind = HandCursorKind.Point;
        Vector2 relative = Sprite(kind).Hotspot - Anchor(kind, 0) + new Vector2(overHud ? 42 : 82, 10);
        return pointerPixels + relative * (screenHeight / 480f);
    }
}

public sealed class HandCursorFeedback
{
    private HandCursorKind _transient;
    private long _startedAt;
    public void Reset() => _transient = HandCursorKind.Default;
    public void Play(HandCursorKind kind, long milliseconds)
    { _transient = kind; _startedAt = milliseconds; }
    public HandCursorKind Resolve(HandCursorKind hover, bool holding, bool gold, bool primaryHeld, long milliseconds)
    {
        if (_transient != HandCursorKind.Default)
        {
            HandCursorSprite sprite = HandCursor.Sprite(_transient);
            if (Math.Max(0, milliseconds - _startedAt) * sprite.FramesPerSecond < sprite.FrameCount * 1000L)
                return _transient;
            if (_transient == HandCursorKind.Tag && primaryHeld) return HandCursorKind.Tagging;
            Reset();
        }
        return holding ? gold ? HandCursorKind.HoldGold : HandCursorKind.HoldThing : hover;
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
