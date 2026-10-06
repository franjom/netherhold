// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Research upgrades for the keeper spells of ADR-0350, granted through
/// <see cref="ResearchTreeRules"/> (ADR-0247). The grant is the spell grant <c>0x004E0F00</c>,
/// the only caller of the flag's setter <c>0x004BB070</c>; the chooser <c>0x004C3000</c> takes a
/// spell only when its record has a bonus shot. Each spell's upgraded effect is the bonus data
/// its own tests already cover (CreateGoldTests, TremorTests, TurncoatTests,
/// PossessionUpkeepTests); here the flag is reached by research instead of being set by hand.
/// Records are the shipped bytes, pinned by ResearchTreeOnRealDataTests.</summary>
public sealed class KeeperSpellUpgradeTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const AvailabilityKind SpellKind = AvailabilityKind.Spell;
    private const int Available = 3;
    private const byte SightOfEvil = 1, CreateGold = 13;
    private static readonly TilePos GoldAt = new(10, 10);

    private static int Second => SimulationTiming.TicksPerSecond;

    private static KeeperSpellDef[] TaskSpells() =>
    [
        new()
        {
            Name = "Create Gold", KeeperSpellId = CreateGold, ManaCost = 15000, ManaUpkeepPerSecond = 0,
            LocationSelector = 1, TargetSelector = 4,
            ShotTypeId = 5, ShotData1 = 1, ShotData2 = 1000,
            BonusShotTypeId = 5, BonusShotData1 = 1, BonusShotData2 = 2000,
        },
        new()
        {
            Name = "Tremor", KeeperSpellId = TremorRules.SpellId, ManaCost = 30000, ManaUpkeepPerSecond = 0,
            LocationSelector = 5, TargetSelector = 4,
            ShotTypeId = 24, ShotData1 = 5, ShotData2 = 3,
            BonusShotTypeId = 24, BonusShotData1 = 7, BonusShotData2 = 3,
        },
        new()
        {
            Name = "Turncoat", KeeperSpellId = TurncoatRules.SpellId, ManaCost = 20000, ManaUpkeepPerSecond = 0,
            LocationSelector = 1, TargetSelector = 2,
            ShotTypeId = 31, ShotData1 = 25, BonusShotTypeId = 31, BonusShotData1 = 40,
        },
        new()
        {
            Name = "Possession", KeeperSpellId = PossessionRules.SpellId, ManaCost = 500,
            ManaUpkeepPerSecond = 25, LocationSelector = 5, TargetSelector = 6,
            ShotTypeId = 32, BonusShotTypeId = 32,
        },
        new()
        {
            Name = "Sight Of Evil", KeeperSpellId = SightOfEvil, ManaCost = 5000, ManaUpkeepPerSecond = 0,
            LocationSelector = 5, TargetSelector = 4,
            ShotTypeId = 33, ShotData1 = 5, BonusShotTypeId = 33, BonusShotData1 = 7,
        },
        new()
        {
            Name = "Summon Horny", KeeperSpellId = ReaperRules.SpellId, ManaCost = 100000,
            ManaUpkeepPerSecond = 2000, LocationSelector = 1, TargetSelector = 4, ShotTypeId = 46,
        },
        new()
        {
            Name = "OneShotReaper", KeeperSpellId = ReaperRules.OneShotSpellId, ManaCost = 0,
            ManaUpkeepPerSecond = 125, LocationSelector = 1, TargetSelector = 4,
            ShotTypeId = 46, ShotData1 = 99,
        },
    ];

    /// <summary>The fixture with the task's spells and a one-entry research list: the spell.</summary>
    private static GameDatabase Db(byte spellId)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var level = new GlobalVariables();
        level.Research.Add(new ResearchEntry(ResearchTreeRules.KindSpell, spellId, 40));
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, [.. basis.KeeperSpells.Values, .. TaskSpells()],
            objects: basis.Objects.Values, shots: basis.Shots.Values).ForLevel(level);
    }

    /// <summary>The spell AVAILABLE and not upgraded, a 3×3 Library and a Vampire researching
    /// in it (one point a tick), as in ResearchTreeTests.</summary>
    private static WorldState World(GameDatabase db, byte spellId)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Neutral, Keeper]);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(SpellKind, spellId, Available));
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), Keeper);

        var at = new TilePos(1, 1);
        CreatureState it = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, Keeper, at);
        it.CurrentJob = JobType.Research;
        StatePlacement.As(it, CreatureActivity.AtWork);
        StatePlacement.Place(it, JobRules.StateFor(it.CurrentJob)!.Value);
        it.JobTileIndex = (at.Y * world.Width) + at.X;
        return world;
    }

    [Theory]
    [InlineData(CreateGold)]
    [InlineData(TremorRules.SpellId)]
    [InlineData(TurncoatRules.SpellId)]
    [InlineData(PossessionRules.SpellId)]
    [InlineData(SightOfEvil)]
    public void ResearchUpgradesAnAvailableSpellWithABonusShot_AndMakesNoBook(byte spellId)
    {
        GameDatabase db = Db(spellId);
        WorldState world = World(db, spellId);
        var sim = new GameSimulation(db, world, 42, [new ResearchSystem(), new CreatureJobSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Run(Second * 12);

        Assert.True(world.IsUpgraded(Keeper, SpellKind, spellId));
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == ResearchTreeRules.BookObjectTypeId);
        Assert.Contains(events.Events, e => e.Kind == "research_upgraded");
        Assert.Equal(0, world.Player(Keeper).ResearchTarget);
    }

    [Theory]
    [InlineData(ReaperRules.SpellId)]
    [InlineData(ReaperRules.OneShotSpellId)]
    public void ASpellWithoutABonusShotIsNeverAResearchTarget(byte spellId)
    {
        GameDatabase db = Db(spellId);
        WorldState world = World(db, spellId);
        var sim = new GameSimulation(db, world, 42, [new ResearchSystem(), new CreatureJobSystem()]);

        sim.Run(Second * 12);

        Assert.False(world.IsUpgraded(Keeper, SpellKind, spellId));
        Assert.Equal(0, world.Player(Keeper).ResearchTarget);
    }

    [Fact]
    public void CreateGoldUpgradedByResearchMakesTwoThousand()
    {
        GameDatabase db = Db(CreateGold);
        WorldState world = World(db, CreateGold);
        world.Player(Keeper).Mana = 45000;
        world.Tile(GoldAt).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(GoldAt).OwnerPlayerId = Keeper;
        var sim = new GameSimulation(db, world, 42,
            [new ResearchSystem(), new CreatureJobSystem(), new KeeperSpellSystem(), new ShotSystem()]);
        sim.Run(Second * 12);
        Assert.True(world.IsUpgraded(Keeper, SpellKind, CreateGold));

        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(
            Keeper, CreateGold, CreatureState.CentreOf(GoldAt.X), CreatureState.CentreOf(GoldAt.Y))));
        sim.Run(2);

        ObjectState pile = Assert.Single(world.Objects, o => o.ObjectTypeId == FloorGold.FloorGoldObjectId);
        Assert.Equal(2000, pile.GoldHeld);
    }
}
