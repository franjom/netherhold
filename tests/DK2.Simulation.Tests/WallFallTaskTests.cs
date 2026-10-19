// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A wall falls under workers still on their way (ADR-0516). DK2's walk (state 6,
/// <c>0x00470C80</c>) never looks at its task, and the dig entry (state 13, <c>0x004654F0</c>) tests
/// only that the task entry is alive, which outlives the wall: so the turner goes on to 14 and raises
/// the fresh claim. Level 4's capture shows it at (33,55), turn 1225: the claim raised the turn the
/// wall fell, a walker still walking five turns later.</summary>
public sealed class WallFallTaskTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Face = new(4, 2);

    private static (GameSimulation Sim, ListEventSink Events, CreatureState Imp) Start(int state, TilePos? at = null)
    {
        GameDatabase db = IdleActionClockTests.WithImpIdleArt(Dk2TerrainFixture.Database());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        foreach (TilePos own in new TilePos[] { new(3, 2), new(2, 2), new(3, 3), new(3, 1) })
        {
            world.Tile(own).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(own).OwnerPlayerId = Keeper;
        }
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper,
            at ?? (state == 6 ? new TilePos(2, 2) : new TilePos(3, 2)));
        imp.TargetWork = TileWorkKind.Dig;
        imp.TargetTileIndex = Face.Y * world.Width + Face.X;
        imp.Dk2StateId = state;
        imp.StateEnteredTick = 0;
        if (state == 6)
        {
            var target = new Fixed16Vector(CreatureState.CentreOf(3) << 4, CreatureState.CentreOf(2) << 4);
            RouteWindow.Load(imp.Body, [target], target, 0);
            CreatureStateMachine.BeginTravel(new StateHookContext(world, db), imp, 6, ImpStates.For(TileWorkKind.Dig).Entry);
        }
        // The wall has just fallen; its dig task's entry outlives it.
        ref TileState tile = ref world.Tile(Face);
        tile.TerrainId = Dk2TerrainFixture.DirtPath;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.DigTaskOutlivesWallFor = tile.DigTaskOutlivesWallFor.With(Keeper);
        var sim = new GameSimulation(db, world, 3, [new ImpTaskSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events, imp);
    }

    [Fact]
    public void TheTurnerGoesOnToFourteenAndRaisesTheFreshClaim()
    {
        (GameSimulation sim, ListEventSink events, CreatureState imp) = Start(13);
        sim.Tick();
        Assert.Equal(14, imp.Dk2StateId);
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_abandoned");
        sim.Tick();
        Assert.Single(events.Events, e => e.Kind == "claim_raised");
        Assert.True(sim.World.Tile(Face).FreshlyDug);
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
    }

    // 0x004654F0 tests each axis within one tile, so a diagonal neighbour also goes on to 14.
    [Fact]
    public void ADiagonalTurnerAlsoGoesOnToFourteen()
    {
        (GameSimulation sim, ListEventSink events, CreatureState imp) = Start(13, new TilePos(3, 3));
        sim.Tick();
        Assert.Equal(14, imp.Dk2StateId);
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_abandoned");
    }

    [Fact]
    public void TheWalkerKeepsWalkingTheTurnTheWallFalls()
    {
        (GameSimulation sim, ListEventSink events, CreatureState imp) = Start(6);
        sim.Tick();
        Assert.Equal(6, imp.Dk2StateId);
        Assert.DoesNotContain(events.Events, e => e.Kind == "job_abandoned");
    }

    [Fact]
    public void ATurnerWhoseTaskWasAlreadyRemovedDropsToTenWithoutRaising()
    {
        (GameSimulation sim, ListEventSink events, CreatureState imp) = Start(13);
        sim.World.Tile(Face).DigTaskOutlivesWallFor = sim.World.Tile(Face).DigTaskOutlivesWallFor.Without(Keeper);
        sim.Tick();
        Assert.Single(events.Events, e => e.Kind == "job_abandoned");
        Assert.DoesNotContain(events.Events, e => e.Kind == "claim_raised");
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
    }
}
