// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies this tick's <see cref="BuildRoomCommand"/>s: turns the player's own
/// floor into room tiles and takes the gold out of their treasury.
///
/// <para>Like <see cref="TileTagSystem"/>, this system carries no policy of its
/// own. Where a room may go and what it costs are <see cref="RoomRules"/>; where
/// the gold comes from is <see cref="GoldStore"/>. All that happens here is the
/// per-tile loop and the reporting.</para>
///
/// <para><b>A tile at a time, in the order the command gives.</b> A keeper who
/// can afford four tiles of a six-tile treasury gets four, and the run says
/// which two were refused and why. The alternative — all or nothing per command
/// — would be equally easy to write and neither is measured against the
/// original, but this one cannot silently lose a partly-paid room, and it makes
/// the refusal reasons visible one tile at a time.</para>
///
/// <para><b>Building is instant.</b> In DK2 an imp walks over and constructs the
/// tile, and the room appears as it works. That is another worker job, and its
/// trigger and priority are the same UNKNOWN as every other unprompted imp job
/// (Q24), so nothing here pretends to time it. What this system does honour is
/// the part that is not in doubt: the keeper asked, the ground allows it, and
/// the gold is gone.</para>
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
        var refusals = new Dictionary<RoomPlacement, int>();

        foreach (TilePos pos in command.Tiles)
        {
            if (!world.Contains(pos))
            {
                Count(refusals, RoomPlacement.OffMap);
                continue;
            }

            ref TileState tile = ref world.Tile(pos);
            RoomPlacement verdict = RoomRules.CanBuild(context.Database, tile, room, command.PlayerId);
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

            RoomRules.Place(context.Database, ref tile, room, command.PlayerId);
            built++;
            spent += cost;
        }

        context.Emit(SystemName, "built",
            $"player={command.PlayerId} room={room.Name} tiles={built} cost={spent} " +
            $"refused={Describe(refusals)}");
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
