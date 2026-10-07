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

/// <summary>A researcher's states in its keeper's Library (<c>library-states.md</c>, ADR-0397): 53
/// chooses a free book or bookcase, or a bare tile, and walks there; 54 faces it and reads, action 29;
/// 56 and 57 wait the read out and choose again; 59 watches an intruder who disturbed a long read.
/// The Research hook runs under all of them (<see cref="Systems.CreatureJobSystem"/>).</summary>
/// <remarks>Ours: a state entered this turn runs from the next. The objects' flags, the book's
/// process and action 40's animation are presentation and not kept.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/library-states.md")]
public static class ResearchStep
{
    /// <summary>53 (<c>0x00475A20</c>): the chooser.</summary>
    public const int ChoosingState = 53;

    /// <summary>54 (<c>0x00475D20</c>): at the spot, facing, then the read.</summary>
    public const int AtTheShelfState = 54;

    /// <summary>56 (<c>0x00476060</c>): a long read, at a book or bookcase.</summary>
    public const int ReadingLongState = 56;

    /// <summary>57 (<c>0x00475E70</c>): a short read, on a bare tile.</summary>
    public const int ReadingShortState = 57;

    /// <summary>59 (<c>0x004762D0</c>): disturbed, watching the intruder.</summary>
    public const int DisturbedState = 59;

    /// <summary>60 (<c>0x004761F0</c>): a delivered research item's cheer, which every place-holder of
    /// the Library that shelved it is kicked to (<c>0x004E08B0</c>).</summary>
    public const int CelebratingState = 60;

    /// <summary>62 (<c>0x00476320</c>): back into a Library that changed.</summary>
    public const int ReturningState = 62;

    /// <summary>The art slot 60's test reads (<c>0x00494860(8)</c>) and its action 35 plays.</summary>
    private const int CelebrationArtSlot = 8;

    /// <summary>The Library's objects: 58, <i>Library Book</i>, and 64, <i>Book Case</i>.</summary>
    public const byte BookObjectId = 58, BookCaseObjectId = 64;

    /// <summary>The disturbed room's reach, <c>dx² + dy² &lt; 0x9000</c> in squared 1/4096 tiles over
    /// 4096: three tiles.</summary>
    public const uint DisturbReach = 0x9000;

    public static bool IsResearchState(int state) =>
        state is ChoosingState or AtTheShelfState or ReadingLongState or ReadingShortState or DisturbedState
            or CelebratingState or ReturningState;

