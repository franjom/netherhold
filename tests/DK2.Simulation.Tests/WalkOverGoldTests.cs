// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The creature arm of the walk-over gold pick-up (0x00489246–0x0048954B,
/// gold-on-the-floor.md §12, ADR-0300): every creature the worker arm does not take puts the
/// neutral piles on its tile into its purse, whole, up to its level-scaled MaxGoldHeld.</summary>
public sealed class WalkOverGoldTests
{
    private const byte Keeper = 3, Rival = 4;
    private static readonly TilePos Spot = new(5, 5);

    private sealed record Run(GameDatabase Db, WorldState World, GameSimulation Sim)
    {
        public int Index => (Spot.Y * World.Width) + Spot.X;
        public ObjectState Pile(int gold) => FloorGold.Drop(World, Db, Index, gold)!;
        public CreatureState Spawn(byte creatureId, byte owner = Keeper) =>
            World.SpawnCreature(Db, creatureId, owner, Spot);
        public void Tick() => Sim.Tick();
        public ObjectState Chicken(byte owner = PlayerIds.Neutral) =>
            World.SpawnObject(Db, HatcheryRules.ChickenObjectId, new PlayerId(owner), Index);
    }

    private static Run Start(byte terrain = Dk2TerrainFixture.ClaimedPath)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(12, 12), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < 11; y++)
        {
            for (int x = 1; x < 11; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }
        world.Tile(Spot.X, Spot.Y).TerrainId = terrain;
        return new Run(db, world, new GameSimulation(db, world, 1, [new WalkOverGoldSystem()]));
    }

    [Fact]
    public void AGoblinOnACorridorPileTakesWhatItsPurseLacksAndTheRestStays()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.GoldHeld = 2000;
        ObjectState pile = run.Pile(4000);

        run.Tick();

        Assert.Equal(5000, goblin.GoldHeld);
        Assert.Equal(1000, pile.GoldHeld);
        Assert.NotNull(run.World.ObjectById(pile.Id));
    }

    [Fact]
    public void TheMaximumIsScaledByLevel()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.Level = 2;
        run.Pile(9000);

        run.Tick();

        Assert.Equal(5250, goblin.GoldHeld); // 5000 at 105%
    }

    [Fact]
    public void EveryPileOnTheTileGoesInOneTickUntilThePurseIsFull()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.GoldHeld = 1000;
        run.Pile(2000);
        run.Pile(2500);

        run.Tick();

        Assert.Equal(5000, goblin.GoldHeld);
        Assert.Equal(500, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.TreasuryTerrain)]
    [InlineData(Dk2TerrainFixture.CasinoTerrain)]
    public void NothingIsTakenOnATreasuryOrCasinoTile(byte terrain)
    {
        Run run = Start(terrain);
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        run.Pile(800);

        run.Tick();

        Assert.Equal(0, goblin.GoldHeld);
        Assert.Equal(800, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Fact]
    public void NothingIsTakenInTheHandDropLanding()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.Dk2StateId = DropLandingRules.LandingState;
        run.Pile(800);

        run.Tick();

        Assert.Equal(0, goblin.GoldHeld);
    }

    [Fact]
    public void NothingIsTakenWhileTheCasinoBigWinnerCountdownRuns()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.CasinoBigWinnerTicks = 1;
        run.Pile(800);

        run.Tick();

        Assert.Equal(0, goblin.GoldHeld);
    }

    [Fact]
    public void ALoosePileOnAHeartTileIsTaken()
    {
        Run run = Start(Dk2TerrainFixture.DungeonHeartTerrain);
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        run.Pile(3388);

        run.Tick();

        Assert.Equal(3388, goblin.GoldHeld);
        Assert.Equal(0, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Fact]
    public void ASkeletonWithNoMaxGoldHeldTakesNothing()
    {
        Run run = Start();
        CreatureState skeleton = run.Spawn(Dk2TerrainFixture.SkeletonCreatureId);
        run.Pile(800);

        run.Tick();

        Assert.Equal(0, skeleton.GoldHeld);
        Assert.Equal(800, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Fact]
    public void TheTakeIsNotHalved()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        ObjectState pile = run.Pile(3000);

        run.Tick();

        Assert.Equal(3000, goblin.GoldHeld);
        Assert.Null(run.World.ObjectById(pile.Id));
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, Rival)]
    [InlineData(Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good)]
    public void AnEnemysCreatureOrAHeroTakesYourFloorGoldTheSameWay(byte creatureId, byte owner)
    {
        Run run = Start();
        CreatureState stranger = run.Spawn(creatureId, owner);
        run.Pile(800);

        run.Tick();

        Assert.Equal(800, stranger.GoldHeld);
    }

    [Fact]
    public void APurseOverItsMaximumTakesTheWholePileAndIsClampedBack()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.GoldHeld = 6000;
        run.Pile(800);

        run.Tick();

        Assert.Equal(5000, goblin.GoldHeld);
        Assert.Equal(0, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Fact]
    public void ACorpseDoesNotTakeBackTheGoldItsDeathDropped()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDeathRules.BecomeCorpse(run.World, run.Db, goblin);
        Assert.True(CreatureDeathRules.IsCorpse(goblin));
        run.Pile(800);

        run.Tick();

        Assert.Equal(0, goblin.GoldHeld);
        Assert.Equal(800, FloorGold.OnTile(run.World, run.Db, run.Index));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 800)]
    public void AKeepersImpIsTheWorkerArmsUnlessPossessed(bool possessed, int taken)
    {
        Run run = Start();
        CreatureState imp = run.Spawn(Dk2TerrainFixture.ImpCreatureId);
        if (possessed) run.World.Player(Keeper).PossessedCreatureId = imp.Id;
        run.Pile(800);

        run.Tick();

        Assert.Equal(taken, imp.GoldHeld);
    }

    /// <summary>The chicken side arm (<c>0x004892C5</c>..<c>0x0048932E</c>, ADR-0775): a loose neutral
    /// Chicken underfoot is destroyed and action 26 begins, whatever the creature's hunger, and nothing
    /// of the creature's changes.</summary>
    [Fact]
    public void ACreatureStandingOnALooseNeutralChickenEatsItWhateverItsHunger()
    {
        Run run = Start();
        CreatureState goblin = run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        goblin.HungerNeed = 0;
        goblin.Health = 100;
        ObjectState chicken = run.Chicken();
        var sink = new ListEventSink();
        run.Sim.ObserveWith(sink);

        run.Tick();

        Assert.Null(run.World.ObjectById(chicken.Id));
        Assert.Equal((0, 100, 0), (goblin.HungerNeed, goblin.Health, goblin.GoldHeld));
        Assert.Contains(sink.Events, e => e.Kind == "chicken_walked_over");
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.HatcheryTerrain)]
    [InlineData(Dk2TerrainFixture.PortalTerrain)]
    public void NotOnAHatcheryOrPortalTile(byte terrain)
    {
        Run run = Start(terrain);
        run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        ObjectState chicken = run.Chicken();

        run.Tick();

        Assert.NotNull(run.World.ObjectById(chicken.Id));
    }

    [Fact]
    public void OnATreasuryTileTheChickenIsStillEaten_TheSkipIsTheGoldsAlone()
    {
        // 0x00489346 tests the Treasury on the gold arm only.
        Run run = Start(Dk2TerrainFixture.TreasuryTerrain);
        run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        ObjectState chicken = run.Chicken();

        run.Tick();

        Assert.Null(run.World.ObjectById(chicken.Id));
    }

    [Fact]
    public void ACasinoTileOrALandingCreatureSkipsTheChicken()
    {
        // 0x00489248..0x0048926A skip the whole walk on a Casino tile; 0x004892A6 refuses state 76.
        Run casino = Start(Dk2TerrainFixture.CasinoTerrain);
        casino.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        ObjectState there = casino.Chicken();
        casino.Tick();
        Assert.NotNull(casino.World.ObjectById(there.Id));

        Run landing = Start();
        CreatureState goblin = landing.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        StatePlacement.Place(goblin, DropLandingRules.LandingState);
        ObjectState under = landing.Chicken();
        landing.Tick();
        Assert.NotNull(landing.World.ObjectById(under.Id));
    }

    [Fact]
    public void AKeepersOwnChickenAndAKeepersImpAreLeftAlone()
    {
        // The arm wants the neutral owner; a keeper's worker never reaches it (0x00489551).
        Run run = Start();
        run.Spawn(Dk2TerrainFixture.GoblinCreatureId);
        ObjectState owned = run.Chicken(Keeper);
        run.Tick();
        Assert.NotNull(run.World.ObjectById(owned.Id));

        Run imps = Start();
        imps.Spawn(Dk2TerrainFixture.ImpCreatureId);
        ObjectState loose = imps.Chicken();
        imps.Tick();
        Assert.NotNull(imps.World.ObjectById(loose.Id));
    }
}
