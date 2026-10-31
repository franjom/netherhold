// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A Casino jackpot (<c>casino.md</c> §8–§10, ADR-0396): the roll's call <c>0x004D6AA0</c>
/// sends the room's other gamblers dancing and starts a party, and the winner celebrates (152), walks
/// to a Treasury or Heart (155, 156) and takes its winnings (157) before a deadline, which, missed,
/// angers every creature of its keeper (<c>0x004D6A40</c>). The room update <c>0x004D6270</c> ends
/// the party.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/casino.md")]
public static class CasinoJackpotRules
{
    /// <summary>152 (<c>0x00456EC0</c>): the winner's celebration, ten game seconds.</summary>
    public const int CelebratingState = 152;

    /// <summary>155 (<c>0x00457010</c>): choosing a Treasury or Heart and walking there.</summary>
    public const int SeekingTheGoldState = 155;

    /// <summary>156 (<c>0x004574A0</c>): arrived, turning.</summary>
    public const int ArrivedState = 156;

    /// <summary>157 (<c>0x00457560</c>): taking the winnings.</summary>
    public const int CollectingState = 157;

    /// <summary><see cref="TileState.CasinoParty"/>'s bits: the party (room <c>+0x41</c>) and its
    /// music (room <c>+0x3D</c>).</summary>
    public const byte PartyBit = 1, MusicBit = 2;

    /// <summary>The roll that wins: <c>random(chance) == 42</c>.</summary>
    public const int WinningRoll = 42;

    /// <summary>A party ends after 90 game seconds (<c>v19 × 90</c>).</summary>
    public const int PartySeconds = 90;

    /// <summary>A party's tile effect, every twelve game seconds (<c>v19 × 12</c>).</summary>
    public const int EffectSeconds = 12;

    /// <summary>152 lasts ten game seconds (<c>v19 × 10</c>).</summary>
    public const int CelebrationSeconds = 10;

    /// <summary>155's eight stand points on a tile, <c>0x00698C20</c>: thirds of a tile, the centre left out.</summary>
    private static readonly (int X, int Y)[] Spots =
    [
        (0x2AA, 0x2AA), (0x7FF, 0x2AA), (0xD54, 0x2AA), (0x2AA, 0x7FF),
        (0xD54, 0x7FF), (0x2AA, 0xD54), (0x7FF, 0xD54), (0xD54, 0xD54),
    ];

    public static bool IsWinnerState(int state) =>
        state is CelebratingState or SeekingTheGoldState or ArrivedState or CollectingState;

    /// <summary>Whether this Casino has a party on, room <c>+0x41</c>.</summary>
    public static bool IsPartyOn(WorldState world, RoomInstance room) =>
        room.TileIndices.Count > 0 && (world.TileAtIndex(room.TileIndices[0]).CasinoParty & PartyBit) != 0;

    /// <summary>The update hook's roll, once an act after the purse: 1 in variable 205 (Smiley) or 206 ($).</summary>
    public static int Chance(GameDatabase database, byte setting) => database[setting == CasinoRules.Stingy
        ? VariableTypeId.GamblingCreatureJackpotChanceMoneyPerSecond
        : VariableTypeId.GamblingCreatureJackpotChanceSmilesPerSecond];

    // ------------------------------------------------------------------------ the jackpot

    /// <summary><c>0x004D6AA0</c> on the gambler's keeper: nothing while any of its creatures has a
    /// deadline; otherwise the others holding a place in the winner's Casino are kicked to 1 and 153,
    /// the party starts, the winner is given its winnings and deadline, message 73, and the winner is
    /// kicked to 1 and 152 (casino.md §8).</summary>
    public static void Jackpot(SimulationContext context, string system, CreatureState winner)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        PlayerId keeper = winner.OwnerPlayerId;
        foreach (CreatureState c in world.Creatures)
            if (c.OwnerPlayerId == keeper && c.JackpotDeadlineTicks != 0) return;
        if (!winner.HeldPlace.IsHeld || RoomIndex.RoomAt(world, database, winner.HeldPlace.TileIndex) is not { } room) return;

        foreach (CreatureState c in world.Creatures)
        {
            if (c.Id == winner.Id || c.OwnerPlayerId != keeper || !c.HeldPlace.In(room)) continue;
            Kick(context, system, c, CasinoStep.DancingState);
        }

        world.Player(keeper).CasinoParties++;
        ref TileState first = ref world.TileAtIndex(room.TileIndices[0]);
        first.CasinoParty |= PartyBit;
        first.CasinoPartyStartTick = context.Tick.Value;

