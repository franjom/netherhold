// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class TerrainDestructionEffectTests
{
    [Theory]
    [InlineData(6, false, 1)]
    [InlineData(0, false, 0)]
    [InlineData(6, true, 0)]
    public void OnlyCompletedTerrainChangesWithAnEffectRequestDebris(ushort effectId, bool sameTerrain, int expected)
    {
        GameDatabase original = Dk2TerrainFixture.Database();
        TerrainDef rock = original.TerrainById(Dk2TerrainFixture.Rock) with
        {
            StartingHealth = 32000, MaxHealth = 32000, DestroyedEffectId = effectId,
            DestroyedTypeTerrainId = sameTerrain ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath,
        };
        var db = new GameDatabase(original.Terrain.Values.Select(t => t.TerrainId == rock.TerrainId ? rock : t),
            original.Rooms.Values, original.Creatures.Values, original.Variables);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        TilePos at = new(2, 2);
        world.Tile(at).TerrainId = rock.TerrainId;
        world.Tile(at).OwnerPlayerId = PlayerIds.Neutral;
        world.Tile(at).Health = rock.StartingHealth;
        var sim = new GameSimulation(db, world, 1, [new TileWorkSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        world.QueueWork(at.X, at.Y, TileWorkKind.Dig, PlayerIds.FirstKeeper);
        sim.Tick();
        Assert.InRange(world.Tile(at).Health, 1, rock.StartingHealth - 1);
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);

        for (int i = 0; i < 1000 && !events.Events.Any(e => e.Kind == "destroyed"); i++)
        {
            world.QueueWork(at.X, at.Y, TileWorkKind.Dig, PlayerIds.FirstKeeper);
            sim.Tick();
        }
        Assert.Contains(events.Events, e => e.Kind == "destroyed");
        Assert.Equal(expected, events.Events.Count(e => e.EffectRequest.HasValue));
        if (expected != 0)
        {
            var request = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
            Assert.Equal(new EffectId(effectId), request.EffectId);
            Assert.Equal(at, request.Tile);
            Assert.Equal(Fp32.One, request.Height.Raw);
        }
    }
}
