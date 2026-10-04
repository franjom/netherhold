// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Sight of Evil as a simulation cast with an empty effect (ADR-0352): the position
/// permission <c>0x004BB8A0</c> (location 5, target 4), then <c>0x004BBD30</c>'s payment and both
/// clocks. Its shot's process 25 only draws the local player's region (keeper-spell-sight.md §2).</summary>
public sealed class SightOfEvilCastTests
{
    private const byte Keeper = 3, Rival = 4;
    private static readonly TilePos At = new(10, 10);

    private static KeeperSpellDef Spell() => new()
    {
        Name = "Sight Of Evil", KeeperSpellId = KeeperSpellRules.SightOfEvil, ManaCost = 5000,
        ManaUpkeepPerSecond = 0, ShotTypeId = 33, ShotData1 = 5, BonusShotTypeId = 33, BonusShotData1 = 7,
        LocationSelector = 5, TargetSelector = 4, Flags = KeeperSpellFlags.ExcludedFromBattle,
    };

    private static ShotDef SightShot() => new(new byte[239])
    {
        Name = "Sight Of Evil", MeshResource = new KwdArtResource("", 0, default, 0, 0, 0, 0), Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0),
        SoundCategory = "", ShotId = 33, Speed = 2048, Radius = 819, Health = 64, Flags = 0x4,
        ShotProcessFlags = 0x42, CollideType = 0, ProcessType = 25, DamageType = 0, AttackCategory = 0, DeathShotId = 0,
    };

    private static (GameDatabase Db, WorldState World) Setup(bool withShot = true)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, [Spell()], objects: basis.Objects.Values,
            shots: withShot ? basis.Shots.Values.Append(SightShot()) : basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 45000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, KeeperSpellRules.SightOfEvil, 3));
        return (db, world);
    }

    private static CastSpellAtPlaceCommand Cast() =>
        new(Keeper, KeeperSpellRules.SightOfEvil, CreatureState.CentreOf(At.X), CreatureState.CentreOf(At.Y));

    private static ISimulationSystem[] Systems() => [new KeeperSpellSystem(), new ShotSystem()];

    [Fact]
    public void ACastOnRockPaysOnceStampsBothClocksAndMakesNothing_StronglyInferredFromBinary()
    {
        // Location 5 admits anything, so a rival's rock or a solid tile is a place.
        var (db, world) = Setup();
        world.Tile(At).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(At).OwnerPlayerId = Rival;
        var sim = new GameSimulation(db, world, 1, Systems());
        sim.Run(7);

        sim.Tick(InputFrame.Of(Cast()));

        Assert.Equal((40000, 5000), (world.Player(Keeper).Mana, world.Player(Keeper).OneShotManaSpent));
        AvailabilityState row = Assert.Single(world.Player(Keeper).Availabilities);
        Assert.Equal((7L, 7L), (row.LastCastTick, row.LastCheckedTick));
        Assert.Empty(world.Shots);
        Assert.Empty(world.Objects);
    }

    [Theory]
    [InlineData(0, KeeperSpellRefusal.Mana)]
    [InlineData(1, KeeperSpellRefusal.NoSpellEntry)]
    [InlineData(2, KeeperSpellRefusal.UnsupportedFlags)]
    [InlineData(3, KeeperSpellRefusal.MissingShot)]
    public void ARefusalPaysAndStampsNothing(int change, KeeperSpellRefusal reason)
    {
        var (db, world) = Setup(withShot: change != 3);
        CastSpellAtPlaceCommand cast = Cast();
        if (change == 0) world.Player(Keeper).Mana = 4999;
        if (change == 1) world.Player(Keeper).Availabilities.Clear();
        if (change == 2) cast = cast with { Flags = 1 };
        KeeperSpellRefusal seen = KeeperSpellRefusal.None;

        new GameSimulation(db, world, 1, [new CallbackSystem(context => seen = KeeperSpellRules.CanCastAtPlace(context, cast)),
            .. Systems()]).Tick(InputFrame.Of(cast));

        Assert.Equal(reason, seen);
        Assert.Equal(change == 0 ? 4999 : 45000, world.Player(Keeper).Mana);
        Assert.All(world.Player(Keeper).Availabilities, r => Assert.Equal(0L, r.LastCastTick));
    }

    [Fact]
    public void EqualManaIsEnough()
    {
        var (db, world) = Setup();
        world.Player(Keeper).Mana = 5000;
        new GameSimulation(db, world, 1, Systems()).Tick(InputFrame.Of(Cast()));

        Assert.Equal(0, world.Player(Keeper).Mana);
    }

    private sealed class CallbackSystem(Action<SimulationContext> execute) : ISimulationSystem
    {
        public string Name => "sight-test";
        public void Execute(SimulationContext context) => execute(context);
    }
}
