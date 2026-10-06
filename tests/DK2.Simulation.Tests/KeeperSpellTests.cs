// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class KeeperSpellTests
{
    private const byte Keeper = 3, Rival = 4;

    [Theory]
    [InlineData(false, 150)]
    [InlineData(true, 200)]
    public void CastCreatesTheSelectedShotPaysAndStampsBothClocksButReturnsZero(bool upgraded, int damage)
    {
        var (db, world, target) = Setup();
        world.SetUpgraded(Keeper, AvailabilityKind.Spell, 6, upgraded);
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 9, [boundary, new ShotSystem()]);
        sim.Run(7);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(0, boundary.Result);
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        Assert.Equal(1500 - damage, target.Health);
        Assert.Equal(255, target.Dk2StateId);
        Assert.Equal(6000, world.Player(Keeper).Mana);
        Assert.Equal(6000, world.Player(Keeper).OneShotManaSpent);
        var row = Assert.Single(world.Player(Keeper).Availabilities);
        Assert.Equal((7L, 7L), (row.LastCastTick, row.LastCheckedTick));
        Assert.Equal(damage, Assert.Single(world.Shots).Data1);
        sim.Run(10);
        Assert.Equal(1500 - damage, target.Health);
    }

    [Theory]
    [InlineData(0, KeeperSpellRefusal.UnsupportedSpell)]
    [InlineData(2, KeeperSpellRefusal.Mana)]
    [InlineData(3, KeeperSpellRefusal.MissingCreature)]
    [InlineData(4, KeeperSpellRefusal.Target)]
    [InlineData(5, KeeperSpellRefusal.Target)]
    [InlineData(6, KeeperSpellRefusal.Location)]
    [InlineData(7, KeeperSpellRefusal.Location)]
    [InlineData(8, KeeperSpellRefusal.Location)]
    [InlineData(9, KeeperSpellRefusal.UnsupportedFlags)]
    public void RefusalChangesNeitherManaClocksNorShots(int change, KeeperSpellRefusal expected)
    {
        var (db, world, target) = Setup();
        byte spell = 6; int id = target.Id; ushort flags = 0;
        switch (change)
        {
            case 0: spell = 4; break;
            case 2: world.Player(Keeper).Mana = 5999; break;
            case 3: id = 999; break;
            case 4: target.OwnerPlayerId = Keeper; break;
            case 5: target.OwnerPlayerId = PlayerIds.Neutral; break;
            case 6: world.Tile(target.Tile).OwnerPlayerId = Rival; break;
            case 7: world.Tile(target.Tile).OwnerPlayerId = PlayerIds.Neutral; break;
            case 8: world.Tile(target.Tile).TerrainId = Dk2TerrainFixture.Rock; break;
            case 9: flags = 4; break;
        }
        int mana = world.Player(Keeper).Mana;
        var row = Assert.Single(world.Player(Keeper).Availabilities);
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, spell, id, flags)));
        Assert.Equal(expected, boundary.Refusal);
        Assert.Equal(0, boundary.Result);
        Assert.Empty(world.Shots);
        Assert.Equal(mana, world.Player(Keeper).Mana);
        Assert.Equal(0, world.Player(Keeper).OneShotManaSpent);
        Assert.Equal(row, Assert.Single(world.Player(Keeper).Availabilities));
    }

    /// <summary>Neither action 55 nor <c>0x004BBF90</c> reads availability; the interface gates
    /// it, as for Create Imp (ADR-0247). A merely researchable Thunderbolt still casts, so a
    /// replayed cast is never refused for research our tree finished later than DK2's.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AThunderboltNotYetAvailableStillCasts_ForTheInterfaceGatesAvailability(int value)
    {
        var (db, world, target) = Setup();
        world.SetAvailability(Keeper, AvailabilityKind.Spell, 6, value);
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(
            InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        Assert.Single(world.Shots);
    }

    /// <summary>With no spell entry there is nowhere to stamp the clocks or read Upgraded from,
    /// so the cast is refused: ours, not a read of DK2.</summary>
    [Fact]
    public void ASpellWithNoEntryInThePlayersTableIsRefused_OursNotARead()
    {
        var (db, world, target) = Setup();
        world.Player(Keeper).Availabilities.Clear();
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(
            InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(KeeperSpellRefusal.NoSpellEntry, boundary.Refusal);
        Assert.Empty(world.Shots);
    }

    [Theory]
    [InlineData(255, 300)]
    [InlineData(256, 300)]
    [InlineData(257, 300)]
    [InlineData(1, 150)]
    public void BinaryPrediction_StunStatesDoubleDamageIncludingGettingUp(int state, int damage)
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = state;
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(1500 - damage, target.Health);
    }

    [Fact]
    public void BinaryPrediction_State209AlsoContributesTheStunnedMultiplier()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = 209;
        Assert.Equal(256, StatusEffectRules.DamageMultiplier(world, db, target));
    }

    [Fact]
    public void WaterDoublesArmourHalvesAndInvulnerabilityDoesNotBlockLightning()
    {
        var (db, world, target) = Setup();
        world.Tile(target.Tile).TerrainId = Dk2TerrainFixture.Water;
        target.ArmourTicks = 5;
        target.InvulnerableTicks = 9;
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(1350, target.Health);
    }

    [Fact]
    public void BinaryPrediction_DyingTargetConsumesManaButCannotBeFinished()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = CreatureDeathRules.DyingState;
        target.Health = 0;
        target.DyingTicks = 19;
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(6000, world.Player(Keeper).Mana);
        Assert.Equal((0, 19, 217), (target.Health, target.DyingTicks, target.Dk2StateId));
    }

    [Fact]
    public void AlliedCreatureOnAlliedLandIsAValidForeignTargetWithoutFogOrRangeGate()
    {
        var (db, world, target) = Setup();
        world.SetAllied(Keeper, Rival, true);
        world.Tile(target.Tile).OwnerPlayerId = Rival;
        world.Tile(target.Tile).DiscoveredBy = PlayerMask.None;
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
    }

    [Fact]
    public void SyntheticCooldownConvertsRawSecondsAndEqualityIsReady()
    {
        var (db, world, target) = Setup(Spell() with { CooldownRaw = Fp32.One });
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 1, [boundary]);
        var command = InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id));
        sim.Run(1);
        sim.Tick(command);
        sim.Tick(command);
        Assert.Equal(KeeperSpellRefusal.Cooldown, boundary.Refusal);
        sim.Run(2);
        sim.Tick(command);
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        Assert.Equal(0, world.Player(Keeper).Mana);
    }

    [Fact]
    public void SyntheticSelectorsFailClosedAndUpgradedShotIdComesFromTheDefinition()
    {
        foreach (KeeperSpellDef spell in new[] { Spell() with { LocationSelector = 9 }, Spell() with { TargetSelector = 9 } })
        {
            var (db, world, target) = Setup(spell);
            var boundary = new Boundary();
            new GameSimulation(db, world, 1, [boundary]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
            Assert.Equal(spell.LocationSelector == 9 ? KeeperSpellRefusal.UnsupportedLocationSelector
                : KeeperSpellRefusal.UnsupportedTargetSelector, boundary.Refusal);
            Assert.Empty(world.Shots);
        }
        var (bonusDb, bonusWorld, bonusTarget) = Setup(Spell() with { BonusShotTypeId = 71 });
        bonusWorld.SetUpgraded(Keeper, AvailabilityKind.Spell, 6, true);
        new GameSimulation(bonusDb, bonusWorld, 1, [new KeeperSpellSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, bonusTarget.Id)));
        Assert.Equal((71, 200), ((int)Assert.Single(bonusWorld.Shots).ShotTypeId, Assert.Single(bonusWorld.Shots).Data1));
    }

    [Fact]
    public void PendingCastAndBothClocksSurviveSaveLoadAndAffectTheHash()
    {
        var (db, world, target) = Setup();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem()]);
        sim.Run(3);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new ShotSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal((6000, 6000), (copy.World.Player(Keeper).Mana, copy.World.Player(Keeper).OneShotManaSpent));
        var row = Assert.Single(copy.World.Player(Keeper).Availabilities);
        Assert.Equal((3L, 3L), (row.LastCastTick, row.LastCheckedTick));
        foreach (var changed in new[] { row with { LastCastTick = 2 }, row with { LastCheckedTick = 2 } })
        {
            copy.World.Player(Keeper).Availabilities[0] = changed;
            Assert.NotEqual(sim.StateHash(), copy.StateHash());
        }
        copy.World.Player(Keeper).Availabilities[0] = row;
        copy.World.Player(Keeper).OneShotManaSpent++;
        Assert.NotEqual(sim.StateHash(), copy.StateHash());
        copy.World.Player(Keeper).OneShotManaSpent--;
        copy.Tick(InputFrame.Empty);
        Assert.Equal(1350, copy.World.CreatureById(target.Id)!.Health);
    }

    [Theory]
    [InlineData(RoomTypeId.Portal, false)]
    [InlineData(RoomTypeId.DungeonHeart, false)]
    [InlineData(RoomTypeId.Prison, false)]
    [InlineData(RoomTypeId.Portal, true)]
    [InlineData(RoomTypeId.DungeonHeart, true)]
    [InlineData(RoomTypeId.Prison, true)]
    public void LocationOneExcludesSpecialRoomInteriorsButAllowsTheirRims(RoomTypeId kind, bool rim)
    {
        var (db, world, target) = Setup();
        byte terrain = db.Rooms.Values.Single(r => r.TypeId == kind).TerrainId;
        for (int y = 1; y < 19; y++)
            for (int x = 1; x < 19; x++)
            {
                world.Tile(x, y).TerrainId = x >= 8 && x <= 12 && y >= 8 && y <= 12
                    ? terrain : Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        if (rim) { target.PosX = 8 * Fp32.One + Fp32.One / 2; target.PosY = target.PosX; }
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(rim ? KeeperSpellRefusal.None : KeeperSpellRefusal.Location, boundary.Refusal);
    }

    [Theory]
    [InlineData(RoomTypeId.Portal, KeeperSpellRefusal.Location)]
    [InlineData(RoomTypeId.Prison, KeeperSpellRefusal.None)]
    [InlineData(RoomTypeId.Temple, KeeperSpellRefusal.None)]
    [InlineData(RoomTypeId.CombatPit, KeeperSpellRefusal.None)]
    public void MissingDiagonalIsNotPoolInteriorButStillHasPortalCodeFour(RoomTypeId kind, KeeperSpellRefusal expected)
    {
        var (db, world, target) = Setup();
        byte terrain = db.Rooms.Values.Single(r => r.TypeId == kind).TerrainId;
        for (int y = 9; y <= 11; y++)
            for (int x = 9; x <= 11; x++)
            {
                world.Tile(x, y).TerrainId = x == 9 && y == 9 ? Dk2TerrainFixture.ClaimedPath : terrain;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        var boundary = new Boundary();
        new GameSimulation(db, world, 1, [boundary]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        Assert.Equal(expected, boundary.Refusal);
    }

    [Fact]
    public void TargetedLightningFollowsTheTagAndDamageDoesNotScaleWithLevel()
    {
        var (db, world, target) = Setup();
        target.Level = 5;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));
        target.PosX = 15 * Fp32.One;
        target.PosY = 15 * Fp32.One;
        new GameSimulation(db, world, 1, [new ShotSystem()]).Tick(InputFrame.Empty);
        Assert.Equal(1350, target.Health);
    }

    [Fact]
    public void SyntheticCooldownLastCastZeroRemainsTheNeverCastSentinel()
    {
        var (db, world, target) = Setup(Spell() with { CooldownRaw = 10 * Fp32.One });
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 1, [boundary]);
        var command = InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id));
        sim.Tick(command);
        sim.Tick(command);
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        Assert.Equal(0, world.Player(Keeper).Mana);
        Assert.Equal(2, world.Shots.Count);
    }

    private sealed class Boundary : ISimulationSystem
    {
        public string Name => "boundary_probe";
        public int Result = -1;
        public KeeperSpellRefusal Refusal;
        public void Execute(SimulationContext context)
        {
            foreach (var command in context.Input.All.OfType<CastSpellOnCreatureCommand>())
                Result = KeeperSpellRules.CastOnCreature(context, command, out Refusal);
        }
    }

    private static KeeperSpellDef Spell() => new()
    {
        Name = "Thunderbolt", KeeperSpellId = 6, ManaCost = 6000, ManaUpkeepPerSecond = 0,
        ShotTypeId = 21, ShotData1 = 150, BonusShotTypeId = 21, BonusShotData1 = 200,
        LocationSelector = 1, TargetSelector = 2
    };

    private static ShotDef Shot(byte id) => new(new byte[239])
    {
        Name = "Thunderbolt", ShotId = id, MeshResource = new("", 0, default, 0, 0, 0, 0),
        Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "", Speed = 2048,
        Radius = 819, Health = 8, Flags = 4, ShotProcessFlags = 0x40, ProcessType = 9, AttackCategory = 6
    };

    private static (GameDatabase, WorldState, CreatureState) Setup(KeeperSpellDef? spell = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, [spell ?? Spell()], objects: basis.Objects.Values, shots: [Shot(21), Shot(71)]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 12000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, 6, 3));
        world.Tile(10, 10).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(10, 10).OwnerPlayerId = Keeper;
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        return (db, world, target);
    }
}
