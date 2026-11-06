// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Simulation.Tests;

/// <summary>The presented world's copy (ADR-0693): equal to the live world by the state hash and by
/// every field, independent of it afterwards, and a simulation in its own right.</summary>
public sealed class StateCopyTests
{
    private static ulong WorldHash(WorldState world)
    {
        Hash64 hash = Hash64.Start();
        world.HashInto(ref hash);
        return hash.Value;
    }

    private static void AssertCopied(WorldState live, WorldState copy)
    {
        StateGraph.AssertDeepCopy(live, copy, StateGraph.WorldIgnored, StateGraph.WorldShared);
        Assert.Equal(WorldHash(live), WorldHash(copy));
    }

    [Fact]
    public void CopyFrom_MatchesTheLiveWorld_AtEveryCheckpoint()
    {
        GameSimulation simulation = TestWorld.Simulation();
        WorldState presented = WorldState.CopyOf(simulation.World);
        AssertCopied(simulation.World, presented);
        foreach (int ticks in new[] { 1, 50, 200, 600 })
        {
            simulation.Run(ticks);
            presented.CopyFrom(simulation.World);
            AssertCopied(simulation.World, presented);
            AssertCopied(simulation.World, WorldState.CopyOf(simulation.World));
        }
    }

    [Fact]
    public void Copy_DoesNotMoveWhenTheLiveWorldTicks()
    {
        GameSimulation simulation = TestWorld.Simulation();
        simulation.Run(100);
        WorldState presented = WorldState.CopyOf(simulation.World);
        ulong frozen = WorldHash(presented);

        simulation.Run(300);

        Assert.Equal(frozen, WorldHash(presented));
        Assert.NotEqual(frozen, WorldHash(simulation.World));
        presented.CopyFrom(simulation.World);
        AssertCopied(simulation.World, presented);
    }

    [Fact]
    public void CopiedWorld_TicksLikeTheLiveOne()
    {
        GameSimulation live = TestWorld.Simulation();
        live.Run(100);
        GameSimulation twin = GameSimulation.Resume(live.Database, WorldState.CopyOf(live.World),
            SimRandom.Restore(live.Rng.SaveState()), [new ChaosSystem()]);
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) live.Navigation.Write(writer);
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            twin.Navigation.Read(reader);
        }
        Assert.Equal(live.StateHash(), twin.StateHash());

        for (int t = 0; t < 500; t++)
        {
            live.Tick();
            twin.Tick();
            Assert.True(live.StateHash() == twin.StateHash(), $"the copy diverged {t} ticks after it was taken");
        }
    }

    [Fact]
    public void CopyFrom_KeepsTheInstanceOfAThingThatLives_AndFollowsTheLiveOrder()
    {
        GameDatabase database = TestWorld.Database();
        WorldState live = TestWorld.World(database);
        CreatureState first = live.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, ownerPlayerId: 1, new TilePos(3, 3));
        CreatureState second = live.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, ownerPlayerId: 2, new TilePos(5, 4));
        WorldState presented = WorldState.CopyOf(live);
        CreatureState? kept = presented.CreatureById(second.Id);
        Assert.NotNull(kept);

        live.RemoveCreature(first.Id);
        CreatureState third = live.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, ownerPlayerId: 1, new TilePos(4, 4));
        third.PosX += 100;
        live.ChangeCreatureOwner(second, playerId: 1);
        presented.CopyFrom(live);

        Assert.Same(kept, presented.CreatureById(second.Id));
        Assert.Equal(live.Creatures.Select(c => c.Id), presented.Creatures.Select(c => c.Id));
        Assert.Equal(live.OwnedCreatures(1).Select(c => c.Id), presented.OwnedCreatures(1).Select(c => c.Id));
        Assert.Equal(third.PosX, presented.CreatureById(third.Id)!.PosX);
        Assert.Null(presented.CreatureById(first.Id));
        AssertCopied(live, presented);
    }
}
