// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A worshipper's states in its keeper's Temple (<c>temple-states.md</c>, ADR-0398): 101 takes
/// it in, 103 picks a spot on its own tile or a free neighbour, 104 faces the room's centre and prays,
/// action 27; 105 waits the prayer out and steps ahead; 106 walks a summoned creature off the pool; 107
/// takes a holder back in after its Temple changed. The prayer hook runs under all but 107
/// (<see cref="Systems.CreatureJobSystem"/>).</summary>
/// <remarks>Ours: a state entered this turn runs from the next. The Hand offers a creature dropped into
/// the pool at once, where DK2 offers it in 101 after the drop's stun; 107 is seen at the holder's
/// next turn (§8).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/temple-states.md")]
public static class TempleStep
{
    /// <summary>101 (<c>0x00472930</c>): the way in.</summary>
    public const int EnteringState = 101;

    /// <summary>102 (<c>0x00472AB0</c>): 101 again; nothing enters it.</summary>
    public const int UnreachableState = 102;

    /// <summary>103 (<c>0x00472AC0</c>): the spot.</summary>
    public const int ChoosingState = 103;

    /// <summary>104 (<c>0x00472D30</c>): facing the centre, then the prayer.</summary>
    public const int FacingState = 104;

    /// <summary>105 (<c>0x00472E90</c>): praying.</summary>
    public const int PrayingState = 105;

    /// <summary>106 (<c>0x00473010</c>): a summoned creature leaves the pool.</summary>
    public const int SummonedState = 106;

    /// <summary>107 (<c>0x004730E0</c>): back in after the Temple changed.</summary>
    public const int ReturningState = 107;

    /// <summary>103 stays on its own tile above this <c>random(100)</c>.</summary>
    public const int StayAbove = 35;

    /// <summary>Action 27's art slot (record <c>0x006E44A7</c> <c>+0x14</c>).</summary>
    public const int PrayerArtSlot = 23;

    /// <summary><c>0x006BC350</c>: N, E, S, W; and <c>0x006BC370</c>: N, NE, E, SE, S, SW, W, NW.</summary>
    private static readonly (int Dx, int Dy)[] Four = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static readonly (int Dx, int Dy)[] Eight = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    public static bool IsTempleState(int state) => state is >= EnteringState and <= ReturningState;

