// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Door and trap offers and their placement (campaign bot stage 3 plan, Task 4) on the shipped
/// definitions: the Workshop panel's slots with price and the Workshop greying, and a placement resolved
/// to the command the click sends (DungeonRunner.Hud).</summary>
public class PlayerWorkshopTests
{
    private static readonly PlayerId Keeper = new(3);

    private sealed record Setup(GameDatabase Database, WorldState World, TilePos Floor, byte Door, byte Trap);

    /// <summary>Level 1's start with one door and one trap available; with <paramref name="workshop"/> one of
    /// the keeper's floor tiles is made a Workshop tile.</summary>
    private static Setup Level1(bool workshop)
    {
        GameDatabase database = ScenarioCorpus.Database;
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "level1")!;
        WorldState world = WorldState.FromMap(database, map, map.Tiles.Select(t => t.PlayerId).Append(Keeper.Value).Distinct());
        byte door = database.Doors.Values.OrderBy(d => d.DoorId).First().DoorId;
        byte trap = database.Traps.Values.Where(BottomPanelView.IsOnPanel).OrderBy(t => t.TrapId).First().TrapId;
        world.SetAvailability(Keeper.Value, AvailabilityKind.Door, door, 3);
        world.SetAvailability(Keeper.Value, AvailabilityKind.Trap, trap, 3);
        List<TilePos> floors = [.. Enumerable.Range(0, map.Width * map.Height).Select(i => new TilePos(i % map.Width, i / map.Width))
            .Where(t => world.Tile(t).OwnerPlayerId == Keeper.Value && database.RoomByTerrain(world.Tile(t).TerrainId) is null
                && (database.TerrainById(world.Tile(t).TerrainId).Flags & TerrainFlags.Solid) == 0)];
        if (workshop) world.Tile(floors[1]).TerrainId = database.Room(RoomTypeId.Workshop).TerrainId;
        return new Setup(database, world, floors[0], door, trap);
    }

    private static PlayerObservationCapture Capture(Setup s) =>
        new PlayerObservationBuilder(s.Database, Keeper).Capture(s.World,
            new PlayerStamp(new PlayerRunId("workshop-test"), Keeper, s.World.CurrentTick, new PlayerRevision(1)), []);

    [InstallFact]
    public void TheWorkshopPanelOffersDoorsAndTrapsGreyedWithoutAWorkshop()
    {
        Setup without = Level1(workshop: false);
        PlayerOffers greyed = Capture(without).Observation.Offers;
        PlayerWorkshopOffer door = Assert.Single(greyed.Workshop, o => o.Kind == PlayerWorkshopKind.Door && o.Id == without.Door);
        Assert.True(door.NeedsWorkshop);
        Assert.Equal(new GoldAmount(without.Database.Doors[without.Door].GoldCost), door.Cost);
        Assert.Equal(without.Database.Doors[without.Door].Name, door.Name);
        Assert.Contains(greyed.Workshop, o => o.Kind == PlayerWorkshopKind.Trap && o.Id == without.Trap && o.NeedsWorkshop);
        Assert.Equal(PlayerActionKinds.None, greyed.Actions & (PlayerActionKinds.PlaceDoor | PlayerActionKinds.PlaceTrap));

        PlayerOffers open = Capture(Level1(workshop: true)).Observation.Offers;
        Assert.All(open.Workshop, o => Assert.False(o.NeedsWorkshop));
        Assert.Equal(PlayerActionKinds.PlaceDoor | PlayerActionKinds.PlaceTrap, open.Actions & (PlayerActionKinds.PlaceDoor | PlayerActionKinds.PlaceTrap));
        // Affordable is the panel's own gold test (BottomPanelView), against the Heart's starting gold here.
        Assert.Equal(GoldStore.Total(without.World, Keeper) >= door.Cost.Value, door.Affordable);
    }

    [InstallFact]
    public void TheTileOfAnOwnTrapBlueprintShowsIt()
    {
        Setup s = Level1(workshop: true);
        GoldStore.Deposit(s.World, s.Database, Keeper, 10_000);
        Assert.False(Capture(s).Observation.Tiles[s.Floor].Flags.HasFlag(PlayerTileFlags.OwnTrap));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(s.World, s.Database, s.Floor, Keeper, s.Trap));
        Assert.True(Capture(s).Observation.Tiles[s.Floor].Flags.HasFlag(PlayerTileFlags.OwnTrap));
        // Another keeper sees nothing there.
        PlayerId other = s.World.PlayerAtIndex(0).PlayerId == Keeper ? s.World.PlayerAtIndex(1).PlayerId : s.World.PlayerAtIndex(0).PlayerId;
        PlayerObservation theirs = new PlayerObservationBuilder(s.Database, other).Capture(s.World,
            new PlayerStamp(new PlayerRunId("workshop-test"), other, s.World.CurrentTick, new PlayerRevision(1)), []).Observation;
        Assert.False(theirs.Tiles[s.Floor].Flags.HasFlag(PlayerTileFlags.OwnTrap));
    }

    [InstallFact]
    public void TheHarnessGivesThePlayerEveryCreaturesFightingStyle()
    {
        var spec = new DK2.Harness.CampaignPlayerSpec(Keeper);
        var player = (DK2.AI.Players.CampaignPlayer)DK2.Harness.PlayerRunCommand.PlayerRunFor(
            ScenarioCorpus.Database, spec, new PlayerRunId("styles"), null).Controller;
        foreach (CreatureDef creature in ScenarioCorpus.Database.Creatures.Values)
            Assert.Equal(creature.FightStyle, player.Options.StyleOf(new CreatureTypeId(creature.CreatureId)));
        Assert.Contains(ScenarioCorpus.Database.Creatures.Values, c => c.FightStyle == FightStyle.Flanker);
        Assert.Contains(ScenarioCorpus.Database.Creatures.Values, c => c.FightStyle == FightStyle.Blocker);
    }

    [InstallFact]
    public void AnUnknownDoorAndAnOffPanelTrapAreNotOffered()
    {
        Setup s = Level1(workshop: true);
        s.World.SetAvailability(Keeper.Value, AvailabilityKind.Door, s.Door, 2);                       // researchable: an unknown slot
        TrapDef? hidden = s.Database.Traps.Values.FirstOrDefault(t => !BottomPanelView.IsOnPanel(t));   // definition flag 0x2000
        if (hidden is not null) s.World.SetAvailability(Keeper.Value, AvailabilityKind.Trap, hidden.TrapId, 3);
        IReadOnlyList<PlayerWorkshopOffer> offers = Capture(s).Observation.Offers.Workshop;
        Assert.DoesNotContain(offers, o => o.Kind == PlayerWorkshopKind.Door && o.Id == s.Door);
        if (hidden is not null) Assert.DoesNotContain(offers, o => o.Kind == PlayerWorkshopKind.Trap && o.Id == hidden.TrapId);
        Assert.Contains(offers, o => o.Kind == PlayerWorkshopKind.Trap && o.Id == s.Trap);
    }

    [InstallFact]
    public void APlacementResolvesToTheClicksCommandAndNeedsAWorkshop()
    {
        Setup s = Level1(workshop: true);
        var adapter = new PlayerActionAdapter(s.Database, Keeper);
        PlayerObservationCapture capture = Capture(s);
        Assert.Equal(new PlaceDoorCommand(Keeper, s.Floor, s.Door),
            Assert.Single(adapter.Resolve(capture, new PlayerAction.PlaceDoor(s.Door, s.Floor)).Commands));
        Assert.Equal(new PlaceTrapCommand(Keeper, s.Floor, s.Trap),
            Assert.Single(adapter.Resolve(capture, new PlayerAction.PlaceTrap(s.Trap, s.Floor)).Commands));

        Assert.Equal(PlayerRefusalReason.NotOffered, adapter.Resolve(capture, new PlayerAction.PlaceDoor(250, s.Floor)).Refusal);
        Assert.Equal(PlayerRefusalReason.NotOffered, adapter.Resolve(capture, new PlayerAction.PlaceTrap(250, s.Floor)).Refusal);
        Assert.Equal(PlayerRefusalReason.OutOfBounds, adapter.Resolve(capture, new PlayerAction.PlaceDoor(s.Door, new TilePos(-1, 0))).Refusal);

        Setup without = Level1(workshop: false);
        Assert.Equal(PlayerRefusalReason.NotOffered,
            new PlayerActionAdapter(without.Database, Keeper).Resolve(Capture(without), new PlayerAction.PlaceDoor(without.Door, without.Floor)).Refusal);
    }
}
