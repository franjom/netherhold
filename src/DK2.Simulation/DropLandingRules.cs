// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A creature put down by the hand (hand-drop-landing.md, ADR-0226). The keeper's drop
/// (<c>0x004BC710</c>) sets 76, whose handler <c>0x0046EAB0</c> stuns an own non-worker that no
/// room takes over (<c>0x00491830(255)</c>): it lies for <c>StunDuration</c>, gets up in 257
/// (<c>0x00481200</c>), and 257 asks the room underfoot (<c>0x004E8DA0(0)</c>): the room's drop
/// state, or with none, 250.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/hand-drop-landing.md")]
public static class DropLandingRules
{
    /// <summary>State 76, the landing (<c>0x0046EAB0</c>).</summary>
    public const int LandingState = 76;

    /// <summary>State 250 (<c>0x00481CF0</c>), "guarding": plays action 43 until 50 turns past the
    /// state word <c>+0x7E</c>, then idle. The word is <c>creature+0x2CC</c>, which the room drop
    /// stamps with now on its way in, so the stand is 50 turns from getting up.</summary>
    public const int StandingState = 250;

    /// <summary>250's length past <c>+0x7E</c>, in turns (<c>0x32</c>).</summary>
    public const int StandTicks = 50;

    /// <summary>257's hold (<c>0x00481200</c>, <c>0x00496D00</c>): the ticks action 53 plays the
    /// creature's Getup animation (block 27, <c>0x006E47AD</c>), its frames at its rate in four-tick
    /// seconds, rounded up, plus one tick, the tick the handler sees the action end, as for the
    /// Entrance animation. STRONGLY INFERRED: the formula is fitted, and reproduces every 257 run the
    /// Level 1–3 captures measured: Warlock 8, Goblin 11, Troll 5, Firefly 13, Dwarf 4, Lord of the
    /// Land 4 (Q527, ADR-0348). A creature with no Getup art waits one tick.</summary>
    public static int GetUpTicks(CreatureDef definition) =>
        (definition.GetUpFrames <= 0 || definition.GetUpFramesPerSecond <= 0
            ? 0
            : ((definition.GetUpFrames * SimulationTiming.OriginalTicksPerGameSecond)
               + definition.GetUpFramesPerSecond - 1) / definition.GetUpFramesPerSecond)
        + 1;

    /// <summary>What <c>0x004E8DA0(0)</c> leaves in <c>FightProcessCounter</c> on its way to 250:
    /// bits 22 and 24 of the flag dword cleared, 23 and 25 set.</summary>
    public const byte StandFightProcessCounter = 10;

    /// <summary>A room record's drop state, <c>+0x19</c> (portals-room-handler-table.md); null for
    /// a room with none.</summary>
    public static int? RoomDropState(RoomTypeId type) => type switch
    {
        RoomTypeId.Treasury => 188,
        RoomTypeId.Lair => 79,
        RoomTypeId.Portal => 197,
        RoomTypeId.Hatchery => 92,
        RoomTypeId.DungeonHeart => 71,
        RoomTypeId.Library => 53,
        RoomTypeId.TrainingRoom => 108,
        RoomTypeId.GuardRoom => 97,
        RoomTypeId.Workshop => 63,
        RoomTypeId.Prison => 114,
        RoomTypeId.TortureChamber => 130,
        RoomTypeId.Temple => 101,
        RoomTypeId.Casino => 145,
        RoomTypeId.CombatPit => 137,
        (RoomTypeId)25 => 197,
        (RoomTypeId)26 => 274,
        _ => null,
    };

