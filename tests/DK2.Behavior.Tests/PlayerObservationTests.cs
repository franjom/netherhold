// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Behavior.Tests;

/// <summary>
/// The player observation shows one player what that player may know, and nothing else (campaign
/// player plan, task 2). Paired worlds differ only behind player 3's fog; their observations must be
/// identical, while the same differences in view must show. The database extends
/// <see cref="ScenarioTests.SyntheticDatabase"/> (an Imp, a Treasury, Create Imp) with fog terrains, a
/// slappable creature and two objects, so it runs without an install. Everything added is invented.
/// </summary>
public class PlayerObservationTests
{
    private static readonly PlayerId Viewer = new(3), Rival = new(4);
    private const int Width = 8, Height = 3, Seen = 4; // columns 0-3 are explored by the viewer
    private const byte Imp = 1, Fighter = 2, Pot = 1, Torch = 2;
    private const byte DirtPath = 3, Treasury = 4, Gold = 5, Water = 6, Lava = 7, Rock = 2, RevealedFloor = 8;
    private static readonly TilePos HiddenFloor = new(6, 1), HiddenRoom = new(7, 2), OwnFloor = new(1, 1), Scanned = new(5, 2);
    private static readonly PlayerRunId Run = new("observation-test");

    private static PlayerStamp Stamp(WorldState world, long revision = 1) =>
        new(Run, Viewer, world.CurrentTick, new PlayerRevision(revision));

    private static GameDatabase Database()
    {
        GameDatabase basic = ScenarioTests.SyntheticDatabase();
        TerrainDef template = basic.TerrainById(DirtPath);
        TerrainDef Terrain(byte id, string name, TerrainFlags flags, ushort gold = 0) => template with
        {
            TerrainId = id, Name = name, Flags = flags, GoldValue = gold, MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id,
        };
        CreatureDef imp = basic.CreatureById(Imp);
        ObjectDef Object(byte id, string name, ObjectFlags flags) => new()
        {
            Name = name, ObjectId = id, Flags = flags, Hp = 1, ManaValue = 0, RoomCapacity = 0, StartState = 0,
            PickUpPriority = 0, Width = new Fp32(0), Height = new Fp32(0), Mass = new Fp32(0), Speed = new Fp32(0),
            NameStringId = 0, TooltipStringId = 0,
        };
        basic.Variables.Misc[VariableTypeId.MaxNumberOfThingsInHand] = new MiscVariable(VariableTypeId.MaxNumberOfThingsInHand, 8, 0, 0);
        return new GameDatabase(
            basic.Terrain.Values.Concat(
            [
                Terrain(27, "Fog Of War", TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable),
                Terrain(38, "Claim Scan", TerrainFlags.Solid),
                Terrain(Gold, "Gold", TerrainFlags.Solid | TerrainFlags.Taggable | TerrainFlags.RevealThroughFogOfWar, gold: 100),
                Terrain(Water, "Water", TerrainFlags.Water | TerrainFlags.RevealThroughFogOfWar),
                Terrain(Lava, "Lava", TerrainFlags.Lava | TerrainFlags.RevealThroughFogOfWar),
                // Ownable ground that reveals through fog, so a scan could leak its owner if it would.
                Terrain(RevealedFloor, "Revealed Floor", TerrainFlags.Ownable | TerrainFlags.RevealThroughFogOfWar),
            ]),
            basic.Rooms.Values,
            [imp, imp with { Name = "Fighter", CreatureId = Fighter, Flags = CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped }],
            basic.Variables,
            // The panel prices Create Imp by its key (BottomPanelView.CreateImpSpellId); give it that id.
            basic.KeeperSpells.Values.Select(k => k with { KeeperSpellId = BottomPanelView.CreateImpSpellId }),
            [Object(Pot, "Pot", ObjectFlags.CanBePickedUp), Object(Torch, "Torch", 0)]);
    }

