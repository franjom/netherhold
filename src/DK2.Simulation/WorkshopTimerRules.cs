// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>One Workshop's four countdowns, in the order the room constructor draws them.</summary>
public readonly record struct WorkshopTimers(int Steam, int Smoke, int LavaBubbling, int LavaSpill);

/// <summary>The Workshop's effect timers: the room constructor <c>0x004F4A60</c>, which seeds four
/// countdowns with <c>random(4 x ticks a second)</c> from the world stream, and the handler's
/// <c>0x004F4B20</c>, which counts each down a turn and, at zero, reloads it with
/// <c>random(16 x ticks) + 8 x ticks</c> and asks for an effect (workshop-timers.md). The draws are what
/// matter here: a reload draws, and so do a slot pick and a Steel Bucket count, whether or not an effect
/// or a sound follows. The effects are requests; the sounds (<c>0x2E9</c>, <c>0x2E8</c>) are not built.</summary>
/// <remarks>Ours differs in two ways. The room's furniture list leaves out the Workshop Pillars (object 80),
/// which only the picks of the second timer could land on. And the mesh point the second timer's effect sits at
/// (<c>0x2712</c>, unread) is taken to be there, at the object's own floor position. The rooms are ticked in
/// DK2's order by <see cref="Systems.RoomTickSystem"/>.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-timers.md")]
public static class WorkshopTimerRules
{
    /// <summary>The Steel Bucket, object 16 (<c>0x10</c> in the handler's type tests).</summary>
    public const byte SteelBucketObjectId = 16;

    /// <summary>The constructor's draw range, in game seconds (<c>4 x ticks a second</c>).</summary>
    public const int StartRangeSeconds = 4;

    /// <summary>A reload's draw range and floor, in game seconds: sixteen drawn plus eight.</summary>
    public const int ReloadRangeSeconds = 16, ReloadFloorSeconds = 8;

    /// <summary>The height DK2 puts the floor and wall effects at: one tile (<c>0x1000</c>).</summary>
    public const int SlotEffectHeight = Fp32.One;

    /// <summary>The wall effect's inset from the slot's centre toward its wall.</summary>
    public const int WallInset = 0x700;

    private const string SystemName = "workshop timers";
    private const int Ticks = SimulationTiming.OriginalTicksPerGameSecond;

    /// <summary>The room constructor's draws (<c>Workshop.cpp</c> lines 0x22 to 0x25): steam
    /// (<c>+0x49</c>), smoke (<c>+0x4D</c>), lava bubbling (<c>+0x51</c>) then lava spill (<c>+0x55</c>),
    /// each <c>random(4 x ticks)</c>.</summary>
    public static WorkshopTimers Construct(SimRandom rng) =>
        new(rng.NextInt(StartRangeSeconds * Ticks), rng.NextInt(StartRangeSeconds * Ticks),
            rng.NextInt(StartRangeSeconds * Ticks), rng.NextInt(StartRangeSeconds * Ticks));

    /// <summary>Puts a room's countdowns on its lowest tile and clears them from the rest of
    /// <paramref name="chain"/>.</summary>
    internal static void Place(WorldState world, IReadOnlyList<int> chain, WorkshopTimers timers)
    {
        int lowest = int.MaxValue;
        foreach (int tile in chain)
        {
            ref TileState t = ref world.TileAtIndex(tile);
            (t.WorkshopSteamTimer, t.WorkshopSmokeTimer, t.WorkshopLavaBubblingTimer, t.WorkshopLavaSpillTimer) = (0, 0, 0, 0);
            lowest = Math.Min(lowest, tile);
        }

        ref TileState head = ref world.TileAtIndex(lowest);
        (head.WorkshopSteamTimer, head.WorkshopSmokeTimer, head.WorkshopLavaBubblingTimer, head.WorkshopLavaSpillTimer) =
            (timers.Steam, timers.Smoke, timers.LavaBubbling, timers.LavaSpill);
    }

