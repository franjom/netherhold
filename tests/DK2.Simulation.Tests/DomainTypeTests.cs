// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Tests;

public class PlayerIdTests
{
    [Fact]
    public void ABytePrintsAsItsNumber_SoEventTextAndCsvRowsAreUnchanged()
    {
        PlayerId keeper = PlayerIds.FirstKeeper;

        Assert.Equal("3", keeper.ToString());
        Assert.Equal("player=3", $"player={keeper}");
    }

    [Fact]
    public void ABytePassesInImplicitly_AndComesOutOnlyExplicitly()
    {
        PlayerId id = (byte)7;

        Assert.Equal((byte)7, (byte)id);
        Assert.Equal((byte)7, id.Value);
        Assert.True(id.IsKeeper);
        Assert.False(((PlayerId)PlayerIds.Neutral).IsKeeper);
    }

    [Fact]
    public void PlayersOrderByTheirId()
    {
        PlayerId[] players = [5, 3, 4];

        Array.Sort(players);

        Assert.Equal(new PlayerId[] { 3, 4, 5 }, players);
        Assert.True((PlayerId)3 < 4);
        Assert.True((PlayerId)4 >= 4);
    }
}

public class PlayerMaskTests
{
    [Fact]
    public void BitNIsPlayerN_AsTheTileFieldsHaveAlwaysStoredIt()
    {
        PlayerMask mask = PlayerMask.None.With(3).With(0);

        Assert.Equal((ushort)0b1001, mask.Bits);
        Assert.True(mask.Contains(3));
        Assert.True(mask.Contains(0));
        Assert.False(mask.Contains(4));
        Assert.False(mask.IsEmpty);
    }

    [Fact]
    public void WithoutClearsOnlyThatPlayer()
    {
        PlayerMask mask = new PlayerMask(0b1111).Without(2);

        Assert.Equal((ushort)0b1011, mask.Bits);
        Assert.True(PlayerMask.None.With(5).Without(5).IsEmpty);
    }

    [Fact]
    public void AnIdWithoutABitIsNeverInTheMask_AndCannotBeAdded()
    {
        PlayerMask full = new(ushort.MaxValue);

        Assert.True(full.Contains(PlayerMask.MaxPlayerId));
        Assert.False(full.Contains(PlayerMask.MaxPlayerId + 1));
        Assert.Equal(full, full.Without(200));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerMask.None.With(PlayerMask.MaxPlayerId + 1));
    }

    [Fact]
    public void ATileSetsAndClearsItsTagsThroughTheMask()
    {
        var tile = new TileState();

        tile.SetTaggedBy(3, true);
        tile.SetDiscoveredBy(4);

        Assert.True(tile.IsTaggedBy(3));
        Assert.True(tile.IsTaggedByAnyone);
        Assert.True(tile.IsDiscoveredBy(4));
        Assert.False(tile.IsDiscoveredBy(3));

        tile.SetTaggedBy(3, false);

        Assert.False(tile.IsTaggedByAnyone);
        Assert.Throws<ArgumentOutOfRangeException>(() => tile.SetTaggedBy(16, true));
    }
}

public class AvailabilityKindTests
{
    [Theory]
    [InlineData(AvailabilityKind.Room, 1)]
    [InlineData(AvailabilityKind.Creature, 2)]
    [InlineData(AvailabilityKind.Trap, 3)]
    [InlineData(AvailabilityKind.Door, 4)]
    [InlineData(AvailabilityKind.Spell, 5)]
    public void TheKindsKeepTheNumbersTheKwdAndTheHashUse(AvailabilityKind kind, int number) =>
        Assert.Equal(number, (int)kind);

    [Theory]
    [InlineData(ResearchTreeRules.KindRoom, AvailabilityKind.Room)]
    [InlineData(ResearchTreeRules.KindDoor, AvailabilityKind.Door)]
    [InlineData(ResearchTreeRules.KindTrap, AvailabilityKind.Trap)]
    [InlineData(ResearchTreeRules.KindSpell, AvailabilityKind.Spell)]
    [InlineData(0, AvailabilityKind.None)]
    public void AResearchKindMapsOntoItsAvailabilityKind(int researchKind, AvailabilityKind expected) =>
        Assert.Equal(expected, ResearchTreeRules.AvailabilityKindOf(researchKind));
}

public class MoveOptionsTests
{
    [Theory]
    [InlineData(MoveOptions.Plain, 0x0)]
    [InlineData(MoveOptions.Loose, 0x0)]
    [InlineData(MoveOptions.Spot, 0xC)]
    [InlineData(MoveOptions.ExactSpot, 0xD)]
    [InlineData(MoveOptions.CaptiveSpot, 0xD)]
    [InlineData(MoveOptions.Leaving, 0xD)]
    public void ThePathFlagsFollowOptionBitsThreeToSix(MoveOptions options, int expected) =>
        Assert.Equal((byte)expected, options.PathFlags());
}
