// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ImpTeleportTests
{
    [Theory]
    [InlineData(8, 6, 6, true)]
    [InlineData(8, 6, 1, true)]
    [InlineData(8, 1, 6, true)]
    [InlineData(8, 3, 3, false)]
    [InlineData(7, 6, 6, false)]
    public void TeleportRequiresLevelAndMoreThanTwoTiles(int level, int x, int y, bool teleport)
    {
        Run((context, imp) =>
        {
            imp.Level = level;
            context.Navigation.TryRoute(imp, new TilePos(x, y), MoveOptions.Loose);
            Begin(context, imp);
            Assert.Equal(teleport ? 51 : 6, imp.Dk2StateId);
        });
    }

    [Fact]
    public void TeleportActionSurvivesReplacingARunningAction()
    {
        Run((context, imp) =>
        {
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            imp.ActionUntilTick = context.Tick.Value + 100;
            Begin(context, imp);
            Assert.Equal(51, imp.Dk2StateId);
            Assert.Equal(0, imp.ActionUntilTick);
            Assert.Equal(ImpTeleportAction.Departure, imp.TeleportAction);
        });
    }

    [Fact]
    public void RechargingTeleportWalks()
    {
        Run((context, imp) =>
        {
            imp.SpellStamps[1] = context.Tick.Value;
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            Begin(context, imp);
            Assert.Equal(6, imp.Dk2StateId);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TeleportAtOrBeforeRechargeExpiryWalks(int ticksBeforeExpiry)
    {
        Run((context, imp) =>
        {
            CreatureDef def = context.Database.CreatureById(imp.CreatureTypeId);
            CreatureSpellDef spell = SpellRules.SpellOf(context.Database, def, 1)!;
            int recharge = CombatRules.Scaled(CombatRules.RechargeTicks(context.Database, imp,
                SpellRules.RechargeTicks(spell)), imp, def);
            imp.SpellStamps[1] = context.Tick.Value - recharge + ticksBeforeExpiry;
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            Begin(context, imp);
            Assert.Equal(6, imp.Dk2StateId);
        });
    }

    [Theory]
    [InlineData(41)]
    [InlineData(43)]
    [InlineData(45)]
    [InlineData(32)]
    [InlineData(35)]
    [InlineData(37)]
    public void CarryLegForbidsTeleport(int resume)
    {
        Run((context, imp) =>
        {
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            MethodInfo method = typeof(ImpTaskSystem).GetMethod("BeginTaskWalk", BindingFlags.Static | BindingFlags.NonPublic)!;
            method.Invoke(null, [context, imp, resume, true]);
            Assert.Equal(6, imp.Dk2StateId);
            Assert.Equal(CreatureState.NeverUsed, imp.SpellStamps[1]);
        });
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.WoodenDoorCrateObjectId)]
    [InlineData(Dk2TerrainFixture.AlarmCrateObjectId)]
    public void PickingUpACrateWalksTheCarryLeg(byte objectType)
    {
        Run((context, imp) =>
        {
            for (int y = 5; y <= 7; y++)
            {
                for (int x = 5; x <= 7; x++)
                {
                    ref TileState tile = ref context.World.Tile(x, y);
                    RoomRules.Place(context.Database, ref tile, context.Database.Room(RoomTypeId.Workshop), PlayerIds.FirstKeeper);
                }
            }
            Dk2TerrainFixture.FurnishRooms(context.Database, context.World);
            ObjectState crate = context.World.SpawnObject(context.Database, objectType, PlayerIds.Unowned,
                imp.Tile.Y * context.World.Width + imp.Tile.X);
            imp.TargetObjectId = crate.Id;
            CreatureStateMachine.BeginTravel(context.Hooks, imp, 6, ImpStates.PickingUpObject);
            var grid = new WorldPathGrid(context.Database, context.World, context.Database.CreatureById(imp.CreatureTypeId));
            typeof(ImpTaskSystem).GetMethod("UpdateFetchingObject", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(new ImpTaskSystem(), [context, imp, grid]);
            Assert.Equal(crate.Id, imp.CarriedObjectId);
            Assert.Equal(6, imp.Dk2StateId);
            Assert.Equal(CreatureState.NeverUsed, imp.SpellStamps[1]);
        });
    }

    [Fact]
    public void GoldStoreWalkMayTeleportWithGoldInHand()
    {
        Run((context, imp) =>
        {
            imp.GoldHeld = 100;
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.ExactSpot);
            typeof(ImpTaskSystem).GetMethod("ToTheStore", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [context, imp]);
            Assert.Equal(51, imp.Dk2StateId);
            Assert.Equal(ImpStates.AtTheStore, imp.UtilityNewState);
        });
    }

    [Fact]
    public void UnreachableTargetWalks()
    {
        Run((context, imp) =>
        {
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            context.World.Tile(new TilePos(6, 6)).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
            Begin(context, imp);
            Assert.Equal(6, imp.Dk2StateId);
        });
    }

    [Fact]
    public void ArrivalWaitsFourThenTwoTicksAndResumesSavedState()
    {
        int elapsed = 0;
        var tasks = new ImpTaskSystem();
        Run((context, imp) =>
        {
            if (elapsed == 0)
            {
                context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
                Begin(context, imp);
            }
            else
            {
                tasks.Update(context, imp);
            }
            Assert.Equal(elapsed < 4 ? 51 : elapsed < 6 ? 52 : 15, imp.Dk2StateId);
            Assert.Equal(elapsed < 4 ? new TilePos(1, 1) : new TilePos(6, 6), imp.Tile);
            elapsed++;
        }, ticks: 7);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void TeleportActionStillCompletesAfterStateAndRouteChange(int interruptAt)
    {
        int elapsed = 0;
        var tasks = new ImpTaskSystem();
        var sweep = new CreatureUpdateSystem(new CreatureMovementSystem());
        Run((context, imp) =>
        {
            if (elapsed == 0)
            {
                context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
                Begin(context, imp);
            }
            else
            {
                if (elapsed == interruptAt)
                {
                    CreatureStateMachine.TrySetState(context.Hooks, imp, CreatureStateMachine.Idle);
                    imp.StopMoving();
                }
                sweep.Execute(context);
                if (elapsed < interruptAt)
                {
                    tasks.Update(context, imp);
                }
            }
            if (elapsed == 6)
            {
                Assert.Equal(ImpTeleportAction.None, imp.TeleportAction);
                Assert.Equal(new TilePos(6, 6), imp.Tile);
                Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
            }
            elapsed++;
        }, ticks: 7);
    }

    [Theory]
    [InlineData(51)]
    [InlineData(52)]
    public void MovementDoesNotWalkDuringTeleport(int state)
    {
        Run((context, _) =>
        {
            CreatureState imp = context.World.SpawnCreature(context.Database, 1, PlayerIds.FirstKeeper, new TilePos(1, 1));
            context.Navigation.TryRoute(imp, new TilePos(6, 6), MoveOptions.Loose);
            imp.UtilityNewState = 15;
            CreatureStateMachine.TrySetState(context.Hooks, imp, state);
            new CreatureMovementSystem().Update(context, imp);
            Assert.Equal(new TilePos(1, 1), imp.Tile);
            Assert.Equal(CreatureState.CentreOf(1), imp.PosX);
            Assert.Equal(CreatureState.CentreOf(1), imp.PosY);
        });
    }

    private static void Begin(SimulationContext context, CreatureState imp, bool noTeleport = false)
    {
        MethodInfo method = typeof(ImpTaskSystem).GetMethod("BeginTaskWalk", BindingFlags.Static | BindingFlags.NonPublic)!;
        object[] args = method.GetParameters().Length == 3 ? [context, imp, 15] : [context, imp, 15, noTeleport];
        method.Invoke(null, args);
    }

    private static void Run(Action<SimulationContext, CreatureState> check, int ticks = 1)
    {
        CreatureDef definition = Dk2TerrainFixture.Imp() with
        {
            CreatureId = 50,
            Spells = [new CreatureSpellSlot(Dk2TerrainFixture.SpeedTestSpellId, 4, new Fp32(Fp32.One), 1),
                new CreatureSpellSlot(Dk2TerrainFixture.TeleportTestSpellId, 8, new Fp32(Fp32.One), 1)]
        };
        GameDatabase db = Dk2TerrainFixture.Database(definition);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.ClaimedPath;
            }
        }
        CreatureState imp = world.SpawnCreature(db, definition.CreatureId, PlayerIds.FirstKeeper, new TilePos(1, 1));
        imp.Level = 8;
        new GameSimulation(db, world, 1, [new Probe(context => check(context, imp))]).Run(ticks);
    }

    private sealed class Probe : ISimulationSystem
    {
        private readonly Action<SimulationContext> _action;

        public Probe(Action<SimulationContext> action)
        {
            _action = action;
        }

        public string Name { get { return "TeleportTest"; } }
        public void Execute(SimulationContext context) { _action(context); }
    }
}

