// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A Combat Pit's spectators (<c>combat-pit-spectators.md</c>, ADR-0227): job 26's handler
/// <c>0x00457780</c>, which sends a creature to watch a fight in one of its keeper's pits, and the
/// three Spectating states. 142 picks a spot on the rim and walks there, 143 turns to face the pit
/// and cheers, 144 waits out the cheer and picks again while the fight lasts. Four game seconds
/// after the last fight was seen the spectator goes idle. Job 26's only caller is state 89's
/// fallback (ADR-0228).</summary>
/// <remarks>Not built: the finder's messages <c>0x2A</c>–<c>0x2C</c>, the animation test before
/// the cheer (ours assumes every creature has one), and the reachability point
/// <c>0x004E5000</c> (ours tests the room's first standable tile).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit-spectators.md")]
public static class CombatPitSpectatorRules
{
    /// <summary>142 (<c>0x00457D60</c>): a spot on the rim, and the walk to it.</summary>
    public const int ChoosingState = 142;

    /// <summary>143 (<c>0x00457F40</c>): face the pit's centre, then the cheer.</summary>
    public const int FacingState = 143;

    /// <summary>144 (<c>0x004580D0</c>): wait out the cheer, then look again.</summary>
    public const int CheeringState = 144;

    /// <summary>Variable 19 × 4, four game seconds: how long after the last fight a spectator keeps
    /// watching.</summary>
    public const int QuietTicks = 4 * SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary>142 keeps its own position unless <c>random(100)</c> is at most this, or another
    /// creature shares its tile.</summary>
    public const int MoveChance = 35;

    /// <summary>The cheer's length, ours: actions 69 and 70 run for their animation, which is not
    /// read. A GUESS, like the imp's idle actions (<see cref="ImpTaskSystem.IdleActionGuessTicks"/>).</summary>
    public const int CheerGuessTicks = 8;

    public static bool IsSpectatorState(int state) => state is ChoosingState or FacingState or CheeringState;

    /// <summary><c>0x004D7CF0</c>: whether someone on the pit's floor is fighting, that is on the
    /// map in a state of category 22 (a walk counted by the state it resumes, <c>0x00478050</c>).
    /// Found, it stamps the pit's <c>+0x53</c> with now.</summary>
    public static bool FightOn(SimulationContext context, RoomInstance pit)
    {
        WorldState world = context.World;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (!CreatureStateMachine.HasState(c) || !CombatPitRules.IsOnTheMap(c)) continue;
            if (!CombatPitRules.IsFloor(pit, (c.Tile.Y * world.Width) + c.Tile.X)) continue;
            int state = StateShadow.IsTravelling(c) ? c.UtilityNewState : c.Dk2StateId;
            if (CreatureStateTable.Get(state).Category != CreatureStateCategory.Combat) continue;
            world.TileAtIndex(pit.TileIndices[0]).PitFightSeenTick = context.Tick.Value;
            return true;
        }

