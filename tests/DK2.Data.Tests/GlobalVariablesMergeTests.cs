// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// The two merge rules in <see cref="GlobalVariables.MergedWith"/>, on synthetic
/// tables so they run without a DK2 install.
///
/// <para>The install-backed counterpart is
/// <c>DK2.Behavior.Tests.LevelVariablesOnRealDataTests</c>, which pins the actual
/// shipped numbers. These pin the <i>rules</i>: plain values replace by id, the
/// four lists replace wholesale when the level provides any, and a level that
/// provides none of a thing leaves it alone.</para>
/// </summary>
public class GlobalVariablesMergeTests
{
    private static GlobalVariables Table(params (VariableTypeId Id, int Value)[] values)
    {
        var vars = new GlobalVariables();
        foreach ((VariableTypeId id, int value) in values)
            vars.Misc[id] = new MiscVariable(id, value, 0, 0);
        return vars;
    }

    [Fact]
    public void APlainValueTheLevelGivesWins()
    {
        GlobalVariables global = Table(
            (VariableTypeId.MinimumImpThreshold, 4),
            (VariableTypeId.MaxGoldPerTreasuryTile, 3000));

        GlobalVariables level = Table((VariableTypeId.MinimumImpThreshold, 0));

        GlobalVariables merged = global.MergedWith(level);

        Assert.Equal(0, merged[VariableTypeId.MinimumImpThreshold]);
        Assert.Equal(3000, merged[VariableTypeId.MaxGoldPerTreasuryTile]);
    }

    [Fact]
    public void AnIdOnlyTheLevelHasIsAdded()
    {
        GlobalVariables merged = Table((VariableTypeId.MinimumImpThreshold, 4))
            .MergedWith(Table((VariableTypeId.GameTicks, 340)));

        Assert.Equal(4, merged[VariableTypeId.MinimumImpThreshold]);
        Assert.Equal(340, merged[VariableTypeId.GameTicks]);
        Assert.Equal(2, merged.Misc.Count);
    }