    /// <summary>Columns 0-3 are the viewer's explored Dirt Path; 4-7 the rival's, unexplored.</summary>
    private static (GameDatabase Database, WorldState World) World(int width = Width, int height = Height)
    {
        GameDatabase database = Database();
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = new MapTile(DirtPath, x < Seen ? (byte)3 : (byte)4, 0, 0);
        WorldState world = WorldState.FromMap(database, new LevelMap(width, height, tiles), [3, 4], fogOfWarEnabled: true);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < Seen; x++)
                world.Tile(x, y).SetExploredBy(3);
        return (database, world);
    }

    private static PlayerObservation Observe(GameDatabase database, WorldState world, long revision = 1) =>
        new PlayerObservationBuilder(database, Viewer).Capture(world, Stamp(world, revision), []).Observation;

    /// <summary>Everything the rival has that the viewer cannot see, including a message posted
    /// before the viewer's own, which advances the shared queue id.</summary>
    private static void AddHiddenDifferences(GameDatabase database, WorldState world)
    {
        world.SpawnCreature(database, Fighter, Rival, HiddenFloor);
        world.SpawnObject(database, Pot, Rival, HiddenFloor.Y * world.Width + HiddenFloor.X);
        world.Tile(HiddenRoom).TerrainId = Treasury;
        world.Tile(HiddenRoom).GoldHeld = 900;
        world.Tile(Scanned).OwnerPlayerId = 4;
        world.Player(Rival).Mana = 5000;
        world.Messages.Post(world, 4, PlayerMessageKind.ScriptedSpeech, 7, 0);
    }

    private static void CommonView(WorldState world)
    {
        world.Tile(Scanned).TerrainId = Gold;
        world.Tile(Scanned).SetDiscoveredBy(3);
        world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 7, 0);
    }

    [Fact]
    public void HiddenDifferencesDoNotChangeTheObservation()
    {
        var (database, plain) = World();
        var (_, hidden) = World();
        AddHiddenDifferences(database, hidden);
        CommonView(plain);
        CommonView(hidden);

        Assert.Equal(Describe(Observe(database, plain)), Describe(Observe(database, hidden)));
        Assert.Equal(1, Assert.Single(Observe(database, hidden).Notices).Ordinal);
    }

    [Fact]
    public void RevealingTheSameDifferencesChangesTheObservation()
    {
        var (database, plain) = World();
        var (_, hidden) = World();
        AddHiddenDifferences(database, hidden);
        foreach (WorldState world in new[] { plain, hidden })
        {
            world.Tile(HiddenFloor).SetExploredBy(3);
            world.Tile(HiddenRoom).SetExploredBy(3);
        }

        PlayerObservation a = Observe(database, plain), b = Observe(database, hidden);

        Assert.NotEqual(Describe(a), Describe(b));
        Assert.Empty(a.Things);
        PlayerVisibleThing rival = Assert.Single(b.Things, t => t.Kind == PlayerThingKind.Creature);
        Assert.Equal((Rival, HiddenFloor, PlayerThingActions.None), (rival.Owner, rival.Tile, rival.Actions));
        PlayerVisibleThing pot = Assert.Single(b.Things, t => t.Kind == PlayerThingKind.Object);
        Assert.Equal((new ObjectTypeId(Pot), PlayerThingActions.PickUp), (pot.ObjectType, pot.Actions));
        Assert.Equal((RoomTypeId.Treasury, Rival), (b.Tiles[HiddenRoom].Room, b.Tiles[HiddenRoom].Owner));
        // The rival's Treasury is not the viewer's room, and the rival's message is not the viewer's.
        Assert.Empty(b.Rooms);
        Assert.Empty(b.Notices);
        Assert.Equal(PlayerActionKinds.PickUp, b.Offers.Actions & (PlayerActionKinds.PickUp | PlayerActionKinds.Slap));
    }

    [Fact]
    public void TilesShowHiddenScannedAndExploredGroundDifferently()
    {
        var (database, world) = World();
        world.Tile(5, 0).SetTaggedBy(3, true);
        world.Tile(6, 0).SetTaggedBy(4, true);
        world.Tile(Scanned).TerrainId = Gold;
        world.Tile(Scanned).SetDiscoveredBy(3);
        world.Tile(4, 2).SetDiscoveredBy(3);          // scanned Dirt Path does not reveal through fog
        world.Tile(4, 1).TerrainId = RevealedFloor;   // the rival's, scanned: shown without its owner
        world.Tile(4, 1).SetDiscoveredBy(3);
        world.Tile(0, 0).TerrainId = Rock;
        world.Tile(1, 0).TerrainId = Water;
        world.Tile(2, 0).TerrainId = Lava;
        world.Tile(3, 0).TerrainId = Gold;
        PlayerObservation observation = Observe(database, world);

        for (int y = 0; y < Height; y++)
            for (int x = Seen; x < Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile expected = at == new TilePos(5, 0) ? new PlayerTile(PlayerTileFlags.Tagged, default, default)
                    : at == Scanned ? new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable | PlayerTileFlags.GoldSeam, default, default)
                    : at == new TilePos(4, 1) ? new PlayerTile(PlayerTileFlags.Known, default, default)
                    : default;
                Assert.Equal(expected, observation.Tiles[at]);
            }
        Assert.Equal(new PlayerTile(PlayerTileFlags.Known, Viewer, default), observation.Tiles[OwnFloor]);
        Assert.Equal(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable, observation.Tiles[new TilePos(0, 0)].Flags);
        Assert.Equal(PlayerTileFlags.Known | PlayerTileFlags.Water, observation.Tiles[new TilePos(1, 0)].Flags);
        Assert.Equal(PlayerTileFlags.Known | PlayerTileFlags.Lava, observation.Tiles[new TilePos(2, 0)].Flags);
        Assert.Equal(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable | PlayerTileFlags.GoldSeam,
            observation.Tiles[new TilePos(3, 0)].Flags);
    }

    [Fact]
    public void OwnCreaturesInTheHandAndOwnMessagesAreShown()
    {
        var (database, world) = World();
        CreatureState walking = world.SpawnCreature(database, Fighter, Viewer, OwnFloor);
        CreatureState held = world.SpawnCreature(database, Fighter, Viewer, new TilePos(2, 1));
        world.Player(Viewer).Hand.Add(HandItem.Of(held));
        held.InHand = true;
        world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 7, 0);

        PlayerObservation observation = Observe(database, world);

        Assert.Equal(1, observation.HandCount);
        Assert.Equal(2, observation.Creatures.Count);
        PlayerCreatureSummary inHand = Assert.Single(observation.Creatures, c => c.InHand);
        Assert.Equal((null, null), (inHand.Tile, inHand.Handle));
        PlayerCreatureSummary onFloor = Assert.Single(observation.Creatures, c => !c.InHand);
        Assert.Equal(walking.Tile, onFloor.Tile);
        PlayerVisibleThing target = Assert.Single(observation.Things);
        Assert.Equal(target.Handle, onFloor.Handle);
        Assert.Equal(PlayerThingActions.PickUp | PlayerThingActions.Slap, target.Actions);
        PlayerNotice notice = Assert.Single(observation.Notices);
        Assert.Equal((1, PlayerMessageKind.ScriptedSpeech, world.CurrentTick), (notice.Ordinal, notice.Kind, notice.Tick));
        Assert.True((observation.Offers.Actions & PlayerActionKinds.DropAt) != 0);
    }

    [Fact]
    public void ObjectsAreOfferedOnlyWhenTheHandCanLiftThem()
    {
        var (database, world) = World();
        world.SpawnObject(database, Pot, Viewer, OwnFloor.Y * Width + OwnFloor.X);
        world.SpawnObject(database, Torch, Viewer, 2 * Width + 2);
        PlayerObservation observation = Observe(database, world);

        Assert.Equal(PlayerThingActions.PickUp, Assert.Single(observation.Things, t => t.ObjectType == new ObjectTypeId(Pot)).Actions);
        Assert.Equal(PlayerThingActions.None, Assert.Single(observation.Things, t => t.ObjectType == new ObjectTypeId(Torch)).Actions);
    }

    [Fact]
    public void AnOwnPrisonerInUnseenCellsHasNoTile()
    {
        var (database, world) = World();
        CreatureState prisoner = world.SpawnCreature(database, Fighter, Viewer, HiddenFloor);
        prisoner.PrisonOwnerPlayerId = 4;

        Assert.Null(Assert.Single(Observe(database, world).Creatures).Tile);
        world.Tile(HiddenFloor).SetExploredBy(3);
        Assert.Equal(HiddenFloor, Assert.Single(Observe(database, world).Creatures).Tile);
    }

    [Fact]
    public void NoticesKeepTheirFirstSeenTickAndOrdinal()
    {
        var (database, world) = World();
        var simulation = new GameSimulation(database, world, 1, []);
        var builder = new PlayerObservationBuilder(database, Viewer);
        world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 7, 0);
        GameTick first = world.CurrentTick;
        builder.Capture(world, Stamp(world), []);
        simulation.Tick(InputFrame.Empty);
        world.Messages.Post(world, 4, PlayerMessageKind.ScriptedSpeech, 7, 0);
        world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 8, 0);

        PlayerObservation later = builder.Capture(world, Stamp(world, 2), []).Observation;

        Assert.NotEqual(first, world.CurrentTick);
        Assert.Equal([(first, 1), (world.CurrentTick, 2)], later.Notices.Select(n => (n.Tick, n.Ordinal)));
    }

    [Fact]
    public void ReceiptsPassThroughAsGiven()
    {
        var (database, world) = World();
        PlayerReceipt[] receipts =
        [
            new(new PlayerActionId(1), PlayerReceiptStatus.Submitted, scheduledTick: new GameTick(1)),
            new(new PlayerActionId(2), PlayerReceiptStatus.Refused, PlayerRefusalReason.StaleObservation),
        ];
        Assert.Equal(receipts, new PlayerObservationBuilder(database, Viewer).Capture(world, Stamp(world), receipts).Observation.Receipts);
    }

    [Fact]
    public void EmptyHistoryGivesAnEmptyObservationOfOnesOwnGround()
    {
        var (database, world) = World();
        PlayerObservation observation = Observe(database, world);

        Assert.Empty(observation.Notices);
        Assert.Empty(observation.Things);
        Assert.Empty(observation.Creatures);
        Assert.Equal(0, observation.HandCount);
        Assert.Equal(PlayerPhase.Playing, observation.Phase);
        Assert.Equal((Width, Height), (observation.Tiles.Width, observation.Tiles.Height));
        Assert.Equal(PlayerActionKinds.None, observation.Offers.Actions & (PlayerActionKinds.DropAt | PlayerActionKinds.PickUp));
    }

    [Fact]
    public void HandlesAreOrdinalsOfTheirRevisionAndResolveOnlyThere()
    {
        var (database, world) = World();
        world.SpawnCreature(database, Fighter, Viewer, OwnFloor);
        world.SpawnCreature(database, Fighter, Viewer, new TilePos(2, 2));
        PlayerObservationCapture capture = new PlayerObservationBuilder(database, Viewer).Capture(world, Stamp(world, 7), []);

        Assert.Equal(2, capture.Observation.Things.Count);
        for (int i = 0; i < capture.Observation.Things.Count; i++)
        {
            PlayerVisibleThing thing = capture.Observation.Things[i];
            Assert.Equal(new PlayerThingHandle(new PlayerRevision(7), i), thing.Handle);
            Assert.True(capture.TryResolve(thing.Handle, out PlayerInputTarget target));
            Assert.Equal(thing.Tile, target.Tile);
            Assert.NotEqual(default, thing);
        }
        Assert.All(capture.Observation.Creatures, c => Assert.Contains(capture.Observation.Things, t => t.Handle == c.Handle));
        Assert.False(capture.TryResolve(new PlayerThingHandle(new PlayerRevision(6), 0), out _));
        Assert.False(capture.TryResolve(new PlayerThingHandle(new PlayerRevision(7), capture.Observation.Things.Count), out _));
    }

    [Fact]
    public void CaptureNeitherChangesTheWorldNorLetsLaterChangesIn()
    {
        var (database, world) = World();
        world.SpawnCreature(database, Fighter, Viewer, OwnFloor);
        world.Messages.Post(world, 3, PlayerMessageKind.ScriptedSpeech, 7, 0);
        var simulation = new GameSimulation(database, world, 1, []);
        ulong before = simulation.StateHash();
        var builder = new PlayerObservationBuilder(database, Viewer);

        PlayerObservation first = builder.Capture(world, Stamp(world), []).Observation;
        string described = Describe(first);
        builder.Capture(world, Stamp(world, 2), []);
        Assert.Equal(before, simulation.StateHash());

        world.SpawnCreature(database, Fighter, Viewer, new TilePos(2, 2));
        world.Tile(5, 0).SetExploredBy(3);
        world.Player(Viewer).Mana = 1234;
        builder.Capture(world, Stamp(world, 3), []);
        Assert.Equal(described, Describe(first));
    }

    [Fact]
    public void PhasesFollowTheResultAndTheIntroPath()
    {
        var (database, world) = World();
        var builder = new PlayerObservationBuilder(database, Viewer);

        world.Player(Viewer).CameraPathEndTick = world.CurrentTick.Value + 10;
        PlayerObservation intro = builder.Capture(world, Stamp(world), []).Observation;
        Assert.Equal((PlayerPhase.Intro, PlayerActionKinds.SkipIntro), (intro.Phase, intro.Offers.Actions));

        // A result outranks a camera path still playing.
        world.Player(Viewer).Status = PlayerStatus.Won;
        PlayerObservation ended = builder.Capture(world, Stamp(world, 2), []).Observation;
        Assert.Equal((PlayerPhase.Ended, PlayerResult.Won, PlayerActionKinds.None), (ended.Phase, ended.Result, ended.Offers.Actions));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(5000, true)]
    public void RoomOffersCarryThePanelsPriceAndAffordability(int gold, bool affordable)
    {
        var (database, world) = World();
        world.Tile(OwnFloor).TerrainId = Treasury;
        world.Tile(OwnFloor).GoldHeld = gold;
        world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Treasury, 3));
        PlayerObservation observation = Observe(database, world);

        PlayerRoomOffer treasury = Assert.Single(observation.Offers.Rooms);
        Assert.Equal((RoomTypeId.Treasury, new GoldAmount(200), affordable), (treasury.Room, treasury.Cost, treasury.Affordable));
        Assert.Equal(new GoldAmount(gold), observation.Gold);
        Assert.Equal(new PlayerRoomSummary(RoomTypeId.Treasury, 1), Assert.Single(observation.Rooms));
        Assert.Equal(PlayerActionKinds.TagArea | PlayerActionKinds.BuildRoom | PlayerActionKinds.SelectPanel,
            observation.Offers.Actions & (PlayerActionKinds.TagArea | PlayerActionKinds.BuildRoom | PlayerActionKinds.SelectPanel));
    }

    [Fact]
    public void AnOfferedCreateImpCarriesThePanelsPrice()
    {
        var (database, world) = World();
        world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, BottomPanelView.CreateImpSpellId, 3));
        PlayerObservation observation = Observe(database, world);

        var price = new ManaAmount(ImpSupplyRules.CreateImpCost(world, database, Viewer));
        Assert.Equal(price, observation.Offers.CreateImpCost);
        Assert.True((observation.Offers.Actions & PlayerActionKinds.CreateImpAt) != 0);
        Assert.Contains(DK2.Simulation.Commands.GuiButtonTarget.Spell(BottomPanelView.CreateImpSpellId), observation.Offers.Panels);
    }

    /// <summary>A steady capture allocates the detached tile copy and a small fixed remainder. The
    /// bound is loose on purpose (measured 2026-11-09: 5 KB on Level 1's 30x38, 32 KB on Level 20's
    /// 99x99); what it catches is a cost per tile, which a 64x64 map multiplies past it.</summary>
    [Fact]
    public void SteadyCaptureAllocatesTheTileCopyAndLittleElse()
    {
        var (database, world) = World(64, 64);
        world.SpawnCreature(database, Fighter, Viewer, OwnFloor);
        var builder = new PlayerObservationBuilder(database, Viewer);
        builder.Capture(world, Stamp(world), []);

        const int captures = 20;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < captures; i++) builder.Capture(world, Stamp(world, 2 + i), []);
        long perCapture = (GC.GetAllocatedBytesForCurrentThread() - before) / captures;

        long tileCopy = 64 * 64 * System.Runtime.CompilerServices.Unsafe.SizeOf<PlayerTile>();
        Assert.True(perCapture <= tileCopy + 8192, $"{perCapture} bytes per capture, tile copy {tileCopy}");
    }

    [Fact]
    public void CaptureRefusesAStampForAnotherPlayerOrRun()
    {
        var (database, world) = World();
        var builder = new PlayerObservationBuilder(database, Viewer);
        Assert.Throws<ArgumentException>(() => builder.Capture(world, Stamp(world) with { Player = Rival }, []));
        Assert.Throws<ArgumentException>(() => builder.Capture(world, default, []));
    }

    /// <summary>Every field a controller receives, in order: two observations with the same text
    /// are indistinguishable to it.</summary>
    internal static string Describe(PlayerObservation o)
    {
        var text = new StringBuilder();
        text.Append(o.Stamp).Append('|').Append(o.Phase).Append('|').Append(o.Result).Append('|')
            .Append(o.Gold).Append('|').Append(o.Mana).Append('|').Append(o.HandCount).Append('\n');
        for (int y = 0; y < o.Tiles.Height; y++)
        {
            for (int x = 0; x < o.Tiles.Width; x++) text.Append(o.Tiles[new TilePos(x, y)]).Append(';');
            text.Append('\n');
        }
        foreach (PlayerRoomSummary r in o.Rooms) text.Append(r).Append('\n');
        foreach (PlayerCreatureSummary c in o.Creatures) text.Append(c).Append('\n');
        foreach (PlayerVisibleThing t in o.Things) text.Append(t).Append('\n');
        foreach (PlayerNotice n in o.Notices) text.Append(n).Append('\n');
        foreach (PlayerReceipt r in o.Receipts) text.Append(r).Append('\n');
        text.Append(o.Offers.Actions).Append('|').Append(o.Offers.CreateImpCost).Append('\n');
        foreach (PlayerRoomOffer r in o.Offers.Rooms) text.Append(r).Append('\n');
        foreach (var p in o.Offers.Panels) text.Append(p).Append('\n');
        return text.ToString();
    }
}
