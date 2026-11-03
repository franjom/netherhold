// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
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
    /// <c>0x08</c>), or hidden by fog (HYPOTHESIS: the owner saw the dig cursor over "Fog of War",
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

/// <summary>The cursor's transient states, with the two sounds their callbacks request: HAND <c>0xEC</c>
/// when the tag state is entered (<c>0x0040E300</c>) and HAND <c>0xC5</c> once the slap reaches frame 3, at
/// most once until it completes (<c>0x0040E520</c>, latch <c>0x006CE95C</c>). Both are positional at the
/// pointer's world position (interface <c>+0x1078/+0x107C</c>, the pick's clamped hit) at height 2.
/// The state's nonpositional <c>0xEC</c> in a terrain category has no samples in any shipped category.</summary>
[DK2.Core.Dk2Behavior(DK2.Core.Confidence.StronglyInferred, "docs/research/sound-effects.md#6-the-hand-and-its-cursor")]
public sealed class HandCursorFeedback
{
    private const int SlapSoundFrame = 3;
    private static readonly SoundCategory HandCategory = new("hand");
    private HandCursorKind _transient;
    private long _startedAt;
    private bool _slapSounded;

    public SoundRequests Sounds { get; } = new();

    /// <summary>Clears the transient and the slap latch. Deviation: DK2 clears the latch only on a swing's
    /// completion (<c>0x0040E5A1</c>); this also runs on focus loss and disable.</summary>
    public void Reset() { _transient = HandCursorKind.Default; _slapSounded = false; }

    /// <summary>Requests <paramref name="kind"/> as <c>0x0040ABC0</c> does. <paramref name="pointer"/> is
    /// where the hand points, for the tag sound that entering its state requests. The host calls it for a
    /// tag only on a primary press with a Hand-tool drag; DK2's tag and untag also run from tool modes ours
    /// has no way to reach.</summary>
    public void Play(HandCursorKind kind, long milliseconds, FpPosition2? pointer = null)
    {
        // The slap callback adopts a request only past frame 3 and never reads which state is asked for
        // (0x0040E5AF), so a Tag request is dropped too. The drops go through the base channel, whose
        // callback has no such guard, and keep replacing the slap here (ours has one transient).
        if (kind is HandCursorKind.Slap or HandCursorKind.Tag && _transient == HandCursorKind.Slap
            && FrameOf(milliseconds) <= SlapSoundFrame)
            return;
        _transient = kind;
        _startedAt = milliseconds;
        if (kind == HandCursorKind.Tag && pointer is FpPosition2 at) Sounds.Add(HandSound(0xEC, at));
    }

    /// <summary>The per-frame step of the cursor channel (<c>0x0040AAE0</c>): the slap's sound at frame 3,
    /// and its completion, which clears the latch.</summary>
    public void Advance(long milliseconds, FpPosition2? pointer = null)
    {
        if (_transient != HandCursorKind.Slap) return;
        long frame = FrameOf(milliseconds);
        if (!_slapSounded && frame >= SlapSoundFrame && pointer is FpPosition2 at)
        {
            Sounds.Add(HandSound(0xC5, at));
            _slapSounded = true;
        }
        if (frame >= HandCursor.Sprite(HandCursorKind.Slap).FrameCount) { _slapSounded = false; _transient = HandCursorKind.Default; }
    }

    public HandCursorKind Resolve(HandCursorKind hover, bool holding, bool gold, bool primaryHeld, long milliseconds,
        FpPosition2? pointer = null)
    {
        Advance(milliseconds, pointer);
        if (_transient != HandCursorKind.Default)
        {
            HandCursorSprite sprite = HandCursor.Sprite(_transient);
            if (Math.Max(0, milliseconds - _startedAt) * sprite.FramesPerSecond < sprite.FrameCount * 1000L)
                return _transient;
            if (_transient == HandCursorKind.Tag && primaryHeld) return HandCursorKind.Tagging;
            _transient = HandCursorKind.Default;
        }
        return holding ? gold ? HandCursorKind.HoldGold : HandCursorKind.HoldThing : hover;
    }

    private long FrameOf(long milliseconds) =>
        Math.Max(0, milliseconds - _startedAt) * HandCursor.Sprite(HandCursorKind.Slap).FramesPerSecond / 1000;

    private static SoundRequest HandSound(uint id, FpPosition2 at) =>
        new(HandCategory, new SoundId(id), new Vector3(at.X.Raw / 4096f, 2f, -at.Y.Raw / 4096f));
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
