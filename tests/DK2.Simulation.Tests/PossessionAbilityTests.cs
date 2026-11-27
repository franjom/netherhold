// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
namespace DK2.Simulation.Tests;
public sealed class PossessionAbilityTests
{
    [Theory]
    [InlineData("horny")]
    [InlineData("only-horny")]
    [InlineData("dying")]
    [InlineData("carried")]
    [InlineData("hand")]
    public void RefusedNativeDamageDoesNotRequestAColourFlash(string refusal)
    {
        (GameDatabase? db, WorldState _, CreatureState? me) = Open();
        CreatureDef definition = db.CreatureById(me.CreatureTypeId);
        if (refusal == "horny")
        {
            definition = definition with { Flags = definition.Flags | CreatureFlags.IsHorny };
        }

        if (refusal == "only-horny")
        {
            definition = definition with { Flags = definition.Flags | CreatureFlags.OnlyAttackableByHorny };
        }

        if (refusal == "dying")
        {
            me.Dk2StateId = CreatureDeathRules.DyingState;
        }

        if (refusal == "carried")
        {
            me.CarriedByCreatureId = 999;
        }

        if (refusal == "hand")
        {
            me.InHand = true;
        }

        int notifications = 0;
        me.PossessionColourChanged += _ => notifications++;

        CreatureHealthRules.Apply(db, definition, me, -1, 4);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void FearRequestsItsInitial239AndTick336Effects()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        CreatureState enemy = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(6, 5));
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), new CreatureStatusSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Tick(InputFrame.Of(new FrightenPossessedCreatureCommand(3, new CreatureId(enemy.Id), new CreatureId(me.Id), new FearAmount(2000))));

        Assert.Contains(events.Events, e => e.EffectRequest is { EffectId.Value: 239 });
        Assert.Contains(events.Events, e => e.EffectRequest is { EffectId.Value: 336 });
    }

    [Fact]
    public void PickLockOnAPrisonOpensItAndReleasesEverySidesPrisoners()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        for (int y = 3; y <= 7; y++)
        {
            for (int x = 3; x <= 7; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), 4);
            }
        }

        CreatureState captive = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(6, 5));
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, 4, 5 * world.Width + 6));
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        var open = new OpenPossessedPrisonCommand(3, me.Tile);
        me.PosX = CreatureState.CentreOf(9);
        me.PosY = CreatureState.CentreOf(9);
        world.Player(3).PossessedCreatureId = 0;
        sim.Tick(InputFrame.Of(open));
        RoomInstance prison = RoomIndex.RoomAt(world, db, open.PrisonAt.Y * world.Width + open.PrisonAt.X)!;
        Assert.Equal(JailBreakRules.Activated | JailBreakRules.Open | JailBreakRules.Broken,
            JailBreakRules.DoorOf(world, prison));
        Assert.Equal(JailBreakRules.EscapingState, captive.UtilityNewState);
    }
    [Fact]
    public void FearSpecialTicksTwentyTimesAndIncludesTheExpiryTurn()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        CreatureState enemy = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(6, 5));
        var aim = new PossessionAim(new FpPosition3(new Fp32(me.PosX), new Fp32(me.PosY), new Fp32(me.PosZ + Fp32.One)), new Dk2Angle(0x200), new Dk2Angle(0x200));
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), new CreatureStatusSystem()]);
        CreatureId target = PossessionAbilityRules.PickFrightenTarget(world, db, me, aim);
        Assert.Equal(new CreatureId(enemy.Id), target);
        sim.Tick(InputFrame.Of(new FrightenPossessedCreatureCommand(3, target, new CreatureId(me.Id), new FearAmount(2000))));
        Assert.Equal(19, enemy.PossessionFearTicks);
        Assert.Equal(2000, enemy.TempFear);
        Assert.Equal(me.Id, enemy.PossessionFearSource.Value);
        for (int i = 0; i < 19; i++)
        {
            sim.Tick();
        }

        Assert.Equal(0, enemy.PossessionFearTicks);
        Assert.Equal(40000, enemy.TempFear);
        sim.Tick();
        Assert.Equal(40000, enemy.TempFear);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void PrayerPollsEveryTurnCapsAtTheLevelMaximumAndStopsWhenToggledOff(int level)
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        me.Level = level;
        me.PossessionPraying = true;
        int maximum = CreatureHealthRules.MaxHealth(db, db.CreatureById(me.CreatureTypeId), me);
        me.Health = maximum - 10;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem()]);
        sim.Tick();
        Assert.Equal(maximum - 2, me.Health);
        Assert.True(me.PossessionPrayerAction);
        sim.Tick();
        Assert.Equal(maximum, me.Health);
        sim.Tick();
        Assert.Equal(maximum, me.Health);
        me.Health = maximum - 5;
        me.PossessionPraying = false;
        sim.Tick();
        Assert.Equal(maximum - 5, me.Health);
        Assert.False(me.PossessionPrayerAction);
    }
    [Theory]
    [InlineData(33, 9)]
    [InlineData(65569, 9)]
    [InlineData(-65503, 9)]
    [InlineData(65536, 1)]
    [InlineData(-65536, 1)]
    [InlineData(-1, 1500)]
    public void PrayerReadsTheUnsignedLowWordBeforeDividingTheConfiguredRate(int rate, int expectedHealth)
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        VariableTypeId variable = VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond;
        db.Variables.Misc[variable] = new MiscVariable(variable, rate, 0, 0);
        me.Health = 1;
        me.PossessionPraying = true;
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem()]);
        sim.Tick();
        Assert.Equal(expectedHealth, me.Health);
    }
    [Fact]
    public void FormTogglesFlightAndLeavingClearsBothAbilityBits()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem()]);
        sim.Tick(InputFrame.Of(new TogglePossessionFormCommand(3)));
        Assert.True(me.PossessionFlightForm);
        Assert.True(WaterSpeedRules.IsFlying(db.CreatureById(me.CreatureTypeId), me));
        sim.Tick(InputFrame.Of(new TogglePossessionPrayerCommand(3)));
        Assert.True(me.PossessionPraying);
        sim.Tick(InputFrame.Of(new LeavePossessionCommand(3)));
        Assert.False(me.PossessionFlightForm);
        Assert.False(me.PossessionPraying);
    }
    [Fact]
    public void AbilityBitsSurviveSnapshotAndAffectHash()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        me.PossessionPraying = true;
        Assert.NotEqual(before, sim.StateHash());
        me.PossessionFlightForm = true;
        me.PossessionFearTicks = 5;
        me.PossessionFearAmount = 2000;
        me.PossessionFearSource = new CreatureId(me.Id);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.True(copy.World.CreatureById(me.Id)!.PossessionPraying);
        Assert.True(copy.World.CreatureById(me.Id)!.PossessionFlightForm);
        Assert.Equal(5, copy.World.CreatureById(me.Id)!.PossessionFearTicks);
        Assert.Equal(new CreatureId(me.Id), copy.World.CreatureById(me.Id)!.PossessionFearSource);
    }
    private static (GameDatabase, WorldState, CreatureState) Open()
    {
        CreatureDef form = Dk2TerrainFixture.Goblin() with
        {
            CreatureId = 52,
            PrimaryAbility = PossessionAbilityKind.ToggleFlag11,
        };
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor(), form);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(12, 12), [0, 1, 2, 3, 4]);
        CreatureState me = world.SpawnArrived(db, form.CreatureId, 3, new TilePos(5, 5));
        me.Possessed = true;
        world.Player(3).PossessedCreatureId = me.Id;
        return (db, world, me);
    }
}
