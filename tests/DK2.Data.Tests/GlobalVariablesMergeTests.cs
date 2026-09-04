// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
    public void AListTheLevelProvidesReplacesTheGlobalOne()
    {
        var global = new GlobalVariables();
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 1, 3));
        global.Availabilities.Add(new AvailabilityEntry(1, 0, 2, 3));
        global.CreaturePool.Add(new CreaturePoolEntry(1, 99, 0));

        var level = new GlobalVariables();
        level.Availabilities.Add(new AvailabilityEntry(1, 3, 1, 1));
        level.CreaturePool.Add(new CreaturePoolEntry(7, 15, 3));

        GlobalVariables merged = global.MergedWith(level);

        AvailabilityEntry availability = Assert.Single(merged.Availabilities);
        Assert.Equal(3, availability.PlayerId);
        Assert.Equal(1, availability.Value);

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
        Assert.Single(merged.Availabilities);
        Assert.Equal(3, merged.Availabilities[0].PlayerId);
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
