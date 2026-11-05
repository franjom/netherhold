// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Level 6a's Workshop (36..38, 25..28) is the player's own room behind two doors, (35,26) from
/// the Training Room and (37,24) from the Hatchery. The level script un-explores it after the
/// opening fly-over (action 17 on point 26, then explores point 27 over one Workshop column), so only
/// creature sight brings it back (fog-reveal-sources.md section 6). DK2's sight stops at a door only
/// while the navigation byte carries a door owner, which a closed, completed door writes and an
/// open one does not (0x004511A0, 0x004528E0): a creature eating at the Hatchery's edge sees
/// through the door that its own presence opens.
/// </summary>
public class Level6aWorkshopSightTests
{
    private const byte Keeper = 3;

    [InstallFact]
    public void ACreatureAtTheHatcheryDoorSeesTheWorkshopThroughTheDoorItOpens()
    {
        (GameSimulation simulation, WorldState world) = Level6aAfterTheFlyOver(lockHatcheryDoor: false);

        simulation.Tick();

        Assert.True(world.DoorAt(37, 24)!.Open);
        for (int y = 25; y <= 27; y++) Assert.True(world.Tile(37, y).IsExploredBy(Keeper), $"(37,{y})");
    }

    [InstallFact]
    public void ALockedDoorStaysShutAndTheWorkshopStaysHidden()
    {
        (GameSimulation simulation, WorldState world) = Level6aAfterTheFlyOver(lockHatcheryDoor: true);

        simulation.Tick();

        Assert.False(world.DoorAt(37, 24)!.Open);
        for (int y = 25; y <= 28; y++) Assert.False(world.Tile(37, y).IsExploredBy(Keeper), $"(37,{y})");
    }

    /// <summary>Sight marks the blocking tile itself (0x0044E7F0 tests the slots before the cell closes
    /// them), so a Training Room creature explores the closed door's own tile, and the door is drawn:
    /// fog gates structures on the explored bit and the built door carries tint 0x80 (0x004A6B70), so
    /// it stays visible where no point light reaches it.</summary>
    [InstallFact]
    public void ACreatureInTheTrainingRoomExploresTheClosedDoorTileAndTheDoorIsDrawnLit()
    {
        (GameSimulation simulation, WorldState world) = Level6aAfterTheFlyOver(lockHatcheryDoor: false);
        world.Tile(35, 26).ClearExploredBy(Keeper);
        world.SpawnCreature(simulation.Database, 1, Keeper, new TilePos(33, 26));

        simulation.Tick();

        Assert.False(world.DoorAt(35, 26)!.Open);
        Assert.True(world.Tile(35, 26).IsExploredBy(Keeper));
        var view = new WorldView(simulation.Database, new PlayerId(Keeper));
        StructureView door = view.Structures(world).Single(s => s.Tile == new TilePos(35, 26));
        Assert.Equal(WorldView.BuiltDoorTint, door.InstanceTint);
    }

    /// <summary>The set arm 0x00454B80 explores a 5x5 rotated room (the Heart) whole; the clear arm
    /// 0x00454E00 handles only constructions 3 and 4, so un-exploring one Heart tile clears that tile.</summary>
    [InstallFact]
    public void UnexploringOneHeartTileClearsOnlyThatTile()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!;
        WorldState world = WorldState.FromMap(db, map, [Keeper, 1, 2], fogOfWarEnabled: true);
        Assert.True(world.Tile(32, 21).IsExploredBy(Keeper));

        DiscoveryRules.SetExplored(world, db, new TilePos(32, 21), Keeper, explored: false);

        Assert.False(world.Tile(32, 21).IsExploredBy(Keeper));
        Assert.True(world.Tile(31, 21).IsExploredBy(Keeper));
        Assert.True(world.Tile(30, 19).IsExploredBy(Keeper));
        world.Tile(30, 19).ClearExploredBy(Keeper);
        world.Tile(34, 23).ClearExploredBy(Keeper);
        DiscoveryRules.SetExplored(world, db, new TilePos(30, 19), Keeper);
        Assert.True(world.Tile(34, 23).IsExploredBy(Keeper));
    }

    /// <summary>The level as placed, the Workshop un-explored as the script leaves it, and one
    /// Keeper creature on the Hatchery tile north of the door. Only the door and sight systems run, so
    /// the script's own reveals cannot hide the effect.</summary>
    private static (GameSimulation, WorldState) Level6aAfterTheFlyOver(bool lockHatcheryDoor)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level6a")!;
        WorldState world = WorldState.FromMap(db, map, players.Select(p => p.PlayerId), fogOfWarEnabled: true);
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "Level6a")!);
        for (int y = 25; y <= 28; y++)
            for (int x = 36; x <= 38; x++) world.Tile(x, y).ClearExploredBy(Keeper);
        world.SpawnCreature(db, 1, Keeper, new TilePos(37, 23));
        if (lockHatcheryDoor) world.DoorAt(37, 24)!.LockMode = 3;
        return (new GameSimulation(db, world, 1, [new DoorSystem(), new FogOfWarSystem()]), world);
    }
}