    internal static WorkshopTimers Read(WorldState world, int tileIndex)
    {
        ref TileState t = ref world.TileAtIndex(tileIndex);
        return new WorkshopTimers(t.WorkshopSteamTimer, t.WorkshopSmokeTimer, t.WorkshopLavaBubblingTimer, t.WorkshopLavaSpillTimer);
    }

    /// <summary>Whether a room of this definition is a Workshop.</summary>
    internal static bool IsWorkshop(RoomDef? room) => room?.TypeId == RoomTypeId.Workshop;

    /// <summary>The rooms a level's map starts with are constructed as the world is made, row by row
    /// (<see cref="RoomLists.Initialize"/> records them); DK2 loads a level after setting the seed, so
    /// their draws open the stream. A room merged into another still drew, and its timers went with it.</summary>
    internal static void InitializeLevelStart(WorldState world, SimRandom rng)
    {
        foreach (int lowestTile in world.LevelStartWorkshops)
        {
            WorkshopTimers timers = Construct(rng);
            if (lowestTile >= 0) Place(world, [lowestTile], timers);
        }

        world.LevelStartWorkshops.Clear();
    }

    /// <summary>The room's handler, <c>0x004F4B20</c>, after the hated-company step: the steam timer,
    /// the lava-spill timer, the smoke timer, then the lava-bubbling timer, each one a countdown that
    /// reloads on reaching zero.</summary>
    internal static void Tick(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        ref TileState timers = ref world.TileAtIndex(room.TileIndices[0]);
        RoomDef definition = room.Definition;

        // Steam (+0x49), Workshop.cpp:0x39: reload, then, if the room has the effect, one floor slot.
        if (timers.WorkshopSteamTimer != 0) timers.WorkshopSteamTimer--;
        else
        {
            timers.WorkshopSteamTimer = Reload(context.Rng);
            if (definition.RoomEffect425Id != 0 && FloorSlot(context, room) is int floor)
            {
                int x = CreatureState.CentreOf(floor % world.Width), y = CreatureState.CentreOf(floor / world.Width);
                Request(context, room, definition.RoomEffect425Id, x, y, SlotEffectHeight, default);
                // Sound event 0x2E9 goes here (0x004F4C27), at the effect's position.
            }
        }

        // Lava spill (+0x55), :0x5D: reload, then the count of Steel Buckets and a pick among them
        // (:0x6C), which walks the whole furniture list, not the buckets.
        if (timers.WorkshopLavaSpillTimer != 0) timers.WorkshopLavaSpillTimer--;
        else
        {
            timers.WorkshopLavaSpillTimer = Reload(context.Rng);
            List<ObjectState> furniture = Furniture(context, room);
            int buckets = Buckets(furniture);
            if (buckets != 0)
            {
                int pick = context.Rng.NextInt(buckets);
                if (pick < furniture.Count
                    && context.Database.ObjectById(furniture[pick].ObjectTypeId) is { IdleEffectId: not 0 } picked)
                {
                    // DK2 asks the object's mesh point 0x2712 (0x0059D380) for a three-dimensional point, owns the
                    // effect by the object's own owner word (0x004F4D7B) and heads it along (point - the object's
                    // position) & 0x7FF (0x004F4D4C..0x004F4D6B). Marked deviation: the simulation has no mesh
                    // point, so ours sits at the object's floor position at height 0 with heading 0 (Q733a).
                    (int x, int y) = AutoFurnitureRules.FurniturePosition(world, context.Database, room, furniture[pick]);
                    context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(picked.IdleEffectId), furniture[pick].OwnerPlayerId,
                        new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(0)), default));
                    // Sound event 0x2E9 goes here (0x004F4DC7), at the object's mesh point.
                }
            }
        }

        // Smoke (+0x4D), :0xA0: reload, then, with the effect, one wall slot.
        if (timers.WorkshopSmokeTimer != 0) timers.WorkshopSmokeTimer--;
        else
        {
            timers.WorkshopSmokeTimer = Reload(context.Rng) & 0xFFFF;
            if (definition.RoomEffect427Id != 0 && WallSlot(context, room) is (int wall, int side))
            {
                int x = CreatureState.CentreOf(wall % world.Width), y = CreatureState.CentreOf(wall / world.Width);
                (int dx, int dy) = side switch { 0 => (0, -WallInset), 1 => (WallInset, 0), 2 => (0, WallInset), _ => (-WallInset, 0) };
                Request(context, room, definition.RoomEffect427Id, x + dx, y + dy, SlotEffectHeight, new Dk2Angle((ushort)((side & 3) << 9)));
                // Sound event 0x2E8 goes here (0x004F4F03), at the effect's position.
            }
        }

        // Lava bubbling (+0x51), :0xD0: reload, then the bucket count and a pick (:0xE3) among the buckets
        // themselves; the effect (no sound) sits at the bucket.
        if (timers.WorkshopLavaBubblingTimer != 0) timers.WorkshopLavaBubblingTimer--;
        else
        {
            timers.WorkshopLavaBubblingTimer = Reload(context.Rng);
            List<ObjectState> furniture = Furniture(context, room);
            int buckets = Buckets(furniture);
            if (buckets != 0)
            {
                int pick = context.Rng.NextInt(buckets);
                if (definition.RoomEffect429Id != 0)
                {
                    ObjectState bucket = furniture.Where(o => o.ObjectTypeId == SteelBucketObjectId).ElementAt(pick);
                    (int x, int y) = AutoFurnitureRules.FurniturePosition(world, context.Database, room, bucket);
                    Request(context, room, definition.RoomEffect429Id, x, y, 0, default);
                }
            }
        }
    }

    /// <summary>A reload: <c>random(16 x ticks a second) + 8 x ticks a second</c>.</summary>
    private static int Reload(SimRandom rng) =>
        rng.NextInt(ReloadRangeSeconds * Ticks) + (ReloadFloorSeconds * Ticks);

    /// <summary>The room's auto-placed objects, newest first (<c>room+0xE</c>, pushed at the head by
    /// <c>0x004E6220</c>), whoever owns them. Ordered by object id: DK2's list order is insertion order and its
    /// thing slots are reused, but ours are not (<see cref="ObjectState.Id"/>, never reused), so the id is the
    /// insertion order. Pillars (object 80) are not in ours.</summary>
    private static List<ObjectState> Furniture(SimulationContext context, RoomInstance room)
    {
        var list = new List<ObjectState>();
        foreach (ObjectState thing in context.World.Objects)
            if (thing.AutoPlaced && !thing.InHand && room.Contains(thing.TileIndex))
                list.Add(thing);
        list.Sort((a, b) => b.Id.CompareTo(a.Id));
        return list;
    }

    private static int Buckets(List<ObjectState> furniture)
    {
        int count = 0;
        foreach (ObjectState thing in furniture)
            if (thing.ObjectTypeId == SteelBucketObjectId) count++;
        return count;
    }

    /// <summary><c>0x004E8210(1, 0)</c>: with a floor slot, one draw over the slots and the chosen
    /// tile (the chain's nth); with none, no draw.</summary>
    private static int? FloorSlot(SimulationContext context, RoomInstance room)
    {
        int[] slots = AutoFurnitureRules.FloorSlotsInChainOrder(context.World, room);
        return slots.Length == 0 ? null : slots[context.Rng.NextInt(slots.Length)];
    }

    /// <summary><c>0x004E8210(0, 1)</c>: the same over the wall slots, with the side of the wall.</summary>
    private static (int Tile, int Side)? WallSlot(SimulationContext context, RoomInstance room)
    {
        (int Tile, int Side)[] slots = AutoFurnitureRules.WallSlotsInChainOrder(context.World, context.Database, room);
        return slots.Length == 0 ? null : slots[context.Rng.NextInt(slots.Length)];
    }

    private static void Request(SimulationContext context, RoomInstance room, ushort effect, int x, int y, int z, Dk2Angle heading) =>
        context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(effect), room.OwnerPlayerId,
            new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(z)), heading));
}
