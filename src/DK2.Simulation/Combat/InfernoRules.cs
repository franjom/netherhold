// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>Shot process 30, <c>0x004AF660</c>: the keeper's Inferno (keeper-spell-inferno-chicken.md §4).
/// A shot that never moves runs it before every move of its 30-tick life, and each run hurts every
/// creature of <b>any</b> owner on a non-solid tile strictly within data2 tiles by data1, sparing a
/// kind that walks on lava and cannot fly. The owner's own casts confirmed the owner test's absence
/// and the exemption (Q498).</summary>
/// <remarks>Not modelled: bridge command 10 and the <c>0x004B32A0</c> call, whose bound is zero for data2 below
/// 4096. DK2 clamps the tile box's upper edge to the map width and height inclusively, one past the
/// last tile; ours clamps to the last tile, which differs only on a map edge that shipped maps keep
/// as rock.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-inferno-chicken.md")]
public static class InfernoRules
{
    private const string SystemName = "shots";

    public static void Burn(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int radius = shot.Data2;
        int scaled = radius << 12;
        // Thirty-two bits, as DK2 computes it, and halved toward zero on the shot's first tick.
        int bound = unchecked(scaled * scaled) >> 12;
        if (shot.Age == 1) bound /= 2;

        int cx = shot.PosX >> 12, cy = shot.PosY >> 12;
        int x0 = Math.Max(cx - radius, 0), y0 = Math.Max(cy - radius, 0);
        int x1 = Math.Min(cx + radius, world.Width - 1), y1 = Math.Min(cy + radius, world.Height - 1);

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState target = creatures[i];
            // On a tile's thing list: not in a hand or carried, and a creature rather than a body.
            if (target.Activity == CreatureActivity.Dead || !CombatPitRules.IsOnTheMap(target)) continue;
            TilePos tile = target.Tile;
            if (tile.X < x0 || tile.X > x1 || tile.Y < y0 || tile.Y > y1) continue;
            // World slot +0x208: the navigation grid's solid bit at the tile.
            if ((context.Navigation.NavByte(tile.X, tile.Y) & 8) != 0) continue;
            CreatureDef definition = database.CreatureById(target.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.CanWalkOnLava) && !definition.Flags.HasFlag(CreatureFlags.CanFly)) continue;
            long dx = Math.Abs((long)target.PosX - shot.PosX), dy = Math.Abs((long)target.PosY - shot.PosY);
            uint distance = unchecked((uint)((dx * dx) >> 12) + (uint)((dy * dy) >> 12));
            if (distance >= (uint)bound) continue;
            StatusEffectRules.SetBurning(target, 1, shot.OwnerPlayerId);
            // 0x004B2CE0 refuses a dying creature; it tests no owner and no invulnerability.
            // The refusal comes after the source stamp, so a dying creature still takes the caster's owner (0x004AF838).
            if (CreatureDeathRules.IsDown(target)) { target.HealthSourcePlayerId = shot.OwnerPlayerId; continue; }
            int landed = StatusEffectRules.Hurt(world, database, definition, target, shot.Data1, shot.OwnerPlayerId);
            context.Emit(SystemName, "inferno_hit",
                $"shot={shot.Id} target={target.Id} owner={target.OwnerPlayerId} damage={shot.Data1} landed={landed} health={target.Health}");
        }
    }
}