        winner.JackpotDeadlineTicks = database[VariableTypeId.GamblingJackpotPayTimeLimit] * SimulationTiming.OriginalTicksPerGameSecond;
        winner.JackpotWinnings = Math.Max(database[VariableTypeId.GamblingJackpotBasicAmount], WageBill(world, database, keeper));
        winner.CasinoBigWinnerSlappedCount = 0;
        world.Messages.Post(world, winner.OwnerPlayerId, PlayerMessageKind.CasinoJackpot, winner.Id, 0);
        Kick(context, system, winner, CelebratingState);
        winner.CurrentJob = JobType.None;
        winner.JobTileIndex = -1;
        context.Emit(system, "casino_jackpot",
            $"creature={winner.Id} player={keeper} winnings={winner.JackpotWinnings} deadline={winner.JackpotDeadlineTicks}");
    }

    /// <summary><c>0x004BE290</c>: the keeper's wage bill, each creature's level-scaled <c>Pay</c>,
    /// over those <c>0x00491A70</c> counts (not dying; a captive or pit fighter only in its own
    /// keeper's room) and that hold no <c>GetPaid</c> ticket (<c>+0x3E2</c>).</summary>
    public static int WageBill(WorldState world, GameDatabase database, PlayerId keeper)
    {
        int bill = 0;
        foreach (CreatureState c in world.Creatures)
        {
            if (c.OwnerPlayerId != keeper || CreatureDeathRules.IsDying(c) || CreatureDeathRules.IsCorpse(c)) continue;
            if (c.GetPaid || !CountsOnPayroll(world, database, c)) continue;
            bill += PaydayRules.Wage(database, database.CreatureById(c.CreatureTypeId), c.Level);
        }

        return bill;
    }

    private static bool CountsOnPayroll(WorldState world, GameDatabase database, CreatureState c)
    {
        if (!CreatureStateMachine.HasState(c)) return false;
        int state = StateShadow.IsTravelling(c) ? c.UtilityNewState : c.Dk2StateId;
        CreatureStateCategory category = CreatureStateTable.Get(state).Category;
        bool held = category is CreatureStateCategory.Prison or CreatureStateCategory.Torture
            || state is 126 or 136 || c.CombatPitFighter;
        if (!held || !c.HeldPlace.IsHeld) return true;
        return RoomIndex.RoomAt(world, database, c.HeldPlace.TileIndex) is not { } room || room.OwnerPlayerId == c.OwnerPlayerId;
    }

    /// <summary><c>Creature_KickToState(1)</c> then the target, stamped; a creature left idle ends its job.
    /// It stops walking: in DK2 only the walk state moves a creature.</summary>
    private static void Kick(SimulationContext context, string system, CreatureState creature, int state)
    {
        creature.StopMoving();
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        if (StateShadow.TryMove(context, system, creature, state)) return;
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
    }

    // ---------------------------------------------------------------------- the party

    /// <summary>The party part of the Casino's room update <c>0x004D6270</c>, before its settlement: a
    /// party past 90 game seconds, or in a room nobody holds a place in, ends (each its own decrement
    /// of the keeper's count); while one is on, a dancer turns the music on; once it is off, the
    /// music's dancers end their action and are kicked to 145 (casino.md §9). True on a party turn,
    /// which returns without the settlement: only the party-off path reaches it (<c>0x004D676F</c>).</summary>
    public static bool RoomUpdate(SimulationContext context, string system, RoomInstance room)
    {
        WorldState world = context.World;
        ref TileState first = ref world.TileAtIndex(room.TileIndices[0]);
        long now = context.Tick.Value;
        if ((first.CasinoParty & PartyBit) != 0)
        {
            if (now - first.CasinoPartyStartTick > (long)PartySeconds * SimulationTiming.OriginalTicksPerGameSecond)
                EndParty(context, system, room, ref first);
            if (!AnyPlaceHeld(world, room)) EndParty(context, system, room, ref first);
            foreach (CreatureState c in world.Creatures)
                if (c.HeldPlace.In(room) && c.Dk2StateId == CasinoStep.DancingState) first.CasinoParty |= MusicBit;
            // Every twelve seconds an effect on a random tile: the effect is presentation, its draw is
            // on the simulation stream (0x004D6561).
            if ((now + 1) % (EffectSeconds * SimulationTiming.OriginalTicksPerGameSecond) == 0)   // DK2's turn is our tick + 1
                context.Rng.NextInt(room.TileIndices.Count);
            return true;
        }

        if ((first.CasinoParty & MusicBit) == 0) return false;
        foreach (CreatureState c in world.Creatures)
        {
            if (!c.HeldPlace.In(room) || c.Dk2StateId != CasinoStep.DancingState) continue;
            c.ActionUntilTick = 0;
            if (!StateShadow.TryMove(context, system, c, CasinoStep.ChoosingState))
            {
                c.CurrentJob = JobType.None;
                c.JobTileIndex = -1;
            }
        }

        first.CasinoParty &= unchecked((byte)~MusicBit);
        return false;
    }

    private static bool AnyPlaceHeld(WorldState world, RoomInstance room)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room)) return true;
        return false;
    }

    private static void EndParty(SimulationContext context, string system, RoomInstance room, ref TileState first)
    {
        first.CasinoParty &= unchecked((byte)~PartyBit);
        first.CasinoPartyStartTick = 0;
        context.World.Player(room.OwnerPlayerId).CasinoParties--;
        context.Emit(system, "casino_party_over", $"player={room.OwnerPlayerId} room={room.TileIndices[0]}");
    }

    // ------------------------------------------------------------------- the countdowns

    /// <summary>The creature update's two Casino countdowns: the drunk timer, and a winner's deadline,
    /// which at zero angers every creature of its keeper by variable 208 per cent of its Treatment
    /// bucket and posts message 91 (<c>0x0048AA07</c>, <c>0x004D6A40</c>).</summary>
    public static void CountDown(SimulationContext context, string system, CreatureState creature)
    {
        if (creature.DrunkTicks > 0) creature.DrunkTicks--;
        if (creature.JackpotDeadlineTicks == 0 || --creature.JackpotDeadlineTicks != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        int percent = database[VariableTypeId.GamblingJackpotPayTimeLimitMissedAngerPercentageModifier];
        foreach (CreatureState c in world.Creatures)
        {
            if (c.OwnerPlayerId != creature.OwnerPlayerId) continue;
            CreatureDef definition = database.CreatureById(c.CreatureTypeId);
            int delta = c.Anger[AngerBucket.Treatment] * percent / 100;
            CreatureAngerRules.Apply(world, definition, c, AngerBucket.Treatment, delta, AngerComplaint.General);
        }

        world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.JackpotMissed, 0, 0);
        context.Emit(system, "jackpot_missed", $"creature={creature.Id} player={creature.OwnerPlayerId}");
    }

    // ------------------------------------------------------------------ the winner's states

    /// <summary>One turn of a winner in 152, 155, 156 or 157. False when it is left idle.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            return StateShadow.Arrive(context, system, creature);
        }

        long age = context.Tick.Value - creature.StateEnteredTick;
        if (age == 0) return true;
        return creature.Dk2StateId switch
        {
            CelebratingState => Celebrate(context, system, creature, age),
            SeekingTheGoldState => SeekTheGold(context, system, creature),
            ArrivedState => Arrived(context, system, creature),
            CollectingState => Collect(context, system, creature),
            _ => false,
        };
    }

    /// <summary>152: action 57 or 35 at the first turn, 35 again whenever none runs, then after ten
    /// game seconds 1 and 155 (casino.md §10).</summary>
    private static bool Celebrate(SimulationContext context, string system, CreatureState creature, long age)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        long now = context.Tick.Value;
        if (age == 1)
        {
            creature.ActionUntilTick = 0;
            if (AnimationTimingRules.ResolvesPresent(definition, 28))
                creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, 28, 0);
            else if (AnimationTimingRules.ResolvesPresent(definition, 8))
                creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, 8, context.Rng.NextInt(4) + 3);
            else return false;
        }

        if (now >= creature.ActionUntilTick)
            creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, 8, 0);
        if (age >= (long)CelebrationSeconds * SimulationTiming.OriginalTicksPerGameSecond)
            Kick(context, system, creature, SeekingTheGoldState);
        return CreatureStateMachine.HasState(creature) && creature.Dk2StateId != CreatureStateMachine.Idle;
    }

    /// <summary>155: the nearest reachable Treasury or Heart holding more than the winnings, else the
    /// nearest reachable one; a tile of it holding gold, round the chain from a random one; one of the
    /// eight spots on it, and a walk resuming 156. Refused: message 77, then 1 and 87.</summary>
    private static bool SeekTheGold(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        if ((Bank(context, creature, mustCover: true) ?? Bank(context, creature, mustCover: false)) is not { } room) return true;

        int[] chain = SlabChain.Order(world, room);
        int start = context.Rng.NextInt(chain.Length);
        int tile = chain[start];
        for (int k = 0; k < chain.Length; k++)
        {
            int candidate = chain[(start + k) % chain.Length];
            if (world.TileAtIndex(candidate).GoldHeld <= 0) continue;
            if (room.Definition.TypeId == RoomTypeId.DungeonHeart && !OnHeartRing(world, room, candidate)) continue;
            tile = candidate;
            break;
        }

        (int dx, int dy) = Spots[context.Rng.NextInt(Spots.Length)];
        int x = ((tile % world.Width) << 12) | dx, y = ((tile / world.Width) << 12) | dy;
        if (context.Navigation.TryRouteTo(creature, x, y, MoveOptions.ExactSpot))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, ArrivedState);
            return true;
        }

        world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.JackpotUnpaid, 0, 0);
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        return StateShadow.TryMove(context, system, creature, ImpStates.Wandering);
    }

    private static RoomInstance? Bank(SimulationContext context, CreatureState creature, bool mustCover)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        RoomInstance? best = null;
        uint bestDistance = uint.MaxValue;
        var scratch = new List<Fixed16Vector>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsBankOf(world, room, creature.OwnerPlayerId) || room.TileIndices.Count == 0) continue;
            if (mustCover && PaydayRules.GoldIn(world, room) <= creature.JackpotWinnings) continue;
            int first = SlabChain.FirstSlab(world, room);
            int x = CreatureState.CentreOf(first % world.Width), y = CreatureState.CentreOf(first / world.Width);
            if (!CombatTargetRules.CanReach(context.Navigation, creature, definition, x, y, int.MaxValue, scratch, MoveOptions.Loose))
                continue;
            long ax = Math.Abs((long)x - creature.PosX), ay = Math.Abs((long)y - creature.PosY);
            uint distance = unchecked((uint)((ax * ax) >> 12) + (uint)((ay * ay) >> 12));
            if (distance >= bestDistance) continue;
            best = room;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>A Treasury of the keeper's, or its Heart unless ruined (<c>+0x9A</c> bit 0).</summary>
    private static bool IsBankOf(WorldState world, RoomInstance room, PlayerId keeper) =>
        room.OwnerPlayerId == keeper && (room.Definition.TypeId == RoomTypeId.Treasury
            || (room.Definition.TypeId == RoomTypeId.DungeonHeart && !world.TileAtIndex(room.TileIndices[0]).RoomRuined));

    /// <summary><c>0x004DA1B0</c>: a tile on the outer ring of the Heart's 5×5, from its first slab.</summary>
    private static bool OnHeartRing(WorldState world, RoomInstance room, int tile)
    {
        int first = SlabChain.FirstSlab(world, room);
        int x = tile % world.Width, y = tile / world.Width;
        int fx = first % world.Width, fy = first / world.Width;
        return x == fx || x - fx == 4 || y == fy || y - fy == 4;
    }

    /// <summary>156: on its keeper's Treasury or Heart, it turns toward the point DK2 computes from its
    /// position scaled a second time, wrapped to 32 bits, then 1 and 157; elsewhere it stays.</summary>
    private static bool Arrived(SimulationContext context, string system, CreatureState creature)
    {
        if (BankUnder(context, creature) is null) return true;
        int x = unchecked((creature.PosX * 0x1000) + 0x800), y = unchecked((creature.PosY * 0x1000) + 0x800);
        if (CasinoStep.TurnTo(context, creature, x, y) != 0) return true;
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        return StateShadow.TryMove(context, system, creature, CollectingState);
    }

    /// <summary>157: message 77 while the keeper has less gold than the winnings; off a Treasury or Heart,
    /// idle. Otherwise the smaller of the winnings and the room's gold, taken from its tile round into
    /// the purse; all of it asked for, the deadline clears and it is idle, else 1 and 155.</summary>
    private static bool Collect(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (GoldStore.Total(world, creature.OwnerPlayerId) < creature.JackpotWinnings)
            world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.JackpotUnpaid, 0, 0);
        if (BankUnder(context, creature) is not { } room) return false;

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        int asked = Math.Min(creature.JackpotWinnings, PaydayRules.GoldIn(world, room));
        int taken = PaydayRules.TakeFromRoom(world, room, here, asked);
        if (taken != 0)
        {
            int kept = PurseRules.Receive(database, database.CreatureById(creature.CreatureTypeId), creature, taken);
            context.Emit(system, "jackpot_collected", $"creature={creature.Id} taken={taken} kept={kept} owed={creature.JackpotWinnings}");
            if (asked == creature.JackpotWinnings)
            {
                creature.JackpotDeadlineTicks = 0;
                return false;
            }

            creature.JackpotWinnings -= asked;
        }

        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        return StateShadow.TryMove(context, system, creature, SeekingTheGoldState);
    }

    private static RoomInstance? BankUnder(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (!world.Contains(creature.Tile)) return null;
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, (creature.Tile.Y * world.Width) + creature.Tile.X);
        return room is not null && room.OwnerPlayerId == creature.OwnerPlayerId
            && room.Definition.TypeId is RoomTypeId.Treasury or RoomTypeId.DungeonHeart ? room : null;
    }
}