    /// <summary>One turn of a researcher. False when it is left idle, which ends the job.</summary>
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
            ChoosingState => Choose(context, system, creature, age),
            AtTheShelfState => AtTheShelf(context, system, creature),
            ReadingLongState or ReadingShortState => Read(context, system, creature, age),
            DisturbedState => Disturbed(context, system, creature, age),
            CelebratingState => Celebrate(context, system, creature, age),
            ReturningState => RoomEvictRules.BackIn(context, system, creature, RoomTypeId.Library, ChoosingState, endsAction: true),
            _ => false,
        };
    }

    /// <summary>A research item shelved in <paramref name="library"/> kicks every place-holder of it
    /// to 60 (<c>0x004E0870</c>, the loop at <c>0x004E08B0</c>), before the item is granted.</summary>
    public static void Cheer(SimulationContext context, string system, RoomInstance library)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (!c.HeldPlace.In(library) || c.Dk2StateId == CelebratingState) continue;
            c.StopMoving();
            StateShadow.TryMove(context, system, c, CelebratingState);
        }
    }

    // ----------------------------------------------------------------------------- 60

    /// <summary>60 (room-return-states.md §2): at <c>Age</c> 1 a running action ended; without art in
    /// slot 8, 0; while an action runs, 1; otherwise action 35 for <c>random(2) + 1</c> plays
    /// (simulation stream), then 1 and 61, and 0: the creature idles in the same call with the action
    /// playing, as 61's handler never runs. The hold is the counted clip, LIKELY.</summary>
    private static bool Celebrate(SimulationContext context, string system, CreatureState creature, long age)
    {
        long now = context.Tick.Value;
        if (age == 1) creature.ActionUntilTick = 0;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        if (!AnimationTimingRules.ResolvesPresent(definition, CelebrationArtSlot)) return false;
        if (now < creature.ActionUntilTick) return true;
        int plays = context.Rng.NextInt(2) + 1;
        creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, CelebrationArtSlot, plays);
        context.Emit(system, "research_cheer", $"creature={creature.Id} plays={plays} until={creature.ActionUntilTick}");
        return false;
    }

    // ----------------------------------------------------------------------------- 53

    private static bool Choose(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;
        if (age == 1) creature.StopMoving();
        if (world.Player(creature.OwnerPlayerId).ResearchTarget == 0)
        {
            // Nothing to research: action 43 once (art 20), then 0.
            if (age == 1)
                creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(database.CreatureById(creature.CreatureTypeId), 20, 0);
            return now < creature.ActionUntilTick;
        }

        if (OwnLibraryUnder(world, database, creature) is null) return true;
        if (!creature.HeldPlace.IsHeld || RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is not { } held)
            return true;

        if (Find(context, held, creature, out ObjectState? found, out (int X, int Y) point))
        {
            (int x, int y) = StandPoint(context, world, database, held, creature, found!);
            if (Walk(context, system, creature, x, y, AtTheShelfState))
                (creature.StateTargetX, creature.StateTargetY) = AutoFurnitureRules.FurniturePosition(world, database, held, found!);
            return true;
        }

        if (Walk(context, system, creature, point.X, point.Y, AtTheShelfState))
            (creature.StateTargetX, creature.StateTargetY) = point;
        return true;
    }

    /// <summary>53's stand point at an object (library-states.md §3): a quarter tile toward its wall on
    /// a wall slot; otherwise the stand-off at the object's angle, turned half round on variant 2 when
    /// <c>random(2)</c> says so.</summary>
    private static (int X, int Y) StandPoint(
        SimulationContext context, WorldState world, GameDatabase database, RoomInstance room, CreatureState creature,
        ObjectState thing)
    {
        int x = CentreX(world, thing), y = CentreY(world, thing);
        int side = AutoFurnitureRules.WallSide(world, database, room, thing.TileIndex);
        if (side >= 0)
        {
            (int dx, int dy) = Sides[side];
            return (x + ((dx * 0x1000) / 4), y + ((dy * 0x1000) / 4));
        }

        int angle = Angle(world, thing);
        if (Variant(world, thing) == 2 && context.Rng.NextInt(2) != 0) angle = (angle - 0x400) & Bearing.Mask;
        int r = StandOff(database, creature, thing);
        return (x + (int)(((long)TrapRules.Sine(angle) * r) >> 16), y + (int)(-((long)TrapRules.Cosine(angle) * r) >> 16));
    }

    /// <summary><c>0x006BC350</c>: north, east, south, west.</summary>
    private static readonly (int Dx, int Dy)[] Sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    // --------------------------------------------------------------------- the finder

    /// <summary><c>0x004E1ED0</c> (library-states.md §4): the nearest free book or bookcase; none, a
    /// random free bare tile of the held room, jittered, in <paramref name="point"/>.</summary>
    public static bool Find(
        SimulationContext context, RoomInstance room, CreatureState creature, out ObjectState? found, out (int X, int Y) point)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        found = null;
        point = (0, 0);
        uint best = uint.MaxValue;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.InHand || thing.ObjectTypeId is not (BookObjectId or BookCaseObjectId) || !EatingRules.Contains(room, thing.TileIndex))
                continue;
            (int x, int y) = AutoFurnitureRules.FurniturePosition(world, database, room, thing);
            uint distance = Distance(creature.PosX - x, creature.PosY - y);
            if (distance >= best) continue;
            if (Targeted(world, room, x, y)) continue;   // the seeker's own last target counts too
            if (thing.ObjectTypeId == BookCaseObjectId && SomeoneOnTheSpot(world, database, creature, thing)) continue;
            found = thing;
            best = distance;
        }

        if (found is not null) return true;

        // No object: a free bare tile of the held room, chosen by its rank along the chain.
        RoomInstance? held = creature.HeldPlace.IsHeld ? RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) : null;
        if (held is not null)
        {
            int[] chain = SlabChain.Order(world, held);
            var slots = new HashSet<int>();
            AutoFurnitureRules.FurnitureSlots(world, database, held, slots);   // once a search, not once a tile
            var free = new List<int>();
            foreach (int tile in chain)
                if (!slots.Contains(tile) && !TileTargeted(world, room, tile)) free.Add(tile);
            if (free.Count > 0)
            {
                int pick = free[context.Rng.NextInt(free.Count)];
                point = (CreatureState.CentreOf(pick % world.Width), CreatureState.CentreOf(pick / world.Width));
            }
        }

        int a = context.Rng.NextInt(Bearing.Turn);
        point = (point.X + (int)(((long)TrapRules.Cosine(a) << 7) >> 16), point.Y + (int)(((long)TrapRules.Sine(a) << 7) >> 16));
        return false;
    }

    private static bool IsBare(WorldState world, GameDatabase database, RoomInstance room, int tile) =>
        !AutoFurnitureRules.IsFloorSlot(world, room, tile) && AutoFurnitureRules.WallSide(world, database, room, tile) < 0;

    private static bool Targeted(WorldState world, RoomInstance room, int x, int y)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room) && HasTarget(world, c) && c.StateTargetX == x && c.StateTargetY == y)
                return true;
        return false;
    }

    private static bool TileTargeted(WorldState world, RoomInstance room, int tile)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room) && HasTarget(world, c)
                && (c.StateTargetY >> 12) * world.Width + (c.StateTargetX >> 12) == tile)
                return true;
        return false;
    }

    /// <summary>World slot <c>+0x200</c>: the target point lies on the map.</summary>
    private static bool HasTarget(WorldState world, CreatureState c) =>
        c.StateTargetX >= 0 && c.StateTargetY >= 0
        && world.Contains(new TilePos(c.StateTargetX >> 12, c.StateTargetY >> 12));

    /// <summary>A bookcase is taken while a creature stands on its stand point's tile (<c>0x00453670</c>).</summary>
    private static bool SomeoneOnTheSpot(WorldState world, GameDatabase database, CreatureState seeker, ObjectState thing)
    {
        int angle = Angle(world, thing);
        int x = thing.TileIndex % world.Width, y = thing.TileIndex / world.Width;
        if (Variant(world, thing) == 2 && AutoFurnitureRules.Choice(x, y, 2) != 0) angle = (angle - 0x400) & Bearing.Mask;
        int r = StandOff(database, seeker, thing);
        int sx = CentreX(world, thing) + (int)(((long)TrapRules.Sine(angle) * r) >> 16);
        int sy = CentreY(world, thing) + (int)(-((long)TrapRules.Cosine(angle) * r) >> 16);
        var spot = new TilePos(sx >> 12, sy >> 12);
        foreach (CreatureState c in world.Creatures)
            if (c.Tile == spot && !CreatureDeathRules.IsCorpse(c)) return true;
        return false;
    }

    /// <summary>The floor pass's angle (library-states.md §4): a book 0, a bookcase <c>(y &amp; 1) &lt;&lt; 10</c>.</summary>
    private static int Angle(WorldState world, ObjectState thing) =>
        thing.ObjectTypeId == BookCaseObjectId ? ((thing.TileIndex / world.Width) & 1) << 10 : 0;

    /// <summary>A bookcase's variant byte <c>+0x14A</c>, the coordinate-seeded <c>random(3)</c>; 0 for a book.</summary>
    private static int Variant(WorldState world, ObjectState thing) =>
        thing.ObjectTypeId == BookCaseObjectId ? AutoFurnitureRules.Choice(thing.TileIndex % world.Width, thing.TileIndex / world.Width, 3) : 0;

    // ----------------------------------------------------------------------- 54, 56, 57

    /// <summary>54: on the target's tile a bookcase, else a book, to face; neither, 53 and on anyway.
    /// Then action 29: 7 to 13 more plays and 56 at a slot, 3 to 5 and 57 on a bare tile.</summary>
    private static bool AtTheShelf(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int tile = (creature.StateTargetY >> 12) * world.Width + (creature.StateTargetX >> 12);
        ObjectState? thing = OnTile(world, tile, BookCaseObjectId) ?? OnTile(world, tile, BookObjectId);
        if (thing is null)
        {
            if (!Enter(context, system, creature, ChoosingState)) return false;
        }
        else if (RoomIndex.RoomAt(world, database, tile) is { } at
                 && CasinoStep.TurnTo(context, creature, AutoFurnitureRules.FurniturePosition(world, database, at, thing).X,
                     AutoFurnitureRules.FurniturePosition(world, database, at, thing).Y) != 0)
            return true;

        RoomInstance? room = world.Contains(new TilePos(tile % world.Width, tile / world.Width))
            ? RoomIndex.RoomAt(world, database, tile) : null;
        bool slot = room is not null && !IsBare(world, database, room, tile);
        int count = slot ? context.Rng.NextInt(7) + 7 : context.Rng.NextInt(3) + 3;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, 14, count);
        context.Emit(system, "research_read", $"creature={creature.Id} count={count} until={creature.ActionUntilTick}");
        return Enter(context, system, creature, slot ? ReadingLongState : ReadingShortState);
    }

    /// <summary>56 and 57: moved, the read ends; while it runs, 57 goes back to 53 when the finder
    /// offers an object. Ended: a room-mate it objects to and another Library with a place send it
    /// idle; otherwise 53.</summary>
    private static bool Read(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;
        CasinoStep.TurnTo(context, creature, creature.StateTargetX, creature.StateTargetY);
        bool moved = creature.WaitPosX != creature.PosX || creature.WaitPosY != creature.PosY;
        if (age > 1 && moved && now < creature.ActionUntilTick) creature.ActionUntilTick = 0;
        (creature.WaitPosX, creature.WaitPosY) = (creature.PosX, creature.PosY);
        RoomInstance? held = creature.HeldPlace.IsHeld ? RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) : null;

        if (now < creature.ActionUntilTick)
        {
            if (creature.Dk2StateId == ReadingShortState && held is not null && Find(context, held, creature, out _, out _)
                && Enter(context, system, creature, ChoosingState))
                creature.ActionUntilTick = 0;
            return true;
        }

        if (held is not null && ObjectsToARoomMate(world, database, held, creature)
            && RoomCapacityRules.FindRoomWithPlace(context, RoomTypeId.Library, creature) is { } other
            && other.TileIndices[0] != held.TileIndices[0])
            return false;
        return Enter(context, system, creature, ChoosingState);
    }

    /// <summary><c>0x004E85F0</c>: a place-holder of another creature type that this one objects to
    /// (<c>0x004946C0</c>).</summary>
    private static bool ObjectsToARoomMate(WorldState world, GameDatabase database, RoomInstance room, CreatureState creature)
    {
        CreatureFlags against = database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsGood)
            ? CreatureFlags.IsEvil : CreatureFlags.IsGood;
        foreach (CreatureState c in world.Creatures)
            if (c.HeldPlace.In(room) && c.CreatureTypeId != creature.CreatureTypeId
                && database.CreatureById(c.CreatureTypeId).Flags.HasFlag(against))
                return true;
        return false;
    }

    // ----------------------------------------------------------------------------- 59

    /// <summary>The disturbed Library, <c>0x004E88A0</c> (library-states.md §6): once a game second,
    /// each place-holder no worker within three tiles of <paramref name="intruder"/> in 56 goes to 59
    /// when its Treatment bucket is above a tenth of its unhappy bar; otherwise it is charged
    /// <c>AngerNoWork</c>.</summary>
    public static void Disturb(SimulationContext context, string system, RoomInstance room, CreatureState intruder)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.OriginalTicksPerGameSecond != 0) return;   // DK2's turn is our tick + 1
        WorldState world = context.World;
        GameDatabase database = context.Database;
        foreach (CreatureState c in world.Creatures)
        {
            if (!c.HeldPlace.In(room)) continue;
            CreatureDef definition = database.CreatureById(c.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;
            if (Distance(c.PosX - intruder.PosX, c.PosY - intruder.PosY) >= DisturbReach) continue;
            if (c.Dk2StateId != ReadingLongState) continue;
            if (c.Anger[AngerBucket.Treatment] > CreatureAngerRules.UnhappyAt(definition) * 10 / 100)
            {
                StateShadow.TryMove(context, system, c, DisturbedState);
                context.Emit(system, "research_disturbed", $"creature={c.Id} intruder={intruder.Id}");
            }
            else ChargeNoWork(world, definition, c);
        }
    }

    /// <summary>59: <c>AngerNoWork</c> every call; at its first turn the intruder to watch (action 40);
    /// then, while it lives, a turn toward it, on the second turn hurrying it out; with none, a walk to
    /// a random tile of the held room resuming 53.</summary>
    private static bool Disturbed(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        ChargeNoWork(world, definition, creature);
        if (Watch(context, world, database, creature, definition, age)) return true;

        if (creature.HeldPlace.IsHeld && RoomIndex.RoomAt(world, database, creature.HeldPlace.TileIndex) is { } held)
        {
            int[] chain = SlabChain.Order(world, held);
            int tile = chain[context.Rng.NextInt(chain.Length)];
            Walk(context, system, creature, CreatureState.CentreOf(tile % world.Width), CreatureState.CentreOf(tile / world.Width), ChoosingState);
        }

        return true;
    }

    /// <summary><c>0x00478F00</c> after its charge: whether the watch goes on.</summary>
    private static bool Watch(
        SimulationContext context, WorldState world, GameDatabase database, CreatureState creature, CreatureDef definition, long age)
    {
        long now = context.Tick.Value;
        if (age == 1)
        {
            if (!world.Contains(creature.Tile)) return false;
            int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
            if (RoomIndex.RoomAt(world, database, here) is not { } room || Intruder(world, database, room, creature) is not { } intruder)
                return false;
            creature.WatchedCreatureId = intruder.Id;
            creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, 5, 0);
            return true;
        }

        if (world.CreatureById(creature.WatchedCreatureId) is not { } watched || CreatureDeathRules.IsCorpse(watched)) return false;
        int left = CasinoStep.TurnTo(context, creature, watched.PosX, watched.PosY);
        if (left < 0x80 && age == 2)
        {
            // 0x00478F00: twice the intruder's current per-tick speed (vtable +0x90, 0x0048D000) as the percent.
            CreatureDef theirs = database.CreatureById(watched.CreatureTypeId);
            int current = CreatureMovementSystem.MaxSpeedOf(database, theirs, watched.Level, watched.Running) >> 4;
            StatusEffectRules.ModifySpeed(context, watched, 2 * current, 2 * SimulationTiming.OriginalTicksPerGameSecond);
        }

        return now < creature.ActionUntilTick;
    }

    /// <summary><c>0x004E8A30</c>: the nearest creature on the room's tiles that holds no place, or one
    /// in another Library while not researching.</summary>
    private static CreatureState? Intruder(WorldState world, GameDatabase database, RoomInstance room, CreatureState watcher)
    {
        CreatureState? best = null;
        uint bestDistance = uint.MaxValue;
        foreach (CreatureState c in world.Creatures)
        {
            if (CreatureDeathRules.IsCorpse(c) || !world.Contains(c.Tile) || !room.Contains((c.Tile.Y * world.Width) + c.Tile.X)) continue;
            bool counts = !c.HeldPlace.IsHeld
                || (!c.HeldPlace.In(room) && RoomIndex.RoomAt(world, database, c.HeldPlace.TileIndex) is { } r
                    && r.Definition.TypeId == RoomTypeId.Library && ResolvedCategory(c) != CreatureStateCategory.Research);
            if (!counts) continue;
            uint distance = Distance(c.PosX - watcher.PosX, c.PosY - watcher.PosY);
            if (distance >= bestDistance) continue;
            best = c;
            bestDistance = distance;
        }

        return best;
    }

    // --------------------------------------------------------------------------- shared

    /// <summary><c>0x00478B80(3, AngerNoWork, 3)</c>.</summary>
    private static void ChargeNoWork(WorldState world, CreatureDef definition, CreatureState creature) =>
        CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Work, definition.AngerNoWork, AngerComplaint.NoWork);

    public static CreatureStateCategory? ResolvedCategory(CreatureState creature)
    {
        if (!CreatureStateMachine.HasState(creature)) return null;
        int state = StateShadow.IsTravelling(creature) ? creature.UtilityNewState : creature.Dk2StateId;
        return CreatureStateTable.Get(state).Category;
    }

    private static RoomInstance? OwnLibraryUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!world.Contains(creature.Tile)) return null;
        RoomInstance? room = RoomIndex.RoomAt(world, database, (creature.Tile.Y * world.Width) + creature.Tile.X);
        return room is not null && room.Definition.TypeId == RoomTypeId.Library && room.OwnerPlayerId == creature.OwnerPlayerId
            ? room : null;
    }

    private static ObjectState? OnTile(WorldState world, int tile, byte type)
    {
        foreach (ObjectState thing in world.Objects)
            if (!thing.InHand && thing.TileIndex == tile && thing.ObjectTypeId == type) return thing;
        return null;
    }

    internal static int StandOff(GameDatabase database, CreatureState creature, ObjectState thing)
    {
        int width = database.ObjectById(thing.ObjectTypeId) is { } def ? (ushort)def.Width.Raw : 0;
        int footprint = (ushort)database.CreatureById(creature.CreatureTypeId).FootprintRadius.Raw;
        return ((width >> 1) + (footprint >> 1)) * 100 / 100;
    }

    private static int CentreX(WorldState world, ObjectState thing) => CreatureState.CentreOf(thing.TileIndex % world.Width);

    private static int CentreY(WorldState world, ObjectState thing) => CreatureState.CentreOf(thing.TileIndex / world.Width);

    private static uint Distance(int dx, int dy)
    {
        long ax = Math.Abs((long)dx), ay = Math.Abs((long)dy);
        return unchecked((uint)((ax * ax) >> 12) + (uint)((ay * ay) >> 12));
    }

    private static bool Walk(SimulationContext context, string system, CreatureState creature, int x, int y, int resume)
    {
        if (!context.Navigation.TryRouteTo(creature, x, y, MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        context.Emit(system, "research_walk", $"creature={creature.Id} to=({x >> 12},{y >> 12}) resume={resume}");
        return true;
    }

    private static bool Enter(SimulationContext context, string system, CreatureState creature, int state)
    {
        StateShadow.TryMove(context, system, creature, state);
        return creature.Dk2StateId != CreatureStateMachine.Idle;
    }
}