    [Fact]
    public void MergingWithNothingIsTheGlobalTableUnchanged()
    {
        GlobalVariables global = Table((VariableTypeId.MinimumImpThreshold, 4));
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 1, 3));

        GlobalVariables merged = global.MergedWith(null);

        Assert.Equal(4, merged[VariableTypeId.MinimumImpThreshold]);
        Assert.Single(merged.Availabilities);
    }

    [Fact]
    public void TheMergeNeverMutatesEitherSide()
    {
        GlobalVariables global = Table((VariableTypeId.MinimumImpThreshold, 4));
        global.Sacrifices.Add(new SacrificeEntry(1, 1, 0, 0, 0, 0, 1, 0, 100));
        GlobalVariables level = Table((VariableTypeId.MinimumImpThreshold, 0));
        level.Availabilities.Add(new AvailabilityEntry(1, 3, 1, 1));

        GlobalVariables merged = global.MergedWith(level);
        merged.Misc[VariableTypeId.MinimumImpThreshold] =
            new MiscVariable(VariableTypeId.MinimumImpThreshold, 99, 0, 0);
        merged.Availabilities.Clear();
        merged.Sacrifices.Clear();

        // A database is immutable and a running simulation holds one, so one
        // level's numbers must not be able to reach another level's run.
        Assert.Equal(4, global[VariableTypeId.MinimumImpThreshold]);
        Assert.Single(global.Sacrifices);
        Assert.Equal(0, level[VariableTypeId.MinimumImpThreshold]);
        Assert.Single(level.Availabilities);
    }

    [Fact]
    public void AListTheLevelProvidesReplacesTheGlobalOne_AvailabilityAccumulates()
    {
        var global = new GlobalVariables();
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 1, 3));
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 2, 3));
        global.CreaturePool.Add(new CreaturePoolEntry(1, 99, 0));

        var level = new GlobalVariables();
        level.Availabilities.Add(new AvailabilityEntry(1, 3, 1, 1));
        level.CreaturePool.Add(new CreaturePoolEntry(7, 15, 3));

        GlobalVariables merged = global.MergedWith(level);

        // Availability rows accumulate, as DK2's handler writes them as read (0x0051CA60,
        // ADR-0281): player 3 gets the level's value for type 1 and keeps the global type 2.
        Assert.Equal(3, merged.Availabilities.Count);
        AvailabilityEntry[] table = merged.AvailabilitiesOf(3).ToArray();
        Assert.Equal(2, table.Length);
        Assert.Equal((1, 1), (table[0].TypeId, table[0].Value));
        Assert.Equal((2, 3), (table[1].TypeId, table[1].Value));

        CreaturePoolEntry pooled = Assert.Single(merged.CreaturePool);
        Assert.Equal(7, pooled.CreatureId);
        Assert.Equal(15, pooled.Value);
    }

    [Fact]
    public void AListTheLevelIsSilentAboutIsKept()
    {
        var global = new GlobalVariables();
        global.Sacrifices.Add(new SacrificeEntry(1, 1, 0, 0, 0, 0, 1, 0, 100));
        global.CreatureStats.Add(new CreatureStatEntry(1, 100, 1, FirstPerson: false));
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 1, 3));

        var level = new GlobalVariables();
        level.Availabilities.Add(new AvailabilityEntry(1, 3, 1, 1));

        GlobalVariables merged = global.MergedWith(level);

        Assert.Single(merged.Sacrifices);
        Assert.Single(merged.CreatureStats);
        Assert.Equal(2, merged.Availabilities.Count);
        Assert.Equal(1, Assert.Single(merged.AvailabilitiesOf(3)).Value);
    }

    /// <summary>
    /// <see cref="GameDatabase.ForLevel"/> returns the same instance when there
    /// is nothing to apply — the shape that lets a caller hand it whatever the
    /// loader found without checking.
    /// </summary>
    [Fact]
    public void ForLevelWithNoLevelVariablesIsTheSameDatabase()
    {
        var database = new GameDatabase([], [], [], Table((VariableTypeId.MinimumImpThreshold, 4)));

        Assert.Same(database, database.ForLevel(null));
    }

    /// <summary>
    /// <b>A regression guard with a real defect behind it.</b>
    /// <see cref="GameDatabase.ForLevel"/> rebuilds the database from parts, and
    /// it silently dropped <see cref="GameDatabase.Objects"/> for as long as
    /// objects existed: the constructor gained the parameter and this call did
    /// not. Nothing noticed, because objects were only definitions until a bed
    /// became a thing standing on a tile — and then a campaign scenario threw
    /// for want of object 22 while all 719 tests passed, because tests build
    /// their database directly and none of them called this.
    ///
    /// <para>Only objects and spells are populated here, which is as much as a
    /// test without an install can cheaply build. The complete sweep — every
    /// record collection, all of them non-empty — is
    /// <c>ObjectsOnRealDataTests.ForLevelKeepsEveryRecordCollection</c>, which
    /// has the real files to work with.</para>
    /// </summary>
    [Fact]
    public void ForLevelKeepsTheObjectDefinitions()
    {
        var database = new GameDatabase(
            terrain: [],
            rooms: [],
            creatures: [],
            variables: Table((VariableTypeId.MinimumImpThreshold, 4)),
            keeperSpells: [new KeeperSpellDef
            {
                Name = "Create Imp", ManaCost = 1500, ManaUpkeepPerSecond = 7,
            }],
            objects: [Bed(22), Bed(26)]);

        GameDatabase forLevel = database.ForLevel(
            Table((VariableTypeId.MinimumImpThreshold, 0)));

        Assert.Equal([(byte)22, (byte)26], forLevel.Objects.Keys.OrderBy(k => k));
        Assert.Single(forLevel.KeeperSpells);

        // Not passing by returning the original instance: the merge happened.
        Assert.NotSame(database, forLevel);
        Assert.Equal(0, forLevel[VariableTypeId.MinimumImpThreshold]);
    }

    private static ObjectDef Bed(byte id) => new()
    {
        Name = $"bed {id}",
        ObjectId = id,
        Flags = ObjectFlags.TypeLair,
        Hp = 1,
        ManaValue = 0,
        RoomCapacity = 1,
        StartState = 0,
        PickUpPriority = 0,
        Width = new Fp32(0),
        Height = new Fp32(0),
        Mass = new Fp32(0),
        Speed = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };

    [Fact]
    public void ForLevelLeavesTheOriginalDatabaseAlone()
    {
        var database = new GameDatabase([], [], [], Table((VariableTypeId.MinimumImpThreshold, 4)));

        GameDatabase forLevel = database.ForLevel(Table((VariableTypeId.MinimumImpThreshold, 0)));

        Assert.NotSame(database, forLevel);
        Assert.Equal(4, database[VariableTypeId.MinimumImpThreshold]);
        Assert.Equal(0, forLevel[VariableTypeId.MinimumImpThreshold]);
    }
}
