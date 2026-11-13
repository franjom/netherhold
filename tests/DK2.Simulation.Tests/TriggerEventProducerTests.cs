// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

/// <summary>Trigger events fired by rules no other test reads at tree level (OpenKeeper audit
/// 1000-1110, item 17): stunned 15, steals 13, sacked 63 and the owner's 61. Each tree counts its
/// event into a flag (add 1), so a second firing shows.</summary>
public sealed class TriggerEventProducerTests
{
    private const byte Keeper = 3;
    private const ushort PlayerRoot = 5, CreatureRoot = 7;
    private const byte CreatureFlag = 1, PlayerFlag = 2;

    /// <summary><c>0x00491830</c> fires 15 after SetState(255) (<c>0x00491862</c>), for the spell's
    /// stun and the landing's alike.</summary>
    [Fact]
    public void AStunFiresStunnedInTheCreaturesTree()
    {
        (GameSimulation sim, CreatureState goblin) = Simulation((byte)CreatureTriggerEvent.Stunned, playerEvent: null,
            context => Assert.True(StatusEffectRules.Stun(context, Goblin(context), Definition(context))));

        sim.Tick();

        Assert.Equal(255, goblin.Dk2StateId);
        Assert.Equal(1, sim.World.Triggers.Read(CreatureFlag));
    }

    [Fact]
    public void ALandingStunFiresStunnedToo()
    {
        (GameSimulation sim, _) = Simulation((byte)CreatureTriggerEvent.Stunned, playerEvent: null,
            context => Assert.True(StatusEffectRules.StunOnLanding(context, Goblin(context), Definition(context))));

        sim.Tick();

        Assert.Equal(1, sim.World.Triggers.Read(CreatureFlag));
    }

    /// <summary>A refused stun (a creature already down) fires nothing.</summary>
    [Fact]
    public void ARefusedStunFiresNothing()
    {
        (GameSimulation sim, _) = Simulation((byte)CreatureTriggerEvent.Stunned, playerEvent: null, context =>
        {
            CreatureState goblin = Goblin(context);
            Assert.True(CreatureDeathRules.Collapse(context.Hooks, goblin));
            Assert.False(StatusEffectRules.Stun(context, goblin, Definition(context)));
        });

        sim.Tick();

        Assert.Equal(0, sim.World.Triggers.Read(CreatureFlag));
    }

    /// <summary>State 165 fires 13 first (steal-gold.md §7), whether or not gold lies underfoot.</summary>
    [Fact]
    public void ReachingTheGoldFiresStealsInTheThiefsTree()
    {
        (GameSimulation sim, _) = Simulation((byte)CreatureTriggerEvent.Steals, playerEvent: null,
            context => StealGoldRules.AtTheGold(context, "Probe", Goblin(context)));

        sim.Tick();

        Assert.Equal(1, sim.World.Triggers.Read(CreatureFlag));
    }

    /// <summary>State 196's first act (<c>0x0045A0E1</c>, <c>0x0045A0ED</c>): 63 in the creature's
    /// tree and 61 in its owner's, before the removal; once for a creature that walked out.</summary>
    [Fact]
    public void ACreatureThatLeavesFiresSackedAndItsKeeperCreatureSackedOnce()
    {
        int goblinId = 0;
        (GameSimulation sim, _) = Simulation((byte)CreatureTriggerEvent.Sacked,
            (byte)PlayerTriggerEvent.CreatureSacked, context =>
            {
                CreatureState g = Goblin(context);
                goblinId = g.Id;
                LeavingRules.Gone(context, Definition(context), g, "Probe");
            });

        sim.Tick();

        Assert.Null(sim.World.CreatureById(goblinId));
        Assert.Equal(1, sim.World.Triggers.Read(CreatureFlag));
        Assert.Equal(1, sim.World.Triggers.Read(PlayerFlag));
    }

    private static CreatureState Goblin(SimulationContext context) => context.World.Creatures[0];

    private static CreatureDef Definition(SimulationContext context) =>
        context.Database.CreatureById(Goblin(context).CreatureTypeId);

    private static (GameSimulation Sim, CreatureState Goblin) Simulation(
        byte creatureEvent, byte? playerEvent, Action<SimulationContext> rule)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper]);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        goblin.TriggerId = CreatureRoot;
        Dk2TerrainFixture.SetKnownIdentities(world);

        var records = new List<TriggerRecord>
        {
            Condition(CreatureRoot, creatureEvent, child: 0xFFFE),
            Count(0xFFFE, CreatureFlag),
        };
        var playerRoots = new List<TriggerPlayerRoot>();
        if (playerEvent is byte e)
        {
            records.Add(Condition(PlayerRoot, e, child: 0xFFFF));
            records.Add(Count(0xFFFF, PlayerFlag));
            playerRoots.Add(new TriggerPlayerRoot(Keeper, PlayerRoot));
        }
        var tree = new TriggerTree(records, 0, playerRoots: playerRoots);
        return (new GameSimulation(db, world, seed: 7, [new Once(rule)], tree), goblin);
    }

    private static TriggerRecord Condition(ushort id, byte type, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 255,
            ChildTriggerId = child, NextTriggerId = 0, Parameters = new Dictionary<string, long>(),
        };

    /// <summary>Action 7, flag += 1 (mode 0x12: an immediate operand, added).</summary>
    private static TriggerRecord Count(ushort id, byte flag) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 7, RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["FlagId"] = flag, ["Flag"] = 0x12, ["Value"] = 1 },
        };

    private sealed class Once(Action<SimulationContext> rule) : ISimulationSystem
    {
        private bool _done;
        public string Name => "Probe";

        public void Execute(SimulationContext context)
        {
            if (_done) return;
            _done = true;
            rule(context);
        }
    }
}
