// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>FLASH_BUTTON on the bottom panel (docs/research/tab-alerts.md): which tab or slot flashes
/// (0x00412260, 0x00412FC0), the half-second dark phase, clearing on use (0x00426F30), and the
/// sparkles' 16-frame cycle (0x00419DF0).</summary>
public sealed class BottomPanelFlashTests
{
    private static HudFlashTarget Target(byte category, byte button) => new((ushort)(category | button << 8));
    private static BottomPanelOpenSet Open(BottomPanelCategory category) => BottomPanelOpenSet.Of(category);

    [Fact]
    public void ATabTargetFlashesItsTabEvenOpenAndAnItemItsSlotOnlyWhileItsPanelShows()
    {
        Assert.Equal(new BottomPanelFlashState(BottomPanelCategory.Rooms, null, false),
            BottomPanelFlash.Resolve(Target(0, 0xFB), Open(BottomPanelCategory.Rooms), 0));
        Assert.Equal(BottomPanelCategory.Creatures, BottomPanelFlash.Resolve(Target(0, 0xF8), BottomPanelOpenSet.None, 0).Tab);
        Assert.Equal(BottomPanelFlashState.None, BottomPanelFlash.Resolve(HudFlashTarget.Gold, BottomPanelOpenSet.None, 0));

        // Level 1's Lair (room 2): the Rooms tab while that panel is closed, the slot once it shows.
        HudFlashTarget lair = Target(1, 2);
        Assert.Equal(new BottomPanelFlashState(BottomPanelCategory.Rooms, null, false),
            BottomPanelFlash.Resolve(lair, Open(BottomPanelCategory.Spells), 0));
        Assert.Equal(new BottomPanelFlashState(null, new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, 2), false),
            BottomPanelFlash.Resolve(lair, Open(BottomPanelCategory.Rooms), 0));
        Assert.Equal(BottomPanelCategory.Workshop, BottomPanelFlash.Resolve(Target(4, 3), BottomPanelOpenSet.None, 0).Tab);
        Assert.Equal(new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, 1),
            BottomPanelFlash.Resolve(Target(3, 1), Open(BottomPanelCategory.Workshop), 0).Slot);

        // A creature target flashes the Creatures tab only while that panel is closed.
        Assert.Equal(BottomPanelCategory.Creatures, BottomPanelFlash.Resolve(Target(2, 5), Open(BottomPanelCategory.Rooms), 0).Tab);
        Assert.Equal(BottomPanelFlashState.None, BottomPanelFlash.Resolve(Target(2, 5), Open(BottomPanelCategory.Creatures), 0));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    public void TheDarkHalfIsTheOddHalfSecondOfTheRealClock(long milliseconds, bool dark) =>
        Assert.Equal(dark, BottomPanelFlash.Resolve(Target(0, 0xFB), BottomPanelOpenSet.None, milliseconds).Dark);

    [Fact]
    public void UsingTheFlashedItemClearsIt_RoomIdAssumedTheRoomType()
    {
        Assert.True(BottomPanelFlash.ClearedByUse(Target(1, 2), PlayerToolMode.BuildRoom, (RoomTypeId)2, null, BottomPanelOpenSet.None));
        Assert.False(BottomPanelFlash.ClearedByUse(Target(1, 2), PlayerToolMode.BuildRoom, (RoomTypeId)4, null, BottomPanelOpenSet.None));
        Assert.False(BottomPanelFlash.ClearedByUse(Target(1, 2), PlayerToolMode.Hand, (RoomTypeId)2, null, BottomPanelOpenSet.None));
        var trap = new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, 3);
        Assert.True(BottomPanelFlash.ClearedByUse(Target(4, 3), PlayerToolMode.Hand, 0, trap, BottomPanelOpenSet.None));
        Assert.True(BottomPanelFlash.ClearedByUse(Target(0, 0xFF), PlayerToolMode.SellRoom, 0, null, BottomPanelOpenSet.None));
        Assert.True(BottomPanelFlash.ClearedByUse(Target(0, 0xF8), PlayerToolMode.Hand, 0, null, Open(BottomPanelCategory.Creatures)));
        // The Rooms, Spells and Workshop tab targets have no clearer.
        Assert.False(BottomPanelFlash.ClearedByUse(Target(0, 0xFB), PlayerToolMode.Hand, 0, null, Open(BottomPanelCategory.Rooms)));
    }

    [Fact]
    public void TheSparklesStartStaggeredFromTheResetDraws()
    {
        // 0x004033F0: per sparkle, frame Random(0xE), X Random(0xAC1), Y Random(0x9E9), byte-truncated.
        var expected = new Dk2Lcg(7);
        var sparkles = new BottomPanelSparkles(seed: 7);
        for (int i = 0; i < BottomPanelSparkles.Count; i++)
        {
            Assert.Equal(expected.NextInt(0xE), sparkles.Frame(i));
            Assert.Equal((byte)expected.NextInt(0xAC1), sparkles.X(i));
            Assert.Equal((byte)expected.NextInt(0x9E9), sparkles.Y(i));
        }
    }

    [Fact]
    public void EachSparkleRunsSixteenFramesThenMovesWithinTheIndexRange()
    {
        var sparkles = new BottomPanelSparkles(seed: 7);
        int start = sparkles.Frame(0);
        for (int step = 1; step < BottomPanelSparkles.Frames - start; step++)
        {
            sparkles.Advance();
            Assert.Equal(start + step, sparkles.Frame(0));
        }
        sparkles.Advance();
        Assert.Equal(0, sparkles.Frame(0));
        Assert.InRange(sparkles.X(0), 0, 255);
    }

    [Fact]
    public void WithAPinnedPanelBesideTheCurrentOneBothCountAsOpen()
    {
        // Rooms current, Spells pinned (0x00416C20): a spell flashes its slot, the Workshop tab still flashes.
        BottomPanelOpenSet both = Open(BottomPanelCategory.Rooms).With(BottomPanelCategory.Spells);
        Assert.Equal(new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, 3),
            BottomPanelFlash.Resolve(Target(5, 3), both, 0).Slot);
        Assert.Equal(new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, 2),
            BottomPanelFlash.Resolve(Target(1, 2), both, 0).Slot);
        Assert.Equal(BottomPanelCategory.Workshop, BottomPanelFlash.Resolve(Target(3, 1), both, 0).Tab);
        Assert.True(BottomPanelFlash.ClearedByUse(Target(0, 0xF8), PlayerToolMode.Hand, 0, null,
            Open(BottomPanelCategory.Rooms).With(BottomPanelCategory.Creatures)));
    }

    [Theory]
    [InlineData(6, 88)]
    [InlineData(5, 64)]
    [InlineData(1, 64)]
    [InlineData(4, 64)]
    public void SparklesAre88UnitsOnALargeSlotAnd64Elsewhere(int widgetBank, int side) =>
        Assert.Equal(side, BottomPanelSparkles.SizeFor(widgetBank));
}
