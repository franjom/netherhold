// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

/// <summary>The follow-path crowd probe <c>0x004D15E0</c> and the neighbour query <c>0x004D1170</c> walk each
/// tile's thing chain, so they count a living creature by the tile it is linked to (ADR-0781).</summary>
public class CrowdProbeTests
{
    /// <summary>The pick-up unlinks a creature from its tile (<c>0x004B4F60</c>), so one in the hand adds
    /// nothing at the point it was lifted from. Level 5 seed 1: Warlock 432 in the hand on (16,21) at
    /// turns 1882-1883 left Imp 446's seek gain at the full 1.825 (native velocity (12976, -27279)).</summary>
    [Fact]
    public void ACreatureInTheHandAddsNothingWhereItWasLifted()
    {
        (GameDatabase db, WorldState world, CreatureState imp) = OneImp();
        var steering = new WorldSteering(db, world);

        Assert.Equal(4576, steering.CrowdWeight(Centre, FollowPathRules.CrowdSpan));
        imp.InHand = true;
        Assert.False(imp.TileLink.IsLinked);
        Assert.Equal(0, steering.CrowdWeight(Centre, FollowPathRules.CrowdSpan));
    }

    /// <summary>A dead creature's body is a class-6 thing in DK2 (<c>0x004B0F20</c>), which the probe's class-0
    /// test (<c>0x004D1673</c>) passes over.</summary>
    [Fact]
    public void ACorpseAddsNothing()
    {
        (GameDatabase db, WorldState world, CreatureState imp) = OneImp();
        CreatureDeathRules.BecomeCorpse(world, db, imp);

        Assert.Equal(0, new WorldSteering(db, world).CrowdWeight(Centre, FollowPathRules.CrowdSpan));
    }

    /// <summary>The neighbour query <c>0x004D1170</c> walks the same chains (<c>0x004D1245</c>, <c>0x004D1254</c>,
    /// <c>0x004D126A</c>): a creature in the hand or a corpse is no separation neighbour.</summary>
    [Fact]
    public void NeitherAHeldCreatureNorACorpseIsASeparationNeighbour()
    {
        (GameDatabase db, WorldState world, CreatureState imp) = OneImp();
        CreatureState other = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 6));
        var steering = new WorldSteering(db, world);
        var found = new List<SteeringNeighbour>();

        steering.CollectNeighbours(Centre, 0x30000, found);
        Assert.Equal(2, found.Count);

        imp.InHand = true;
        CreatureDeathRules.BecomeCorpse(world, db, other);
        found.Clear();
        steering.CollectNeighbours(Centre, 0x30000, found);
        Assert.Empty(found);
    }

    private static readonly Fixed16Vector Centre = new((5 << 16) + 0x8000, (5 << 16) + 0x8000);

    private static (GameDatabase, WorldState, CreatureState) OneImp()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
            }
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        imp.Body.Radius = 4576;
        return (db, world, imp);
    }
}
