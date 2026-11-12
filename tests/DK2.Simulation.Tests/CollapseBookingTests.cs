// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Every damage path reaches <c>Thing_ModifyHealth</c> (<c>0x004B2CE0</c>), which collapses a creature
/// synchronously when its health is below 1 (<c>0x004B2FB5</c>) and never clamps at zero, and the creature
/// update's own-land healing skips only the dying bit (<c>0x00488A73</c>) (ADR-0764, ADR-0765).</summary>
public class CollapseBookingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Goblin = Dk2TerrainFixture.GoblinCreatureId;
    private const byte Imp = Dk2TerrainFixture.ImpCreatureId;

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 19; y++)
        for (int x = 1; x < 19; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        return (db, world);
    }

    [Fact]
    public void ASlapThatEmptiesHealthCollapsesTheCreatureInsideTheCommand()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Goblin, Keeper, new TilePos(9, 9));
        StatePlacement.Place(goblin, CreatureStateMachine.Idle);
        goblin.Health = 10;
        var sim = new GameSimulation(db, world, 7, [new HandSystem()]);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));

        // No health pass has run: the old code left it standing at zero until the next second.
        Assert.True(CreatureDeathRules.IsDying(goblin));
        Assert.Equal(CreatureDeathRules.FallingState, goblin.Dk2StateId);
        Assert.Equal(0, goblin.Health);
        Assert.False(goblin.CollapseDue);
    }

    [Fact]
    public void AnImpFelledByLessThanItsHealingIsHealedByItsOwnUpdateAndLives()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState imp = world.SpawnCreature(db, Imp, Keeper, new TilePos(9, 9));
        CreatureDef def = db.CreatureById(Imp);
        imp.Health = 20;
        // 23 of damage: health -3 with the collapse booked, as a blow earlier in the same pass leaves it.
        CreatureHealthRules.Apply(db, def, imp, -23, PlayerIds.Good);
        Assert.Equal(-3, imp.Health);
        Assert.True(imp.CollapseDue);

        var sim = new GameSimulation(db, world, 7,
            [new CreatureUpdateSystem(new CreatureStatusSystem()), new CollapseSystem()]);
        sim.Tick();

        // The Imp does not collapse, so DK2's own-land block (bit 21 clear) heals it before the removal walk.
        Assert.Contains(world.Creatures, c => c.Id == imp.Id);
        Assert.Equal(13, imp.Health);
    }

    [Fact]
    public void AnImpFelledByMoreThanItsHealingDiesAfterTheUpdate()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState imp = world.SpawnCreature(db, Imp, Keeper, new TilePos(9, 9));
        CreatureHealthRules.Apply(db, db.CreatureById(Imp), imp, -(imp.Health + 100), PlayerIds.Good);
        Assert.Equal(-100, imp.Health);

        var sim = new GameSimulation(db, world, 7,
            [new CreatureUpdateSystem(new CreatureStatusSystem()), new CollapseSystem()]);
        sim.Tick();

        Assert.DoesNotContain(world.Creatures, c => c.Id == imp.Id);
    }
}
