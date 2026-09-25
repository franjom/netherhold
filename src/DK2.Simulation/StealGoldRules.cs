// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>Job 13, <see cref="JobType.StealGold"/>, as a hero runs it (steal-gold.md, ADR-0219): the arm
/// <c>0x00481FA0</c>, then states 163, 164, 165 and 169, and 226 for the followers it recruits.</summary>
/// <remarks>DK2's quirk is kept: the arm checks the job's player, but 163 walks to the Treasury of
/// the player at index 2, the first keeper, and 164 then insists the tile is the job player's.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/steal-gold.md")]
public static class StealGoldRules
{
    /// <summary>State 163 (<c>0x00482010</c>): to the first keeper's nearest Treasury.</summary>
    public const int ToTheTreasuryState = 163;

    /// <summary>State 164 (<c>0x00482190</c>): in the Treasury, to a tile with gold.</summary>
    public const int InTheTreasuryState = 164;

    /// <summary>State 165 (<c>0x004824E0</c>): take the gold, then leave.</summary>
    public const int AtTheGoldState = 165;

    /// <summary>State 169 (<c>0x00482DB0</c>), "escaping with stolen booty": gone at its origin.</summary>
    public const int EscapingState = 169;

    /// <summary>State 226 (<c>0x004787F0</c>): run the standing job once.</summary>
    public const int RunTheJobState = 226;

    /// <summary>The player 163 raids: <c>[world+0x84] + 0x224A</c>, two records of <c>0x1125</c>
    /// in, whose id byte (<c>+0x10FC</c>, written index + 1 by <c>0x004C1E20</c>) is 3.</summary>
    public const byte RaidedPlayer = PlayerIds.FirstKeeper;

    private const int FeasibleOptions = 0x03;
    private const int TreasuryOptions = 0x69;
    private const int GoldOptions = 0x68;

    /// <summary>The arm (<c>0x00481FA0</c>): a job player other than the hero's own (the strongest when
    /// it names none, not written back), a Treasury of that player's (<c>0x004C61C0</c>) the hero
    /// could reach (<c>0x00471740</c>, options 3), then 163.</summary>
    public static bool Arm(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        byte? target = creature.GoodJobPlayerId != 0
            ? (world.HasPlayer(creature.GoodJobPlayerId) ? creature.GoodJobPlayerId : null)
            : HeroJobRules.StrongestPlayer(world, context.Database);
        if (target is not byte player || player == creature.OwnerPlayerId) return false;
        if (NearestTreasury(context, player, creature) is not RoomInstance room) return false;
        if (FirstStandable(context, creature, room) is not int tile) return false;
        var scratch = new List<Steering.Fixed16Vector>();
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int w = world.Width;
        if (!Combat.CombatTargetRules.CanReach(context.Navigation, creature, definition,
                CreatureState.CentreOf(tile % w), CreatureState.CentreOf(tile / w), int.MaxValue, scratch)
            && !((FeasibleOptions & 0x01) != 0 && TunnelRules.CouldTunnel(context, creature)))
            return false;
        return StateShadow.TryMove(context, system, creature, ToTheTreasuryState);
    }

    /// <summary>State 163: never for a creature of the raided player itself; otherwise that player's
    /// nearest Treasury, resuming 164, options <c>0x69</c>.</summary>
    public static bool ToTheTreasury(SimulationContext context, string system, CreatureState creature)
    {
        if (creature.OwnerPlayerId == RaidedPlayer) return false;
        if (NearestTreasury(context, RaidedPlayer, creature) is not RoomInstance room) return false;
        return ToRoom(context, system, creature, room, InTheTreasuryState, TreasuryOptions);
    }

    /// <summary>State 164: standing in a Treasury of the job's player (a keeper's creature: its own),
    /// walk to the first gold in it that routes, resuming 165. A leader then recruits every follower
    /// of its party in, or walking back to, 222: job 13 against the same player, then 226.</summary>
    public static bool InTheTreasury(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        byte player = HeroJobRules.IsHero(creature) ? creature.GoodJobPlayerId : creature.OwnerPlayerId;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, here);
        if (room is null || room.OwnerPlayerId != player || room.Definition.RoomId != (byte)RoomTypeId.Treasury)
            return false;

        bool walking = false;
        foreach (int tile in room.TileIndices)
        {
            if (world.TileAtIndex(tile).GoldHeld <= 0) continue;
            if (Walk(context, creature, tile, AtTheGoldState, GoldOptions)) { walking = true; break; }
        }
        if (!walking) return false;

