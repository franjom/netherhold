// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

/// <summary>The head count <c>0x004FAC40</c> skips only a creature off the map (<c>+0x16</c> == 2,
/// <c>0x004FAC7E</c>). Level 5, turn 1869: the Thief collapsed on 1864 still counted, so Imp 454's
/// side fell short and it ran to raise the alarm.</summary>
public class LocalStrengthTests
{
    [Fact]
    public void ACollapsedCreatureCounts_AHeldOneAndACorpseDoNot()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 10), [PlayerIds.Good, PlayerIds.FirstKeeper]);
        var good = new PlayerId(PlayerIds.Good);
        CreatureState standing = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, good, new TilePos(5, 5));
        CreatureState collapsed = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, good, new TilePos(5, 6));
        int one = FearRules.ThreatAtLevel(db, db.CreatureById(standing.CreatureTypeId), standing.Level);
        collapsed.Health = 0;
        var capture = new CaptureContext();
        new GameSimulation(db, world, 1, [capture]).Tick();
        SimulationContext context = capture.Context!;
        Assert.True(CreatureDeathRules.IsDown(collapsed));
        Assert.Equal(2 * one, FearRules.LocalStrength(context, good, standing.PosX, standing.PosY));

        CreatureState held = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, good, new TilePos(6, 5));
        held.InHand = true;
        Assert.Equal(2 * one, FearRules.LocalStrength(context, good, standing.PosX, standing.PosY));

        CreatureState corpse = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, good, new TilePos(6, 6));
        corpse.Health = 0;
        CreatureDeathRules.BecomeCorpse(world, db, corpse);
        Assert.Equal(2 * one, FearRules.LocalStrength(context, good, standing.PosX, standing.PosY));
    }

    private sealed class CaptureContext : ISimulationSystem
    {
        public string Name => "LocalStrengthTest";
        public SimulationContext? Context { get; private set; }
        public void Execute(SimulationContext context) => Context = context;
    }
}
