// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Tools;

namespace DK2.Tools.Tests;

public sealed class NativeBirthsTests
{
    // Slot 425 of DK2's Level 4 run, state 5 (dk2runs/level4-live1-seed5/seed05/creature-map.csv):
    // a hero Dwarf, killed, another Dwarf, killed, a Thief, killed, then one of the keeper's Imps.
    private const string Slot425 = """
        tick,id,type,owner,x,y,health,state,target
        1,425,,,,,,,
        3043,425,14,1,15,29,1000,1,0
        3050,425,14,1,16,29,1000,6,0
        3362,425,obj,,,,,,
        3523,425,14,1,35,29,1250,1,0
        3823,425,obj,,,,,,
        3883,425,18,1,15,29,1875,1,0
        4290,425,obj,,,,,,
        5783,425,1,3,26,55,1000,11,0
        5790,425,1,1,26,55,1000,11,0
        """;

    [Fact]
    public void ASlotsBirthsAreItsTypeChangesAfterTheResync_OnTheTurnBeforeTheirHeader()
    {
        NativeBirths births = Read(Slot425, afterTurn: 2400);

        Assert.Equal(
            [new(425, 3042, 14, 1), new(425, 3522, 14, 1), new(425, 3882, 18, 1), new(425, 5782, 1, 3)],
            births.Births);
    }

    [Fact]
    public void ABirthNamesNobodyUntilOneOfOursPairs_ThenNamesIt()
    {
        NativeBirths births = Read(Slot425, afterTurn: 2400);
        var map = new PacketEntityMap();
        births.Reserve(map);

        Assert.True(map.TryResolve(425, 3100, PacketEntityKind.Creature, out int before));
        Assert.Equal(PacketEntityMap.NoCreature, before);

        Assert.True(births.TryPair(map, 3041, 34, 14, 1, out NativeBirths.Birth native));
        Assert.Equal(new NativeBirths.Birth(425, 3042, 14, 1), native);
        Assert.True(map.TryResolve(425, 3100, PacketEntityKind.Creature, out int after));
        Assert.Equal(34, after);
        Assert.True(map.TryResolve(425, 3600, PacketEntityKind.Creature, out int later));
        Assert.Equal(PacketEntityMap.NoCreature, later);
    }

    [Fact]
    public void OursPairsWithTheNearestBirthOfItsTypeAndOwner_WithinTheWindow()
    {
        NativeBirths births = Read(Slot425, afterTurn: 2400);
        var map = new PacketEntityMap();

        Assert.False(births.TryPair(map, 3500, 35, 18, 1, out _));
        Assert.False(births.TryPair(map, 3522 + NativeBirths.Window + 1, 35, 14, 1, out _));
        Assert.True(births.TryPair(map, 3500, 35, 14, 1, out NativeBirths.Birth nearest));
        Assert.Equal(3522, nearest.Turn);
        Assert.False(births.TryPair(map, 3500, 36, 14, 1, out _));
        Assert.Equal(1, births.PairedCount);
    }

    [Fact]
    public void ABirthOnTheResyncTurnCounts_AndItsRowStartsWithTheResyncsPairs()
    {
        // Resync from header 3042: the Dwarfs first seen in header 3043 were born on turn 3042,
        // after the resync's state, and a row keyed there would sit under the resync's pairs.
        NativeBirths births = Read(Slot425, afterTurn: 3042);
        var map = new PacketEntityMap();
        map.Set(425, 3043, 7, PacketEntityKind.Creature);
        births.Reserve(map);

        Assert.Equal(new NativeBirths.Birth(425, 3042, 14, 1), births.Births[0]);
        Assert.True(map.TryResolve(425, 3043, PacketEntityKind.Creature, out int reserved));
        Assert.Equal(PacketEntityMap.NoCreature, reserved);
    }

    [Fact]
    public void NewCreaturesPairEachTurnWithoutAnEventLog_AndThoseAtTheResyncDoNot()
    {
        NativeBirths births = Read(Slot425, afterTurn: 2400);
        var map = new PacketEntityMap();
        var resident = new CreatureState { Id = 5, CreatureTypeId = 14, OwnerPlayerId = 1 };
        births.Begin(map, [resident]);

        births.PairNew(map, 3042, [resident, new CreatureState { Id = 34, CreatureTypeId = 14, OwnerPlayerId = 1 }], events: null);

        Assert.Equal(1, births.PairedCount);
        Assert.True(map.TryResolve(425, 3042, PacketEntityKind.Creature, out int ours));
        Assert.Equal(34, ours);
    }

    [Fact]
    public void AResyncDropsTheMapsLaterCreatureRows_LeavingEarlierRowsAndObjects()
    {
        var map = new PacketEntityMap();
        map.Add(416, 1000, 12, PacketEntityKind.Creature);
        map.Add(416, 3830, 44, PacketEntityKind.Creature);
        map.Add(416, 4190, 47, PacketEntityKind.Creature);
        map.Add(500, 3000, 9, PacketEntityKind.Object);

        map.DropCreatureRowsAfter(2400);

        Assert.True(map.TryResolve(416, 2400, PacketEntityKind.Creature, out int kept));
        Assert.Equal(12, kept);
        Assert.True(map.TryResolve(416, 5000, PacketEntityKind.Creature, out int dropped));
        Assert.Equal(PacketEntityMap.NoCreature, dropped);
        Assert.True(map.TryResolve(500, 3100, PacketEntityKind.Object, out int item));
        Assert.Equal(9, item);
    }

    private static NativeBirths Read(string csv, long afterTurn)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, csv);
            return NativeBirths.Read(path, afterTurn);
        }
        finally { File.Delete(path); }
    }
}
