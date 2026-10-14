// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Foot dust on a Dirt Path (docs/research/creature-movement-dust.md, ADR-0477).</summary>
public class MovementDustTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void ACreatureAsksForFootDustOnTheUpdateAfterItCrossesOntoADirtTile_AndNotWithinATileOrOnClaimedGround()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 2) })
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.Neutral;
        }
        world.Tile(5, 2).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(5, 2).OwnerPlayerId = Keeper;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        goblin.Heading = 0x200;
        var sim = new GameSimulation(db, world, 1, [new MovementDustSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Tick();
        Assert.DoesNotContain(events.Events, e => e.EffectRequest.HasValue);

        // Onto the next dirt tile: the crossing is noticed on the following update, there.
        goblin.PosX = 3 * Fp32.One + 100;
        sim.Tick();
        ViewEffectRequest request = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal(new EffectId(MovementDustSystem.FootDustEffectId), request.EffectId);
        Assert.Equal(Keeper, request.Owner.Value);
        Assert.Equal(new FpPosition3(new Fp32(goblin.PosX), new Fp32(goblin.PosY), new Fp32(Fp32.One)), request.Position);
        Assert.Equal(new Dk2Angle(0x200), request.Heading);

        // Within the tile: nothing. A diagonal crossing: one request.
        goblin.PosX = 3 * Fp32.One + 3000;
        sim.Tick();
        Assert.Equal(1, events.Events.Count(e => e.EffectRequest.HasValue));
        goblin.PosX = 4 * Fp32.One + 100;
        sim.Tick();
        Assert.Equal(2, events.Events.Count(e => e.EffectRequest.HasValue));

        // Onto claimed ground: the terrain test fails, whatever the crossing.
        goblin.PosX = 5 * Fp32.One + 100;
        sim.Tick();
        Assert.Equal(2, events.Events.Count(e => e.EffectRequest.HasValue));
    }
}
