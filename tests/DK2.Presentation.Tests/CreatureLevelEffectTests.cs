// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class CreatureLevelEffectTests
{
    [InstallFact]
    public void AnImpPromotionProducesTheFourAuthoredSparklesThenExpires()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db,
            new LevelMap(7, 7, Enumerable.Repeat(new MapTile(3, PlayerIds.FirstKeeper, 0, 0), 49).ToArray()),
            [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var imp = world.SpawnCreature(db, db.CreatureByName("Imp")!.CreatureId, PlayerIds.FirstKeeper, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, 42, [new KeeperSpellSystem()]);
        var sink = new ListEventSink(); sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(new GrantCreatureLevelCommand(PlayerIds.FirstKeeper, imp.Id)));
        var model = new RoomConstructionParticles(db, 42, () => new EffectTime(0));
        foreach (var emission in sink.Events) model.Emit(emission);
        model.AdvanceTo(new EffectTime(51));
        Assert.Equal(3, model.Particles.Count);
        Assert.All(model.Particles, p =>
        {
            Assert.Equal("gsparkle", p.Definition.ArtResource.Name, ignoreCase: true);
            Assert.Equal((p.Definition.Red, p.Definition.Green, p.Definition.Blue), (p.Red, p.Green, p.Blue));
        });
        for (int step = 2; step <= 32; step++) model.AdvanceTo(new EffectTime(step * 50 + 1));
        Assert.Equal(new ushort[] { 554, 555, 556, 557 }, model.Particles.Select(p => p.Definition.EffectElementId).Distinct().Order().ToArray());
        Assert.Contains(model.Particles, p => p.Red < p.Definition.Red);
        for (int step = 33; step <= 100; step++) model.AdvanceTo(new EffectTime(step * 50 + 1));
        Assert.Empty(model.Particles);
    }
}