    /// <summary>One turn of a worshipper. False when it is left idle, which ends the job.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (RoomEvictRules.Evict(context, system, creature)) return true;
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
            EnteringState => Enter101(context, system, creature, age),
            UnreachableState => Set(context, system, creature, EnteringState),
            ChoosingState => Choose(context, system, creature),
            FacingState => Face(context, system, creature),
            PrayingState => Pray(context, system, creature, age),
            SummonedState => Summoned(context, system, creature, age),
            ReturningState => Return(context, system, creature, age),
            _ => false,
        };
    }

    // ----------------------------------------------------------------------------- 101

    /// <summary>101: on any Temple's pool (a floor slot, the floor-height test's answer) the offering:
    /// the creature goes, the recipes are matched and a summoned creature walks off in 106; on its own
    /// keeper's Temple off the pool, 103; anywhere else, idle.</summary>
    private static bool Enter101(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        if (age == 1) creature.StopMoving();
        if (TempleUnder(world, context.Database, creature) is not { } temple) return false;
        int here = Here(world, creature);
        if (AutoFurnitureRules.IsFloorSlot(world, temple, here))
        {
            Sacrifice(context, system, temple, creature, here);
            return false;
        }

        return temple.OwnerPlayerId == creature.OwnerPlayerId && Set(context, system, creature, ChoosingState);
    }

    /// <summary>101's offering (<c>0x004F1600</c>, <c>0x004F1660</c>, <c>0x004F1780</c>; sacrifice.md),
    /// shared with the Hand's drop into the pool.</summary>
    public static SacrificeResult Sacrifice(SimulationContext context, string system, RoomInstance temple, CreatureState creature, int tile)
    {
        TempleSacrificeRules.Reconcile(context.World, context.Database);
        byte type = creature.CreatureTypeId;
        SacrificeResult result = TempleSacrificeRules.OfferCreature(context.World, context.Database, temple, creature, tile);
        // 0x004F1780 kicks the creature it made to 106 (temple-states.md §6, ADR-0398).
        if (result.RewardKind == SacrificeRewardKind.Creature && context.World.CreatureById(result.SpawnedThingId) is { } summoned)
            Summon(context, system, summoned);
        context.Emit(system, "sacrificed_in_pool",
            $"creature={creature.Id} type={type} temple_owner={temple.OwnerPlayerId} tile={tile % context.World.Width},{tile / context.World.Width} " +
            $"matched={(result.Matched ? "yes" : "no")} speech={result.SpeechId} reward={result.RewardKind}:{result.SpawnedThingId}");
        return result;
    }

    /// <summary>The Temple's pool: its floor slots, the interior tiles whose eight neighbours are all
    /// of the room. A creature in 101 is offered on one (<c>0x004545C0</c>, <c>0x00454110</c>); an
    /// object's landing state 4 asks the same of the tile's byte <c>+0xC</c> (<c>0x0049F632</c>).</summary>
    public static bool InThePool(WorldState world, RoomInstance temple, int tile) =>
        temple.Definition.TypeId == RoomTypeId.Temple && AutoFurnitureRules.IsFloorSlot(world, temple, tile);

    // ----------------------------------------------------------------------------- 103

    /// <summary>103 (temple-states.md §4): its own tile, or a free neighbour when it shares its tile
    /// or a <c>random(100)</c> of 35 or less says so; a found spot a quarter tile further out from the
    /// room's centre, otherwise its own position; a walk there resuming 104.</summary>
    private static bool Choose(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!creature.HeldPlace.IsHeld || RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is not { } held)
            return false;

        int here = Here(world, creature);
        int n = FloorSlot(world, database, here) ? 8 : 4;
        int? target;
        if (SomeoneElseOn(world, creature, here)) target = Neighbour(context, held, here, n);
        else if (context.Rng.NextInt(100) > StayAbove) target = here;
        else target = Neighbour(context, held, here, n);

        int x = creature.PosX, y = creature.PosY;
        if (target is { } tile)
        {
            int tx = CreatureState.CentreOf(tile % world.Width), ty = CreatureState.CentreOf(tile / world.Width);
            int a = Bearing.Of(tx - CreatureState.CentreOf(held.CentreFieldX), ty - CreatureState.CentreOf(held.CentreFieldY));
            x = tx + ((TrapRules.Sine(a) << 10) >> 16);
            y = ty + ((-TrapRules.Cosine(a) << 10) >> 16);
        }

        if (Walk(context, system, creature, x, y, FacingState)) return true;
        StateShadow.ToIdle(context, system, creature);
        return false;
    }

    /// <summary><c>0x004E50F0(room, pos, n, 1, 1)</c>: from <c>random(n)</c> round the four or eight
    /// neighbours, the first tile of the room that is no floor slot and has no creature on it.</summary>
    private static int? Neighbour(SimulationContext context, RoomInstance room, int here, int n)
    {
        WorldState world = context.World;
        int r = context.Rng.NextInt(n);
        (int Dx, int Dy)[] offsets = n == 4 ? Four : Eight;
        int x0 = here % world.Width, y0 = here / world.Width;
        for (int k = 0; k < n; k++)
        {
            (int dx, int dy) = offsets[(r + k) % n];
            var at = new TilePos(x0 + dx, y0 + dy);
            if (!world.Contains(at)) continue;
            int tile = (at.Y * world.Width) + at.X;
            if (!room.Contains(tile) || AutoFurnitureRules.IsFloorSlot(world, room, tile) || AnyoneOn(world, tile)) continue;
            return tile;
        }

        return null;
    }

    // ----------------------------------------------------------------------- 104, 105

    /// <summary>104: shared tile, 103; then it faces the room's centre; then action 27, two or three
    /// more plays when it has the art, and 105.</summary>
    private static bool Face(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (SomeoneElseOn(world, creature, Here(world, creature)) && StateShadow.TryMove(context, system, creature, ChoosingState))
            return true;

        if (creature.HeldPlace.IsHeld && RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is { } held
            && CasinoStep.TurnTo(context, creature, CreatureState.CentreOf(held.CentreFieldX), CreatureState.CentreOf(held.CentreFieldY)) != 0)
            return true;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (AnimationTimingRules.ResolvesPresent(definition, PrayerArtSlot))
        {
            int count = context.Rng.NextInt(2) + 2;
            creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, PrayerArtSlot, count);
            context.Emit(system, "temple_pray", $"creature={creature.Id} count={count} until={creature.ActionUntilTick}");
        }

        return Set(context, system, creature, PrayingState);
    }

    /// <summary>105: moved, back to 101, ending the prayer; while it runs, it waits; then the step
    /// ahead, and 103.</summary>
    private static bool Pray(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;
        bool moved = creature.WaitPosX != creature.PosX || creature.WaitPosY != creature.PosY;
        if (age != 1 && moved && StateShadow.TryMove(context, system, creature, EnteringState))
        {
            if (now < creature.ActionUntilTick) creature.ActionUntilTick = 0;
            return true;
        }

        (creature.WaitPosX, creature.WaitPosY) = (creature.PosX, creature.PosY);
        if (now < creature.ActionUntilTick) return true;

        // 0x00472F64-0x00472FD3: both floor-slot lookups are of its own position, so the step is
        // always taken (0x004B5560 does not bound it; ours clamps to the map, which a step of under
        // half a tile from a room tile never leaves).
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        int ahead = -definition.PrayerStepY;
        int x = creature.PosX + ((TrapRules.Sine(creature.Heading) * ahead) >> 16);
        int y = creature.PosY + -((TrapRules.Cosine(creature.Heading) * ahead) >> 16);
        creature.PosX = Math.Clamp(x, 0, (world.Width << 12) - 1);
        creature.PosY = Math.Clamp(y, 0, (world.Height << 12) - 1);

        return Set(context, system, creature, ChoosingState);
    }

    // ----------------------------------------------------------------------- 106, 107

    /// <summary><c>0x004F1780</c>'s kick on the creature a sacrifice made: 106, whose enter hook
    /// claims it a place in the Temple it stands in.</summary>
    public static void Summon(SimulationContext context, string system, CreatureState creature)
    {
        creature.CurrentJob = JobType.Praying;
        if (!StateShadow.TryMove(context, system, creature, SummonedState)) EndJob(creature);
    }

    /// <summary>106: at its first turn and holding a place, a walk to the room point resuming 1.</summary>
    private static bool Summoned(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        if (age != 1 || !creature.HeldPlace.IsHeld
            || RoomIndex.RoomAt(world, context.Database, creature.HeldPlace.TileIndex) is not { } held)
            return false;
        if (PrisonRules.RefusedBodyPoint(world, context.Database, held, context.Rng) is not { } tile) return false;
        if (!Walk(context, system, creature, CreatureState.CentreOf(tile % world.Width), CreatureState.CentreOf(tile / world.Width),
                CreatureStateMachine.Idle))
            return false;
        EndJob(creature);   // a walk resuming 1 is the idle walk's (CreatureJobSystem)
        return true;
    }

    private static void EndJob(CreatureState creature)
    {
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
    }

    /// <summary>107: a turn's wait, then 101 on its own keeper's Temple, otherwise idle.</summary>
    private static bool Return(SimulationContext context, string system, CreatureState creature, long age)
    {
        if (age <= 1) return true;
        return TempleUnder(context.World, context.Database, creature) is { } temple
            && temple.OwnerPlayerId == creature.OwnerPlayerId
            && Set(context, system, creature, EnteringState);
    }

    // --------------------------------------------------------------------------- shared

    private static int Here(WorldState world, CreatureState creature) => (creature.Tile.Y * world.Width) + creature.Tile.X;

    private static RoomInstance? TempleUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!world.Contains(creature.Tile)) return null;
        RoomInstance? room = RoomIndex.RoomAt(world, database, Here(world, creature));
        return room is not null && room.Definition.TypeId == RoomTypeId.Temple ? room : null;
    }

    /// <summary><c>0x00454110</c> on a tile: a floor slot of the room it is in.</summary>
    private static bool FloorSlot(WorldState world, GameDatabase database, int tile) =>
        RoomIndex.RoomAt(world, database, tile) is { } room && AutoFurnitureRules.IsFloorSlot(world, room, tile);

    /// <summary>The tile's thing list holds a creature (kind 0) other than this one. A corpse is DK2's
    /// <c>CDeadBody</c>, another kind of thing (dying-states.md §2), so it is not counted; a dying
    /// creature is.</summary>
    private static bool SomeoneElseOn(WorldState world, CreatureState creature, int tile)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.Id != creature.Id && !CreatureDeathRules.IsCorpse(c) && world.Contains(c.Tile)
                && (c.Tile.Y * world.Width) + c.Tile.X == tile)
                return true;
        return false;
    }

    private static bool AnyoneOn(WorldState world, int tile)
    {
        foreach (CreatureState c in world.Creatures)
            if (!CreatureDeathRules.IsCorpse(c) && world.Contains(c.Tile) && (c.Tile.Y * world.Width) + c.Tile.X == tile)
                return true;
        return false;
    }

    private static bool Walk(SimulationContext context, string system, CreatureState creature, int x, int y, int resume)
    {
        if (!context.Navigation.TryRouteTo(creature, x, y, MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        context.Emit(system, "temple_walk", $"creature={creature.Id} to=({x >> 12},{y >> 12}) resume={resume}");
        return true;
    }

    /// <summary><c>SetState(state)</c> and return 1, whether or not it was taken.</summary>
    private static bool Set(SimulationContext context, string system, CreatureState creature, int state)
    {
        StateShadow.TryMove(context, system, creature, state);
        return true;
    }
}