    /// <summary>257's tail, <c>0x004E8DA0(0)</c>. On a room with a drop state: the job rooms' state
    /// through 1, with the job remembered (<see cref="JobRules.TryDropOnARoom"/>), and the Combat
    /// Pit's 137 through 1 (ADR-0227); the other rooms' states are not built here and idle. With none, a keeper's creature goes to 250, its
    /// <c>FightProcessCounter</c> at 10; a hero or a neutral creature idles, and so does a
    /// keeper's while its Call To Arms is on (ADR-0279).</summary>
    public static void AfterGettingUp(SimulationContext context, string system, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, here);
        if (room?.Definition.TypeId is RoomTypeId type && RoomDropState(type) is not null)
        {
            // The Combat Pit's drop state, 137: through 1, and on the rim 138 then makes a spectator.
            if (type == RoomTypeId.CombatPit)
            {
                StateShadow.ToIdle(context, system, creature);
                if (CombatPitSystem.Move(context, creature, CombatPitRules.ArrivingState))
                    context.Emit(system, "dropped_to_job", $"creature={creature.Id} job=CombatPit at=({creature.Tile.X},{creature.Tile.Y})");
                return;
            }

            // The Heart's drop state, 71 (0x00459180), through 1 (KickToState(1), KickToState(71)).
            if (type == RoomTypeId.DungeonHeart)
            {
                StateShadow.ToIdle(context, system, creature);
                if (StateShadow.TryMove(context, system, creature, HeartDropState)) HeartDrop(context, system, creature, definition, room!);
                return;
            }

            if (JobRules.TryDropOnARoom(context.Hooks, creature, definition, here) is { } job)
            {
                context.Emit(system, "dropped_to_job", $"creature={creature.Id} job={job} at=({creature.Tile.X},{creature.Tile.Y})");
                return;
            }
            StateShadow.ToIdle(context, system, creature);
            return;
        }
        if (creature.OwnerPlayerId is PlayerIds.Good or PlayerIds.Neutral)
        {
            StateShadow.ToIdle(context, system, creature);
            return;
        }
        // 0x004E8DA0 writes now into creature+0x2CC, the state record's +0x7E, before its Call to
        // Arms test and KickToState(250).
        creature.StateDeadlineTick = context.Tick.Value;
        // With the owner's Call to Arms on the creature idles instead, and idle walks to the flag.
        if (CallToArmsRules.IsOn(world, creature.OwnerPlayerId))
        {
            StateShadow.ToIdle(context, system, creature);
            return;
        }
        creature.FightProcessCounter = StandFightProcessCounter;
        if (!StateShadow.TryMove(context, system, creature, StandingState))
        {
            StateShadow.ToIdle(context, system, creature);
            return;
        }
        context.Emit(system, "standing", $"creature={creature.Id} until={creature.StateDeadlineTick + StandTicks}");
    }

    /// <summary>Whether the creature stands on a Dungeon Heart's tile.</summary>
    public static bool OnAHeart(WorldState world, GameDatabase database, CreatureState creature) =>
        world.Contains(creature.Tile)
        && RoomIndex.RoomAt(world, database, (creature.Tile.Y * world.Width) + creature.Tile.X)?.Definition.TypeId == RoomTypeId.DungeonHeart;

    /// <summary>The Dungeon Heart's drop state (<c>0x00459180</c>, refused-states.md §3).</summary>
    public const int HeartDropState = 71;

    /// <summary>The worker states 71 sends to: 11 off the Heart's inner 3×3 or with no Heart object.</summary>
    private const int WorkerAfterDropState = ImpStates.AfterDrop;

    /// <summary>71's handler, run in the turn it is entered (ours; DK2 runs it the next): a non-worker
    /// goes to 188 for its wages; a worker off the inner 3×3 (<c>0x004DA150</c>), or with no Heart object,
    /// to 11; a worker on it is given back, the tile owner credited Create Imp's mana cost
    /// (<c>Player_AddMana</c>, capped at the threshold), its gold lost, and it is gone (as state 196).</summary>
    private static void HeartDrop(SimulationContext context, string system, CreatureState creature, CreatureDef definition, RoomInstance heart)
    {
        WorldState world = context.World;
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker))
        {
            if (!StateShadow.TryMove(context, system, creature, PaydayRules.ArrivingState)) StateShadow.ToIdle(context, system, creature);
            return;
        }

        int dx = creature.Tile.X - heart.MinX, dy = creature.Tile.Y - heart.MinY;
        bool inner = dx is >= 1 and <= 3 && dy is >= 1 and <= 3;
        PlayerId owner = world.TileAtIndex((creature.Tile.Y * world.Width) + creature.Tile.X).OwnerPlayerId;
        bool heartObject = world.HasPlayer(heart.OwnerPlayerId) && !world.Player(heart.OwnerPlayerId).HeartRuined;
        if (!inner || !heartObject)
        {
            if (!StateShadow.TryMove(context, system, creature, WorkerAfterDropState)) StateShadow.ToIdle(context, system, creature);
            return;
        }

        int refund = context.Database.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaCost;
        if (world.HasPlayer(owner))
        {
            ref PlayerState player = ref world.Player(owner);
            player.Mana = Math.Min(player.Mana + refund, ManaRules.Threshold(context.Database));
        }
        context.Emit(system, "worker_given_back",
            $"creature={creature.Id} player={owner} mana={refund} gold_lost={creature.GoldHeld} at=({creature.Tile.X},{creature.Tile.Y})");
        creature.GoldHeld = 0;
        world.RemoveCreature(creature.Id);
    }

    /// <summary>State 250: true while it stands (<c>now &lt; +0x7E + 50</c>).</summary>
    public static bool Stands(SimulationContext context, CreatureState creature) =>
        context.Tick.Value < creature.StateDeadlineTick + StandTicks;
}
