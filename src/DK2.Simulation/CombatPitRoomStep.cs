// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
namespace DK2.Simulation;

internal readonly record struct CombatPitRoomRuntime(byte ShortTicks, byte LongTicks, byte PreviousStandingCount, long FightSeenTick);

/// <summary>The Combat Pit room handler, including its effect timers and fight-over message (0x004D7A60).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit.md")]
public sealed class CombatPitRoomStep : IRoomTickStep
{
    public string Name
    {
        get
        {
            return "combat pit room";
        }
    }

    public bool Applies(SimulationContext context, RoomInstance room)
    {
        return room.Definition.TypeId == RoomTypeId.CombatPit;
    }

    public void Execute(SimulationContext context)
    {
        RoomTickSystem.RunAlone(context, this);
    }

    public void Tick(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        ref TileState state = ref world.TileAtIndex(room.TileIndices[0]);
        Advance(context, room, ref state.PitAmbientShortTicks, 4, room.Definition.RoomEffect421Id, walls: true);
        Advance(context, room, ref state.PitAmbientLongTicks, 32, room.Definition.CreatedObjectEffectId, walls: false);
        int standing = 0, down = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (!CreatureStateMachine.HasState(creature) || !CombatPitRules.IsOnTheMap(creature))
            {
                continue;
            }

            int tile = creature.Tile.Y * world.Width + creature.Tile.X;
            if (!CombatPitRules.IsFloor(room, tile))
            {
                continue;
            }

            if (CreatureDeathRules.IsDying(creature))
            {
                down++;
            }
            else
            {
                standing++;
            }
        }
        if (standing == 1 && state.PitPreviousStandingCount > 1 && down != 0)
        {
            world.Messages.Post(world, room.OwnerPlayerId, PlayerMessageKind.CombatPitFightOver, 0, 0);
        }

        state.PitPreviousStandingCount = unchecked((byte)standing);
    }

    private void Advance(SimulationContext context, RoomInstance room, ref byte ticks, int seconds, ushort effect, bool walls)
    {
        if (ticks != 0) { ticks--; return; }
        ticks = unchecked((byte)context.Rng.NextInt(CombatPitRules.NativeTicksPerGameSecond(context.Database) * seconds));
        if (effect == 0)
        {
            return;
        }

        int[] slots;
        if (walls)
        {
            var accepted = new HashSet<int>();
            AutoFurnitureRules.FurnitureSlots(context.World, context.Database, room, accepted);
            slots = [.. SlabChain.Order(context.World, room).Where(accepted.Contains)];
        }
        else
        {
            slots = AutoFurnitureRules.FloorSlotsInChainOrder(context.World, room);
        }

        if (slots.Length == 0)
        {
            return;
        }

        int chosen = slots[context.Rng.NextInt(slots.Length)];
        int x = CreatureState.CentreOf(chosen % context.World.Width), y = CreatureState.CentreOf(chosen / context.World.Width);
        context.RequestEffect(Name, new ViewEffectRequest(new EffectId(effect), room.OwnerPlayerId,
            new FpPosition3(new(x), new(y), new(FloorHeightRules.At(context.World, context.Database, x, y)))));
    }

    /// <summary>0x004581F0: the second room effect at the destroyed creature's absolute position.</summary>
    public static void CreatureDestroyed(SimulationContext context, CreatureState creature)
    {
        CreatureDestroyed(context.Hooks, creature);
    }

    public static void CreatureDestroyed(StateHookContext context, CreatureState creature)
    {
        int tile = creature.Tile.Y * context.World.Width + creature.Tile.X;
        if (CombatPitRules.PitAt(context.World, context.Database, tile) is not { } room
            || room.Definition.CreatedObjectEffectId == 0)
        {
            return;
        }
        // The foreign-owner helper 0x004D83D0 only queries a floor and discards the result.
        context.RequestEffect("combat pit room", new ViewEffectRequest(new EffectId(room.Definition.CreatedObjectEffectId), creature.OwnerPlayerId,
            new FpPosition3(new(creature.PosX), new(creature.PosY), new(creature.PosZ + FloorHeightRules.Ordinary)),
            new Dk2Angle((ushort)creature.Heading)));
    }

    internal static CombatPitRoomRuntime Read(WorldState world, int lowestTile)
    {
        ref TileState tile = ref world.TileAtIndex(lowestTile);
        return new(tile.PitAmbientShortTicks, tile.PitAmbientLongTicks, tile.PitPreviousStandingCount, tile.PitFightSeenTick);
    }

    internal static void Place(WorldState world, IReadOnlyList<int> chain, CombatPitRoomRuntime state)
    {
        int lowest = int.MaxValue;
        foreach (int tile in chain)
        {
            ref TileState cleared = ref world.TileAtIndex(tile);
            (cleared.PitAmbientShortTicks, cleared.PitAmbientLongTicks, cleared.PitPreviousStandingCount, cleared.PitFightSeenTick) = (0, 0, 0, 0);
            lowest = Math.Min(lowest, tile);
        }
        ref TileState first = ref world.TileAtIndex(lowest);
        (first.PitAmbientShortTicks, first.PitAmbientLongTicks, first.PitPreviousStandingCount, first.PitFightSeenTick) =
            (state.ShortTicks, state.LongTicks, state.PreviousStandingCount, state.FightSeenTick);
    }
}
