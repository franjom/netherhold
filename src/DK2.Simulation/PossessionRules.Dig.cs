// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>Action 26's dig and claim arm (<c>0x00514E1D</c>–<c>0x00515037</c>,
/// possession-arms-and-inputs.md §3): a melee press that found no target, by a worker.</summary>
public static partial class PossessionRules
{
    /// <summary><c>0x006BC350</c>: the facing tile's step by the packet yaw, a quarter turn a row,
    /// north first and again last.</summary>
    private static readonly (int Dx, int Dy)[] FacingSteps = [(0, -1), (1, 0), (0, 1), (-1, 0), (0, -1)];

    /// <summary>Definition <c>+0x686</c> bit 0 clear does nothing. The pose's tile first: touching the
    /// worker's own floor and not its own, an ally's tile ends the press; plain path is claimed
    /// (action 4), another keeper's claimed floor attacked (action 6). Otherwise the tile the yaw
    /// faces: gold is mined (2), a diggable wall dug (1), other rock only flashes (presentation). The
    /// begin replaces any running action. No reinforce, no wall claim, no task marked.</summary>
    internal static void DigOrClaim(SimulationContext context, CreatureState me, CreatureDef mine, PossessionAim aim)
    {
        if (!mine.Flags.HasFlag(CreatureFlags.IsWorker)) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        PlayerId owner = me.OwnerPlayerId;
        (int px, int py, _) = aim.PacketOrigin;
        var pose = new TilePos(px >> 12, py >> 12);

        if (world.Contains(pose))
        {
            int at = (pose.Y * world.Width) + pose.X;
            ref TileState tile = ref world.TileAtIndex(at);
            if (tile.OwnerPlayerId != owner && OwnFloorRules.Touches(database, world, pose, owner))
            {
                if (!TileWorkRules.IsNeutralTile(tile) && world.HasPlayer(tile.OwnerPlayerId)
                    && world.AreAllied(owner, tile.OwnerPlayerId)) return;
                if (TileWorkRules.IsClaimablePath(database, tile))
                {
                    BeginWork(context, me);
                    me.ClaimActionTile = at;
                    context.Emit(ActSystem, "possessed_claim", $"creature={me.Id} tile={at}");
                    return;
                }
                // 0x00454000, which Applies(AttackTile) mirrors.
                if (TileWorkRules.Applies(database, tile, TileWorkKind.AttackTile, owner))
                {
                    BeginWork(context, me);
                    world.QueueWork(new TileWorkOrder(at, TileWorkKind.AttackTile, owner, me.Id,
                        Strike: TileWorkStrike.NoExperience | TileWorkStrike.ClearsWorkerTile));
                    context.Emit(ActSystem, "possessed_convert", $"creature={me.Id} tile={at}");
                    return;
                }
            }
        }

        (int dx, int dy) = FacingSteps[(ushort)(aim.Yaw.Value + 0x100) >> 9];
        var facing = new TilePos((px >> 12) + dx, (py >> 12) + dy);
        if (!world.Contains(facing)) return;
        int index = (facing.Y * world.Width) + facing.X;
        ref TileState face = ref world.TileAtIndex(index);
        TerrainDef terrain = database.TerrainById(face.TerrainId);
        if (TileWorkRules.IsMinableGold(database, face))
        {
            BeginWork(context, me);
            world.QueueWork(new TileWorkOrder(index, TileWorkKind.MineGold, owner, me.Id, PossessedMultiplier(database, me, mine)));
            context.Emit(ActSystem, "possessed_mine", $"creature={me.Id} tile={index}");
        }
        else if (TileWorkRules.IsDiggableWall(terrain))
        {
            // Action 1 tests its own guard and picks the rate by the wall's owner each strike.
            BeginWork(context, me);
            bool imp = me.CreatureTypeId == CombatRules.ImpCreatureTypeId;
            world.QueueWork(new TileWorkOrder(index, TileWorkKind.Dig, owner, me.Id,
                imp ? PossessedMultiplier(database, me, mine) : 1,
                TileWorkStrike.DiggableWallGuard | (imp ? TileWorkStrike.None : TileWorkStrike.NonImpDig)));
            context.Emit(ActSystem, "possessed_dig", $"creature={me.Id} tile={index}");
        }
        else if (terrain.IsSolid)
            context.Emit(ActSystem, "possessed_rock", $"creature={me.Id} tile={index}");
    }

    /// <summary>A begun action replaces the running one: a swing or cast not yet landed, or a claim.</summary>
    private static void BeginWork(SimulationContext context, CreatureState me)
    {
        me.SwingInProgress = false;
        me.ClaimActionTile = -1;
    }

    /// <summary>Actions 1 (an Imp's), 2 and 4 multiply the floored delta by
    /// <c>Combat_MeleeRechargeTicks</c> (<c>0x00494EF0</c>) while the worker is possessed, so one
    /// press is worth the AI strikes of one recharge.</summary>
    internal static int PossessedMultiplier(GameDatabase database, CreatureState me, CreatureDef mine) =>
        me.Possessed ? CombatRules.RechargeTicks(database, me, CombatRules.TicksOf(mine.MeleeRecharge)) : 1;

    /// <summary>Action 4's runner (<c>0x004974C0</c>, mode 3): while the begin tile is claimable, one
    /// strike every tick, each paying experience, until it converts. On shipped data one possessed
    /// strike converts a Dirt Path, so it rarely runs twice. Ours runs it in the possession pass and
    /// ends it when the creature leaves possession, is down or is held (what DK2's action does then
    /// is unread).</summary>
    internal static void ClaimTurn(SimulationContext context, CreatureState c)
    {
        if (c.ClaimActionTile < 0) return;
        WorldState world = context.World;
        if ((uint)c.ClaimActionTile >= (uint)world.TileCount || CreatureDeathRules.IsDown(c) || HandRules.IsHeld(c)
            || !TileWorkRules.IsClaimablePath(context.Database, world.TileAtIndex(c.ClaimActionTile)))
        {
            c.ClaimActionTile = -1;
            return;
        }
        CreatureDef mine = context.Database.CreatureById(c.CreatureTypeId);
        world.QueueWork(new TileWorkOrder(c.ClaimActionTile, TileWorkKind.Claim, c.OwnerPlayerId, c.Id,
            PossessedMultiplier(context.Database, c, mine)));
    }
}
