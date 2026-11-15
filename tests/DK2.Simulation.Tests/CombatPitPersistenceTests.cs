// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CombatPitPersistenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryNewPitRuntimeFieldIsHashedSavedAndCopied(int field)
    {
        GameSimulation sim = TestWorld.Simulation();
        ulong before = sim.StateHash();
        switch (field)
        {
            case 0: sim.World.TileAtIndex(0).PitAmbientShortTicks = 17; break;
            case 1: sim.World.TileAtIndex(0).PitAmbientLongTicks = 29; break;
            case 2: sim.World.TileAtIndex(0).PitPreviousStandingCount = 3; break;
            default: sim.World.Creatures[0].CombatPitAction = CombatPitActionId.AtLevelCap; break;
        }
        Assert.NotEqual(before, sim.StateHash());
        WorldState copy = WorldState.CopyOf(sim.World);
        GameSimulation saved = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);
        Assert.Equal(sim.StateHash(), saved.StateHash());
        Assert.Equal(sim.World.TileAtIndex(0).PitAmbientShortTicks, copy.TileAtIndex(0).PitAmbientShortTicks);
        Assert.Equal(sim.World.TileAtIndex(0).PitAmbientLongTicks, copy.TileAtIndex(0).PitAmbientLongTicks);
        Assert.Equal(sim.World.TileAtIndex(0).PitPreviousStandingCount, copy.TileAtIndex(0).PitPreviousStandingCount);
        Assert.Equal(sim.World.Creatures[0].CombatPitAction, copy.Creatures[0].CombatPitAction);
    }
}
