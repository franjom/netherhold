// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Possession upkeep, <c>0x004C17A0</c> and <c>0x004C10E0</c> (Q58,
/// docs/research/keeper-spell-possession.md §Upkeep).</summary>
public sealed class PossessionUpkeepTests
{
    private const byte Keeper = PlayerIds.FirstKeeper, SpellId = PossessionRules.SpellId;
    private const short GoblinCost = 150;

    /// <summary>A made-up stat-27 row for levels 2–10: a zero entry and an oversized one reach
    /// both clamps (GameDatabase drops a negative row, as it does for every stat).</summary>
    private static readonly int[] Stat27Row = [0, 133, 200, 250, 300, 350, 400, 500, 30000];

    private static LevelDef Level(ushort flags) => new(Array.Empty<byte>())
    {
        Name = "", Description = "", Author = "", Email = "", Information = "",
        Unknown_1184 = default, Messages = [], Flags = flags, SoundCategory = "",
        PreviousRewards = default, NextRewards = default, IntroductionOverrideObjects = default,
        IntroductionOverrideTextIds = [], TerrainPath = "", Unknown_6403 = default,
        Paths = [], UnknownPathTail = [],
    };

    private static (GameDatabase Db, WorldState World, CreatureState Me) Setup(
        ushort levelFlags = PossessionRules.UpkeepLevelFlag, bool upgraded = false, int level = 1,
        short cost = GoblinCost)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        GlobalVariables vars = Dk2TerrainFixture.Variables();
        for (int i = 0; i < Stat27Row.Length; i++)
            vars.CreatureStats.Add(new CreatureStatEntry((int)CreatureStatId.PossessionManaCost, Stat27Row[i], i + 2, false));
        var spell = new KeeperSpellDef
        {
            Name = "Possession", KeeperSpellId = SpellId, ManaCost = 500, ManaUpkeepPerSecond = 25,
            ShotTypeId = 32, BonusShotTypeId = 32, LocationSelector = 5, TargetSelector = 6,
        };
        GameDatabase db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.GoblinCreatureId
                ? c with { PossessionManaCost = cost } : c), vars,
            basis.KeeperSpells.Values.Where(k => k.KeeperSpellId != SpellId).Append(spell),
            objects: basis.Objects.Values, shots: basis.Shots.Values).ForLevel(null, Level(levelFlags));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, SpellId, 3, Upgraded: upgraded));
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        me.Level = level;
        me.Possessed = true;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        world.Player(Keeper).Mana = 50_000;
        return (db, world, me);
    }

    private static int IndexOf(WorldState world)
    {
        for (int i = 0; ; i++)
            if (world.PlayerAtIndex(i).PlayerId == Keeper) return i;
    }

    private static int Charge(GameDatabase db, WorldState world) =>
        PossessionRules.UpkeepPerSecond(world, db, world.Player(Keeper));

    [Fact]
    public void LevelOneChargesTheCreaturesUnsignedCost_FromBinary()
    {
        var (db, world, _) = Setup();
        Assert.Equal(GoblinCost, Charge(db, world));
    }

    [Fact]
    public void ClearLevelFlagChargesNothingAndNeverEndsPossession_FromBinary()
    {
        var (db, world, me) = Setup(levelFlags: 0);
        Assert.Equal(0, Charge(db, world));
        world.Player(Keeper).Mana = 0;
        new GameSimulation(db, world, 1, [new ManaSystem()]).Run(SimulationTiming.TicksPerSecond);
        Assert.Equal(me.Id, world.Player(Keeper).PossessedCreatureId);
    }

    [Fact]
    public void NothingPossessedChargesNothing()
    {
        var (db, world, _) = Setup();
        world.Player(Keeper).PossessedCreatureId = 0;
        Assert.Equal(0, Charge(db, world));
    }

    [Theory]
    [InlineData(2, GoblinCost, 0)]          // 0 % → below 1 → 0
    [InlineData(3, GoblinCost, 199)]        // 133 % truncates 199.5
    [InlineData(4, GoblinCost, 300)]
    [InlineData(10, GoblinCost, 45000)]
    [InlineData(10, 3000, 0xFFFE)]          // 900000 clamped to 0xFFFE
    public void HigherLevelsScaleByStat27WithASignedDivideAndClamp_FromBinary(int level, short cost, int expected)
    {
        var (db, world, _) = Setup(level: level, cost: cost);
        Assert.Equal(expected, Charge(db, world));
    }

    [Fact]
    public void UpgradedPercentageAppliesAfterTheLevelScale_FromBinary()
    {
        var (db, world, _) = Setup(upgraded: true, level: 3);
        Assert.Equal(70 * 199 / 100, Charge(db, world));
    }

    [Fact]
    public void UpgradedSpellTakesItsPercentageAfterTheLevelScale_FromBinary()
    {
        var (db, world, _) = Setup(upgraded: true);
        int percent = (byte)db[VariableTypeId.UpgradedPossessionCostPercentage];
        Assert.Equal(percent * GoblinCost / 100, Charge(db, world));
    }

    [Fact]
    public void ChargeJoinsTheSecondsDrain_FromBinary()
    {
        var (db, world, _) = Setup();
        int withPossession = ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[IndexOf(world)];
        world.Player(Keeper).PossessedCreatureId = 0;
        int without = ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[IndexOf(world)];
        Assert.Equal(GoblinCost, withPossession - without);
    }

    /// <summary>Sets mana so that after the second's payment it stands at the imp and trap upkeep
    /// plus the charge plus <paramref name="margin"/>, then runs the payment.</summary>
    private static void PayWithMarginAfterPayment(GameDatabase db, WorldState world, int margin)
    {
        int index = IndexOf(world);
        int gain = ManaRules.GainPerSecondByPlayerIndex(world, db)[index];
        int upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[index];
        int held = ManaRules.ImpAndTrapUpkeepPerSecondByPlayerIndex(world, db)[index];
        PlayerState probe = world.Player(Keeper);
        probe.Mana = 50_000;
        ManaRules.ApplyOneSecond(db, ref probe, gain, upkeep);
        int delta = probe.Mana - 50_000;
        world.Player(Keeper).Mana = held + Charge(db, world) + margin - delta;
        new GameSimulation(db, world, 1, [new ManaSystem()]).Run(SimulationTiming.TicksPerSecond);
    }

    [Fact]
    public void ManaLeftEqualToTheChargeKeepsPossession_FromBinary()
    {
        var (db, world, me) = Setup();
        PayWithMarginAfterPayment(db, world, 0);
        Assert.Equal(me.Id, world.Player(Keeper).PossessedCreatureId);
        Assert.True(me.Possessed);
    }

    [Fact]
    public void ManaLeftOneBelowTheChargeEndsPossessionThroughLeave_FromBinary()
    {
        var (db, world, me) = Setup();
        PayWithMarginAfterPayment(db, world, -1);
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
        Assert.False(me.Possessed);
        PlayerMessage posted = Assert.Single(world.Messages.Entries, m => m.Kind == PlayerMessageKind.ManaShortfall);
        Assert.Equal((PossessionRules.PossessionEndedSubtype, 0), (posted.Data1, posted.Data2));
    }

    [Fact]
    public void ZeroChargeStillEndsPossessionWhenManaIsBelowTheImpUpkeep_FromBinary()
    {
        var (db, world, _) = Setup(cost: 0);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(11, 10));
        Assert.Equal(0, Charge(db, world));
        Assert.True(ManaRules.ImpAndTrapUpkeepPerSecondByPlayerIndex(world, db)[IndexOf(world)] > 0);
        PayWithMarginAfterPayment(db, world, -1);
        Assert.Equal(0, world.Player(Keeper).PossessedCreatureId);
    }

    [Fact]
    public void NegativeAuthoredCostIsReadUnsigned_FromBinary()
    {
        var (db, world, _) = Setup(cost: -2);
        Assert.Equal(0xFFFE, Charge(db, world));
    }
}
