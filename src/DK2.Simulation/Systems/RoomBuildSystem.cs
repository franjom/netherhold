// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="BuildRoomCommand"/>s: turns the player's own
/// floor into room tiles and takes the gold out of their treasury. Where a room
/// may go and what it costs are <see cref="RoomRules"/>; where the gold comes
/// from is <see cref="GoldStore"/>.
/// <para><b>A tile at a time, in the order the command gives</b>: a keeper who
/// can afford four tiles of a six-tile treasury gets four, and the run says
/// which two were refused and why. Ours — neither ordering is measured — except that a
/// bridge order is marked whole first, as DK2's drag is (<see cref="RoomRules.SpannableTiles"/>).</para>
/// <para><b>Deliberate deviation: building is instant</b>; no imp task kind for room
/// construction has been identified (imp-task-priority.md §3), so nothing times it.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomBuildSystem : ISimulationSystem
{
    private const string SystemName = "RoomBuild";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is not BuildRoomCommand build) continue;
            Apply(context, build);
        }
    }

    private static void Apply(SimulationContext context, BuildRoomCommand command)
    {
        WorldState world = context.World;

        if (!context.Database.Rooms.TryGetValue(command.RoomId, out RoomDef? room))
        {
            context.Emit(SystemName, "build_refused",
                $"player={command.PlayerId} room={command.RoomId} reason=no_such_room");
            return;
        }

        int cost = RoomRules.CostPerTile(room);
        int built = 0;
        int spent = 0;
        TilePos? first = null;
        var refusals = new Dictionary<RoomPlacement, int>();
        // A bridge order of several tiles is marked whole first, as DK2 marks a dragged span.
        HashSet<TilePos>? spans = BridgeRules.IsBridge(room) && command.Tiles.Count > 1
            ? RoomRules.SpannableTiles(context.Database, world, command.Tiles, room, command.PlayerId)
            : null;

        foreach (TilePos pos in command.Tiles)
        {
            if (!world.Contains(pos))
            {
                Count(refusals, RoomPlacement.OffMap);
                continue;
            }

            ref TileState tile = ref world.Tile(pos);
            RoomPlacement verdict = spans is not null && spans.Contains(pos)
                ? RoomRules.CanBuild(context.Database, tile, room, command.PlayerId, touchesOwnLand: true)
                : RoomRules.CanBuild(context.Database, world, pos, room, command.PlayerId);
            if (verdict != RoomPlacement.Allowed)
            {
                Count(refusals, verdict);
                continue;
            }

            // Charge before placing. A tile that was paid for but not laid, or
            // laid but not paid for, would both be worse than refusing it.
            if (!GoldStore.TryWithdraw(world, command.PlayerId, cost))
            {
                Count(refusals, RoomPlacement.NotEnoughGold);
                continue;
            }

            byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
            RoomRules.Place(context.Database, ref tile, room, command.PlayerId);
            SlabChain.Changed(world, context.Database, (pos.Y * world.Width) + pos.X, terrainBefore, ownerBefore);
            if (built++ == 0) first = pos;
            spent += cost;
        }

        context.Emit(SystemName, "built",
            $"player={command.PlayerId} room={room.Name} tiles={built} cost={spent} " +
            $"refused={Describe(refusals)}" + (first is TilePos f ? $" first=({f.X},{f.Y})" : ""));

        // A Combat Pit that took a tile is rebuilt, and its reset throws its fighters out (0x004D7E00).
        if (built > 0)
            foreach (RoomInstance pit in CombatPitRules.PitsUnder(world, context.Database, command.Tiles))
                CombatPitSystem.Reset(context, pit);
    }

    private static void Count(Dictionary<RoomPlacement, int> refusals, RoomPlacement reason) =>
        refusals[reason] = refusals.TryGetValue(reason, out int n) ? n + 1 : 1;

    /// <summary>
    /// Refusals as "reason=count" pairs, ordered by the enum so that two runs of
    /// the same scenario produce byte-identical event text.
    /// </summary>
    private static string Describe(Dictionary<RoomPlacement, int> refusals) =>
        refusals.Count == 0
            ? "0"
            : string.Join(",", refusals
                .OrderBy(kv => (int)kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}"));
}