        if ((creature.Flags & 0x02) == 0) return true;
        foreach (CreatureState c in world.Creatures)
        {
            if (c.PartyId != creature.PartyId || !HeroJobRules.IsFollower(c) || !HeroJobRules.IsHero(c)) continue;
            bool following = c.Dk2StateId == HeroJobRules.FollowingState
                || (StateShadow.IsTravelling(c) && c.UtilityNewState == HeroJobRules.FollowingState);
            if (!following) continue;
            c.GoodJob = JobType.StealGold;
            c.GoodJobPlayerId = creature.GoodJobPlayerId;
            c.GoodJobActionPointId = 0;
            StateShadow.Move(context, system, c, RunTheJobState);
        }
        return true;
    }

    /// <summary>State 165: creature event 13; gold on the tile underfoot, so take what the purse has
    /// room for from the whole Treasury (<c>0x004F4510</c>), half of it into the purse
    /// (<c>0x0048AE70(taken &gt;&gt; 1)</c>), and walk to the origin tile, resuming 169, options 0.
    /// The gold is gone even when that walk fails. No gold: 164, then idle.</summary>
    public static bool AtTheGold(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        context.FireCreatureEvent(creature, CreatureTriggerEvent.Steals);
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (world.TileAtIndex(here).GoldHeld > 0 && RoomIndex.RoomAt(world, database, here) is { } room)
        {
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            int wanted = CreatureLevelRules.MaxGoldHeld(database, definition, creature.Level) - creature.GoldHeld;
            int taken = PaydayRules.TakeFromRoom(world, room, here, wanted);
            int kept = PurseRules.Receive(database, definition, creature, taken >> 1);
            context.Emit(system, "gold_stolen",
                $"creature={creature.Id} player={room.OwnerPlayerId} taken={taken} kept={kept} " +
                $"at=({creature.Tile.X},{creature.Tile.Y})");
            if (creature.OriginTileIndex >= 0 && Walk(context, creature, creature.OriginTileIndex, EscapingState, 0))
                return true;
        }
        StateShadow.Move(context, system, creature, InTheTreasuryState);
        return false;
    }

    /// <summary>State 169, at the origin: the purse emptied into nothing (<c>0x0048AF50</c>), then
    /// the creature destroyed without a body (<c>+0x3E6</c> bit 5 cleared). A creature not
    /// <see cref="CreatureFlags.AvailableViaPortal"/> leaves effect 15 behind. Ours: nothing a hero
    /// carries is destroyed with it, since no carried steal is built.</summary>
    public static void Escaped(SimulationContext context, string system, CreatureState creature)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int lost = PurseRules.Spend(creature, creature.GoldHeld);
        bool effect = !definition.Flags.HasFlag(CreatureFlags.AvailableViaPortal);
        context.Emit(system, "creature_escaped",
            $"creature={creature.Id} type={definition.Name} player={creature.OwnerPlayerId} " +
            $"at=({creature.Tile.X},{creature.Tile.Y}) gold_lost={lost} effect={(effect ? 15 : 0)}");
        context.World.RemoveCreature(creature.Id);
    }

    /// <summary><c>0x004C61C0(1, position, out, 0)</c> on one player: its Treasury whose first tile's
    /// centre is nearest, by DK2's truncated squares, the first on a tie.</summary>
    public static RoomInstance? NearestTreasury(SimulationContext context, byte player, CreatureState from)
    {
        WorldState world = context.World;
        RoomInstance? best = null;
        uint bestDistance = uint.MaxValue;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.OwnerPlayerId != player || room.Definition.RoomId != (byte)RoomTypeId.Treasury) continue;
            int anchor = room.TileIndices[0];
            long dx = Math.Abs((long)CreatureState.CentreOf(anchor % world.Width) - from.PosX);
            long dy = Math.Abs((long)CreatureState.CentreOf(anchor / world.Width) - from.PosY);
            uint distance = unchecked((uint)((dx * dx) >> 12) + (uint)((dy * dy) >> 12));
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = room;
        }
        return best;
    }

    /// <summary><c>0x00471A70(room, resume, options)</c>: already in the room, 1 and then the resume
    /// state; otherwise a point in it (<c>0x004E4C10</c>) and state 7, or with option bit 0 a tunnel
    /// to the room's first tile (<c>0x00485AB0</c>).</summary>
    private static bool ToRoom(SimulationContext context, string system, CreatureState creature, RoomInstance room, int resume, int options)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (room.Contains(here))
        {
            StateShadow.Move(context, system, creature, CreatureStateMachine.Idle);
            return StateShadow.TryMove(context, system, creature, resume);
        }
        int w = world.Width;
        int first = room.TileIndices[0];
        if (RoomPoint(context, creature, room) is not int tile)
            return (options & 0x01) != 0 && TunnelRules.Fallback(context, system, creature, first % w, first / w, resume);
        return TunnelRules.RouteOrTunnel(context, system, creature, CreatureState.CentreOf(tile % w), CreatureState.CentreOf(tile / w),
            StateShadow.TravelToRoom, resume, options, first % w, first / w);
    }

    /// <summary><c>0x004E4C10</c>: a random tile of the room, then the first from there, round the
    /// room, that passes its four cell tests. Ours reads those tests as "walkable" (unread).</summary>
    private static int? RoomPoint(SimulationContext context, CreatureState creature, RoomInstance room)
    {
        IReadOnlyList<int> tiles = room.TileIndices;
        int start = context.Rng.NextInt(tiles.Count);
        var grid = new WorldPathGrid(context.Database, context.World, context.Database.CreatureById(creature.CreatureTypeId));
        int w = context.World.Width;
        for (int k = 0; k <= tiles.Count; k++)
        {
            int tile = tiles[(start + k) % tiles.Count];
            if (grid.IsWalkable(tile % w, tile / w)) return tile;
        }
        return null;
    }

    /// <summary>The feasibility test's point. DK2's <c>0x004E5000</c> is unread; ours takes the
    /// room's first walkable tile, which draws no random number.</summary>
    private static int? FirstStandable(SimulationContext context, CreatureState creature, RoomInstance room)
    {
        var grid = new WorldPathGrid(context.Database, context.World, context.Database.CreatureById(creature.CreatureTypeId));
        int w = context.World.Width;
        foreach (int tile in room.TileIndices)
            if (grid.IsWalkable(tile % w, tile / w)) return tile;
        return null;
    }

    private static bool Walk(SimulationContext context, CreatureState creature, int tile, int resume, int options)
    {
        int w = context.World.Width;
        if (!context.Navigation.TryRouteTo(creature, CreatureState.CentreOf(tile % w), CreatureState.CentreOf(tile / w), options))
            return false;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, resume);
        return true;
    }
}
