// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class LavaMovementOnRealDataTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LavaMovementOnRealDataTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [InstallFact]
    public void Level8StartingSalamandersCanLeaveTheHeartAreaForLava()
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level8";
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation campaign = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, level)!,
            GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!,
            GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, seed: 1);
        for (int t = 0; t < 2000 && !campaign.World.Creatures.Any(c =>
            db.CreatureById(c.CreatureTypeId).Name == "Salamander"); t++) campaign.Tick();
        CreatureState[] salamanders = campaign.World.Creatures
            .Where(c => db.CreatureById(c.CreatureTypeId).Name == "Salamander").ToArray();
        Assert.NotEmpty(salamanders);
        var visited = new HashSet<TilePos>();
        var targets = new HashSet<TilePos>();
        for (int t = 0; t < 600; t++)
        {
            campaign.Tick();
            foreach (CreatureState c in salamanders)
            {
                visited.Add(c.Tile);
                if (c.HasRoute) targets.Add(new TilePos(c.Body.PathTargetPos.X >> 16, c.Body.PathTargetPos.Y >> 16));
            }
        }
        _output.WriteLine($"starting Salamanders={salamanders.Length}; visited={visited.Count}; lava visited={visited.Count(p => db.TerrainById(campaign.World.Tile(p).TerrainId).IsLava)}; targets={targets.Count}; lava targets={targets.Count(p => db.TerrainById(campaign.World.Tile(p).TerrainId).IsLava)}");
        Assert.Contains(visited, p => db.TerrainById(campaign.World.Tile(p).TerrainId).IsLava);
        CreatureState creature = salamanders.First(c => c.Health > 0);
        WorldState world = campaign.World;
        var navigation = new WorldSteering(db, world);
        TilePos lava = Enumerable.Range(0, world.Width * world.Height)
            .Select(i => new TilePos(i % world.Width, i / world.Width))
            .Where(p => p != creature.Tile && db.TerrainById(world.Tile(p).TerrainId).IsLava)
            .OrderBy(p => Math.Abs(p.X - creature.Tile.X) + Math.Abs(p.Y - creature.Tile.Y))
            .First(p => navigation.TryRoute(creature, p, MoveOptions.ExactSpot));
        _output.WriteLine($"from={creature.Tile}; lava destination={lava}; creature owner={creature.OwnerPlayerId}; lava owner={world.Tile(lava).OwnerPlayerId}");
        var movement = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        for (int t = 0; t < 300 && creature.Tile != lava; t++) movement.Tick();
        Assert.Equal(lava, creature.Tile);
    }

    [InstallTheory]
    [InlineData("Salamander")]
    [InlineData("Giant")]
    public void Level8LavaWalkersEnterLavaAndReachTheirDestination(string name)
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        WorldState world = WorldState.FromMap(db,
            GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!, [0, 1, 2, 3, 4]);
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        TilePos start = Enumerable.Range(0, world.Width * world.Height)
            .Select(i => new TilePos(i % world.Width, i / world.Width))
            .First(p => navigation.NavByte(p.X, p.Y) == DK2.Pathfinding.NavBytes.Floor
                && navigation.NavByte(p.X + 1, p.Y) == DK2.Pathfinding.NavBytes.Lava
                && navigation.NavByte(p.X + 2, p.Y) == DK2.Pathfinding.NavBytes.Lava);
        TilePos destination = new(start.X + 2, start.Y);
        CreatureDef definition = db.CreatureByName(name)!;
        Assert.True(definition.Flags.HasFlag(CreatureFlags.CanWalkOnLava));
        CreatureState creature = world.SpawnCreature(db, definition.CreatureId, new PlayerId(3), start);
        Assert.True(navigation.TryRoute(creature, destination, MoveOptions.ExactSpot));
        var simulation = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        bool enteredLava = false;
        for (int t = 0; t < 300 && creature.Tile != destination; t++)
        {
            simulation.Tick();
            enteredLava |= db.TerrainById(world.Tile(creature.Tile).TerrainId).IsLava;
        }
        Assert.True(enteredLava);
        Assert.Equal(destination, creature.Tile);
    }
}