        return false;
    }

    /// <summary>The turns since the pit last had a fight (<c>now − room+0x53</c>).</summary>
    private static long SinceTheFight(SimulationContext context, RoomInstance room) =>
        context.Tick.Value - context.World.TileAtIndex(room.TileIndices[0]).PitFightSeenTick;

    /// <summary>Job 26, <c>CombatPitSpectate</c> (<c>0x00457780</c>). On the rim of its own or an
    /// ally's Combat Pit with a fight on, or one seen less than <see cref="QuietTicks"/> ago: 142 where
    /// it stands. Otherwise the nearest of its keeper's pits with a fight on (<c>0x004D8510</c>),
    /// walked into resuming 142 (<c>0x00471A70</c>).</summary>
    public static bool TakeTheJob(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (CombatPitRules.PitAt(world, context.Database, here) is { } pit
            && (pit.OwnerPlayerId == creature.OwnerPlayerId || world.AreAllied(creature.OwnerPlayerId, pit.OwnerPlayerId))
            && !CombatPitRules.IsFloor(pit, here)
            && (FightOn(context, pit) || SinceTheFight(context, pit) < QuietTicks)
            && Enter(context, system, creature))
            return true;

        if (Finder(context, creature) is not { } room) return false;
        if (!StealGoldRules.ToRoom(context, system, creature, room, ChoosingState, Navigation.MoveOptions.Loose)) return false;
        if (creature.Dk2StateId == ChoosingState) Entered(context, creature);
        return true;
    }

    /// <summary><c>0x004D8510</c>, over the keeper's own rooms: each Combat Pit with a fight on,
    /// that the creature can reach (<c>0x00471740</c>) and that has a place for it
    /// (<c>0x004E53B0</c>). The nearest by the distance to its <c>FirstSlab</c> (<c>0x004D8510</c>,
    /// <see cref="SlabChain.FirstSlab"/>), preferring one whose
    /// occupants include nobody of the other side (<c>0x0045C640</c>).</summary>
    private static RoomInstance? Finder(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        RoomInstance? nearest = null, nearestFriendly = null;
        uint best = uint.MaxValue, bestFriendly = uint.MaxValue;
        List<Steering.Fixed16Vector>? scratch = null;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId || room.Definition.TypeId != RoomTypeId.CombatPit) continue;
            if (!FightOn(context, room)) continue;
            if (!Reachable(context, creature, definition, room, scratch ??= [])) continue;
            if (!RoomCapacityRules.HasRoom(world, database, room, creature.Id)) continue;

            int first = SlabChain.FirstSlab(world, room);
            long dx = Math.Abs((long)CreatureState.CentreOf(first % world.Width) - creature.PosX);
            long dy = Math.Abs((long)CreatureState.CentreOf(first / world.Width) - creature.PosY);
            uint distance = unchecked((uint)((dx * dx) >> 12) + (uint)((dy * dy) >> 12));
            if (distance < best)
            {
                nearest = room;
                best = distance;
            }
            if (distance < bestFriendly && !HasAnOpponent(world, database, room, definition))
            {
                nearestFriendly = room;
                bestFriendly = distance;
            }
        }

        return nearestFriendly ?? nearest;
    }

    private static bool Reachable(SimulationContext context, CreatureState creature, CreatureDef definition,
        RoomInstance room, List<Steering.Fixed16Vector> scratch)
    {
        var grid = new WorldPathGrid(context.Database, context.World, definition);
        int w = context.World.Width;
        foreach (int tile in room.TileIndices)
        {
            if (!grid.IsWalkable(tile % w, tile / w)) continue;
            return CombatTargetRules.CanReach(context.Navigation, creature, definition,
                CreatureState.CentreOf(tile % w), CreatureState.CentreOf(tile / w), int.MaxValue, scratch);
        }

        return false;
    }

    /// <summary><c>0x0045C640</c> over the room's occupants: a good creature objects to an evil
    /// one, anyone else to a good one.</summary>
    private static bool HasAnOpponent(WorldState world, GameDatabase database, RoomInstance room, CreatureDef seeker)
    {
        CreatureFlags against = seeker.Flags.HasFlag(CreatureFlags.IsGood) ? CreatureFlags.IsEvil : CreatureFlags.IsGood;
        foreach (CreatureState c in world.Creatures)
        {
            if (!CreatureStateMachine.HasState(c) || HandRules.IsHeld(c)) continue;
            if (!RoomCapacityRules.Claims(CreatureStateTable.Get(c.Dk2StateId).Category)) continue;
            if (!room.Contains((c.Tile.Y * world.Width) + c.Tile.X)) continue;
            if (database.CreatureById(c.CreatureTypeId).Flags.HasFlag(against)) return true;
        }

        return false;
    }

    /// <summary>138 on the rim of the keeper's own pit (<c>0x004539B0(16, owner, 0)</c>): 142. The
    /// gate refuses it, since Spectating's entry mask lacks the Combat Pit's bit, and 138 then
    /// returns 0, as DK2's does (<c>combat-pit-spectators.md</c> §1).</summary>
    public static bool FromPlacing(SimulationContext context, string system, CreatureState creature, RoomInstance pit)
    {
        if (pit.OwnerPlayerId != creature.OwnerPlayerId) return false;
        if (!Enter(context, system, creature)) return false;
        creature.CurrentJob = JobType.CombatPitSpectate;
        creature.JobTileIndex = (creature.Tile.Y * context.World.Width) + creature.Tile.X;
        return true;
    }

    private static bool Enter(SimulationContext context, string system, CreatureState creature)
    {
        if (!StateShadow.TryMove(context, system, creature, ChoosingState)) return false;
        Entered(context, creature);
        return true;
    }

    /// <summary>The Spectating enter hook <c>0x0047A9E0</c> on a Combat Pit tile clears
    /// <c>CombatPitFighter</c>; and the state's <c>Age</c> starts.</summary>
    private static void Entered(SimulationContext context, CreatureState creature)
    {
        creature.PitStateEnteredTick = context.Tick.Value;
        WorldState world = context.World;
        if (CombatPitRules.PitAt(world, context.Database, (creature.Tile.Y * world.Width) + creature.Tile.X) != null)
            creature.CombatPitFighter = false;
    }

    /// <summary>One original turn of a spectator. False when it drops to idle, which ends the job.
    /// A state entered this turn runs from the next, as DK2's dispatcher does.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            StateShadow.Arrive(context, system, creature);
            creature.PitStateEnteredTick = context.Tick.Value;
            return true;
        }

        if (creature.PitStateEnteredTick == context.Tick.Value) return true;
        return creature.Dk2StateId switch
        {
            ChoosingState => Choose(context, system, creature),
            FacingState => Face(context, system, creature),
            CheeringState => Cheer(context, system, creature),
            _ => false,
        };
    }

    /// <summary>142: with another creature on its tile, a free rim tile next to it (eight ways
    /// round from the floor, four from the rim); otherwise the same, but only when
    /// <c>random(100)</c> is at most <see cref="MoveChance"/>, and its own position when not. None
    /// found, its own position. A random point in the middle third of the tile, each axis
    /// <c>random(0x555) + 0x555</c>, and a walk there resuming 143. A walk refused: 1, then job 26
    /// again.</summary>
    private static bool Choose(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? pit = RoomIndex.RoomAt(world, context.Database, here);
        int count = CombatPitRules.IsFloor(CombatPitRules.PitAt(world, context.Database, here), here) ? 8 : 4;

        int x = creature.PosX, y = creature.PosY;
        bool crowded = CombatPitRules.AnyoneOn(world, creature.Tile, creature);
        if (crowded || context.Rng.NextInt(100) <= MoveChance)
        {
            int spot = pit is null ? -1 : CombatPitRules.NeighbourStep(context, pit, creature, count, rimOnly: true, empty: true);
            if (spot >= 0)
            {
                x = CreatureState.CentreOf(spot % world.Width);
                y = CreatureState.CentreOf(spot / world.Width);
            }
        }

        x = (x & ~0xFFF) | (context.Rng.NextInt(0x555) + 0x555);
        y = (y & ~0xFFF) | (context.Rng.NextInt(0x555) + 0x555);
        if (context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.Loose))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, FacingState);
            context.Emit(system, "spectator_spot", $"creature={creature.Id} to=({x >> 12},{y >> 12})");
            return true;
        }

        StateShadow.Move(context, system, creature, CreatureStateMachine.Idle);
        return TakeTheJob(context, system, creature);
    }

    /// <summary>143: another creature on its tile sends it back to 142. Otherwise it turns toward
    /// the centre of the pit's middle tile (room <c>+0x27</c>), waiting while it turns; then a cheer,
    /// action 70, or 69 when <c>random(100)</c> is over 30 (with a second <c>random(2)</c> for its
    /// variant), and 144.</summary>
    private static bool Face(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        if (CombatPitRules.AnyoneOn(world, creature.Tile, creature) && CreatureStateMachine.TrySetState(context.Hooks, creature, ChoosingState))
        {
            creature.PitStateEnteredTick = context.Tick.Value;
            return true;
        }

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (RoomIndex.RoomAt(world, context.Database, here) is { } room)
        {
            (int cx, int cy) = Middle(room);
            int heading = creature.Heading;
            int rate = context.Database.CreatureById(creature.CreatureTypeId).TurnRate;
            int left = Steering.Bearing.TurnToward(ref heading,
                Steering.Bearing.Of(CreatureState.CentreOf(cx) - creature.PosX, CreatureState.CentreOf(cy) - creature.PosY), rate);
            creature.Heading = heading;
            if (left != 0) return true;
        }

        int action = context.Rng.NextInt(100) > 30 ? 69 : 70;
        if (action == 69) context.Rng.NextInt(2);
        creature.ActionUntilTick = context.Tick.Value + CheerGuessTicks;
        context.Emit(system, "spectator_cheer", $"creature={creature.Id} action={action}");
        if (CreatureStateMachine.TrySetState(context.Hooks, creature, CheeringState)) creature.PitStateEnteredTick = context.Tick.Value;
        return true;
    }

    /// <summary>The room's <c>+0x27</c>, its position tile. Its meaning is inferred: 89's fallback
    /// aims two tiles either side of the Heart's, which fits a centre. Ours: the middle of the
    /// bounding box.</summary>
    internal static (int X, int Y) Middle(RoomInstance room) => ((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);

    /// <summary>144: moved since it last looked (and not at <c>Age</c> 1), it tries 142 and ends
    /// the cheer. Otherwise it remembers where it stands, waits out the cheer, and then: no fight
    /// on the pit and none for more than <see cref="QuietTicks"/>, idle; else 142.</summary>
    private static bool Cheer(SimulationContext context, string system, CreatureState creature)
    {
        long now = context.Tick.Value;
        bool moved = creature.SpectatorPosX != creature.PosX || creature.SpectatorPosY != creature.PosY;
        if (now - creature.PitStateEnteredTick != 1 && moved && Choosing(context, creature))
        {
            creature.ActionUntilTick = 0;
            return true;
        }

        creature.SpectatorPosX = creature.PosX;
        creature.SpectatorPosY = creature.PosY;
        if (now < creature.ActionUntilTick) return true;

        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (RoomIndex.RoomAt(world, context.Database, here) is { } room
            && !FightOn(context, room) && SinceTheFight(context, room) > QuietTicks)
            return false;
        return Choosing(context, creature);
    }

    private static bool Choosing(SimulationContext context, CreatureState creature)
    {
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, ChoosingState)) return false;
        creature.PitStateEnteredTick = context.Tick.Value;
        return true;
    }
}
