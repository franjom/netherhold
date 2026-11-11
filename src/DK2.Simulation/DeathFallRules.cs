// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The heading a collapsing creature falls along (<c>0x0048F410</c>, called twice by the
/// state-216 entries <c>0x00459560</c> and <c>0x004596D0</c>): world <c>random(0x800)</c> drawn until
/// the value lies in an even sixteenth of the circle whose direction the neighbouring tiles leave
/// open. Not applied: the definition's rotation (<c>+0x766</c>, 0 in every shipped record) and the second
/// pick's word <c>+0x102</c>, which ours draws and drops.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/death-fall-heading.md")]
public static class DeathFallRules
{
    private const int HeadingRange = 0x800;

    /// <summary>The two picks of a state-216 entry, in DK2's order: the heading, then <c>+0x102</c>.</summary>
    public static void Enter(SimulationContext context, CreatureState creature)
    {
        creature.Heading = Pick(context, creature);
        Pick(context, creature);
    }

    /// <summary>Art slot 7, the collapse's action 62 (<c>0x0048B828</c>).</summary>
    private const int CollapseArtSlot = 7;

    /// <summary>The turn state 216's handler first finds the collapse's action over. Fitted: Level 5's
    /// Wizard and Thief, both felled by a shot after the creature sweep and both with five turns of hold,
    /// collapse on 1720 and 1864 and lie on 1726 and 1870, which the hold plus one gives. A creature
    /// felled before its own update, or one with another hold, is not in any capture.</summary>
    [Dk2Behavior(Confidence.Hypothesis, "docs/research/death-fall-heading.md")]
    public static long FallEndsAt(SimulationContext context, CreatureDef definition) =>
        context.Tick.Value + 1 + AnimationTimingRules.ActionHoldTicks(
            definition.ArtSlot(AnimationTimingRules.ResolveArtSlot(definition, CollapseArtSlot)));

    /// <summary>State 216's handler (<c>0x00459850</c>): it waits for the action, enters 217 and steps
    /// the body back along its heading by vector 3's y, onto a tile that is not solid and has its own
    /// tile's navigation byte. Not built: the stamp at state <c>+0xAB</c> (the turn plus ten).</summary>
    public static void Fall(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (context.Tick.Value < creature.ActionUntilTick) return;
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureDeathRules.DyingState, context.Tick.Value)) return;
        int back = unchecked(-definition.DeathFallStepY);
        int x = unchecked(creature.PosX + ((TrapRules.Sine(creature.Heading) * back) >> 16));
        int y = unchecked(creature.PosY - ((TrapRules.Cosine(creature.Heading) * back) >> 16));
        WorldState world = context.World;
        var target = new TilePos(x >> 12, y >> 12);
        TilePos own = creature.Tile;
        if (!world.Contains(target) || !world.Contains(own)
            || (context.Database.TerrainById(world.Tile(target).TerrainId).Flags & TerrainFlags.Solid) != 0) return;
        context.Navigation.Refresh();
        if (context.Navigation.NavByte(target.X, target.Y) != context.Navigation.NavByte(own.X, own.Y)) return;
        creature.PosX = creature.LastPosX = x;
        creature.PosY = creature.LastPosY = y;
        creature.LastPosZ = creature.PosZ;
    }

    /// <summary>One pick. With the four orthogonal neighbours all blocked the heading stays and nothing
    /// is drawn. A diagonal sector is blocked by its own tile or either orthogonal tile beside it.</summary>
    public static int Pick(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        TilePos at = creature.Tile;
        int mode = Mode(context, world.TileIndex(at));
        bool n = Blocked(context, at.X, at.Y - 1, mode), e = Blocked(context, at.X + 1, at.Y, mode);
        bool s = Blocked(context, at.X, at.Y + 1, mode), w = Blocked(context, at.X - 1, at.Y, mode);
        if (n && e && s && w) return creature.Heading;

        Span<bool> sector =
        [
            n, Blocked(context, at.X + 1, at.Y - 1, mode) || n || e,
            e, Blocked(context, at.X + 1, at.Y + 1, mode) || e || s,
            s, Blocked(context, at.X - 1, at.Y + 1, mode) || s || w,
            w, Blocked(context, at.X - 1, at.Y - 1, mode) || w || n,
        ];
        while (true)
        {
            int drawn = context.Rng.NextInt(HeadingRange);
            int sixteenth = ((drawn + 0x20) & (HeadingRange - 1)) >> 7;
            if ((sixteenth & 1) == 0 && !sector[sixteenth >> 1]) return drawn;
        }
    }

    /// <summary><c>0x00455CA0</c>: 0 off a Temple or Combat Pit; on one, 2 for an interior cell the
    /// room's floor-slot rule accepts and 1 otherwise.</summary>
    private static int Mode(SimulationContext context, int tileIndex)
    {
        RoomInstance? room = RoomIndex.RoomAt(context.World, context.Database, tileIndex);
        if (room is null || room.Definition.TypeId is not (RoomTypeId.Temple or RoomTypeId.CombatPit)) return 0;
        return AutoFurnitureRules.IsFloorSlot(context.World, room, tileIndex) ? 2 : 1;
    }

    /// <summary><c>0x00455CE0</c>: in mode 2 a room's rim blocks and in mode 1 its interior does; any
    /// other tile blocks by its terrain's solid bit. A tile off the map blocks.</summary>
    private static bool Blocked(SimulationContext context, int x, int y, int mode)
    {
        WorldState world = context.World;
        if (!world.Contains(x, y)) return true;
        int tileIndex = (y * world.Width) + x;
        if (mode != 0 && RoomIndex.RoomAt(world, context.Database, tileIndex) is { } room)
            return room.IsInterior(tileIndex) != (mode == 2);
        return context.Database.TerrainById(world.Tile(x, y).TerrainId).IsSolid;
    }
}
