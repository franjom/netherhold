// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The campaign player's Combat Pit rescue end to end on the shipped data (stage 3 plan, Task 4): a beaten
/// fighter sinks, rises on the rim (state 217), and the player drops an Imp there that carries it to its bed.</summary>
public class CampaignPlayerPitRescueTests
{
    private const byte Keeper = 3;
    private const byte ClaimedPath = 8, ImpenetrableRock = 1, Goblin = 7, Imp = 1;
    private static readonly PlayerRunId Run = new("pit-rescue-test");

    [InstallFact]
    public void ABeatenFighterRisesOnTheRimAndThePlayersImpCarriesItToBed()
    {
        GameDatabase db = ScenarioCorpus.Database;
        const int width = 20, height = 16;
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles[y * width + x] = edge ? new MapTile(ImpenetrableRock, 0, 0, 0) : new MapTile(ClaimedPath, Keeper, 0, 0);
            }
        WorldState world = WorldState.FromMap(db, new LevelMap(width, height, tiles), [PlayerIds.Neutral, Keeper]);
        for (int y = 4; y <= 8; y++)
            for (int x = 4; x <= 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
        for (int y = 2; y <= 3; y++)
            for (int x = 14; x <= 15; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), Keeper);

        CreatureState loser = world.SpawnCreature(db, Goblin, Keeper, new TilePos(6, 6));
        loser.PosZ = loser.LastPosZ = -0xC00;
        loser.CombatPitFighter = true;
        Assert.True(LairRules.TryPlaceBedInAnyLair(world, db, loser));
        CreatureDeathRules.Collapse(new StateHookContext(world, db), loser);
        // Far from the Pit: beyond the reach of a dropped Imp's job scan, so only the player's drop brings it.
        world.SpawnCreature(db, Imp, Keeper, new TilePos(17, 13));

        var sim = new GameSimulation(db, world, 42, SimulationSystems.Default());
        var builder = new PlayerObservationBuilder(db, new PlayerId(Keeper));
        var adapter = new PlayerActionAdapter(db, new PlayerId(Keeper));
        var player = new CampaignPlayer(new CampaignPlayerOptions(Run, new PlayerId(Keeper), [new CreatureTypeId(Imp)]) { CombatPitMaxLevel = 8 });
        player.Apply(player.Strategy with { PitFighters = 2, Dig = DigMode.Off, Rooms = [] });

        var drops = new List<TilePos>();
        long revision = 0;
        for (int tick = 0; tick < 240 && loser.Dk2StateId != LairRules.SleepingState; tick++)
        {
            var commands = new List<PlayerCommand>();
            if (tick % 4 == 0)
            {
                PlayerObservationCapture capture = builder.Capture(world,
                    new PlayerStamp(Run, new PlayerId(Keeper), world.CurrentTick, new PlayerRevision(++revision)), []);
                foreach (PlayerAction action in player.Decide(capture.Observation).Actions)
                {
                    if (action is PlayerAction.DropAt drop) drops.Add(drop.Tile);
                    commands.AddRange(adapter.Resolve(capture, action).Commands);
                }
            }
            sim.Tick(new InputFrame(commands));
        }

        TilePos dropped = Assert.Single(drops);
        Assert.False(dropped.X is > 4 and < 8 && dropped.Y is > 4 and < 8, $"the Imp went down on the Pit's floor at {dropped}");
        Assert.Equal(LairRules.SleepingState, loser.Dk2StateId);
        Assert.False(CreatureDeathRules.IsDying(loser));
    }
}
