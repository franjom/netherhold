// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Applies direct tile builds and queues player rectangles during input processing.
/// <see cref="RoomBuildWaveSystem"/> drains player slots later in the keeper frame.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public sealed class RoomBuildSystem : ISimulationSystem
{
    private const string SystemName = "RoomBuild";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (BuildRoomCommand build in context.Commands<BuildRoomCommand>())
            if (build.Mode == RoomBuildMode.PlayerWave) Queue(context, build);
            else Apply(context, build);
    }

    private static void Queue(SimulationContext context, BuildRoomCommand command)
    {
        WorldState world = context.World;
        if (!world.HasPlayer(command.PlayerId) || !context.Database.Rooms.TryGetValue(command.RoomId, out var room))
            return;
        HashSet<TilePos>? spans = BridgeRules.IsBridge(room)
            ? RoomRules.SpannableTiles(context.Database, world, command.Tiles, room, command.PlayerId) : null;
        int placeable = command.Tiles.Count(pos => world.Contains(pos)
            && Verdict(context, world, spans, pos, room, command.PlayerId) == RoomPlacement.Allowed);
        int cost = placeable * RoomRules.CostPerTile(room);
        bool prechecked = placeable != 0 && cost <= GoldStore.Total(world, command.PlayerId);
        if (prechecked) RequestAcceptedSound(context, command, room);
        for (int slotIndex = 0; slotIndex < RoomBuildWaveRules.SlotsPerPlayer; slotIndex++)
        {
            ref RoomBuildWaveState? slot = ref world.RoomBuildWave(command.PlayerId, (RoomBuildSlot)slotIndex);
            if (slot.HasValue) continue;
            if (!prechecked)
            {
                context.Emit(SystemName, "build_refused", $"player={command.PlayerId} room={room.Name} drag_cost={cost}");
                return;
            }
            slot = new RoomBuildWaveState(command.RoomId, command.Press, command.Release, context.Tick, 0);
            return;
        }
        context.Emit(SystemName, "build_refused", $"player={command.PlayerId} room={room.Name} reason=queue_full");
    }

    /// <summary>The build command's sound (<c>0x00442D57</c>): after the drag's pre-check passes and the order is
    /// sent, <c>0x2F8</c> in the room's terrain category (room definition <c>+0x44E</c>) at the rectangle's
    /// lowest corner, clamped to the map, height 0x1000. The function runs where the command is issued;
    /// ours has the one path, so it sounds here, before a full queue is found (STRONGLY INFERRED).</summary>
    private static void RequestAcceptedSound(SimulationContext context, BuildRoomCommand command, RoomDef room)
    {
        TilePos press = RoomBuildWaveRules.ClampPress(command.Press);
        TilePos release = RoomBuildWaveRules.ClampRelease(command.Release, context.World);
        int corner = (Math.Min(press.Y, release.Y) * context.World.Width) + Math.Min(press.X, release.X);
        ThingSoundRules.RequestTile(context, SystemName, room.TerrainId, corner, ThingSoundEvent.RoomBuildAccepted,
            height: 0x1000, SoundSourceKind.Room);
    }

    internal static void Drain(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        if (!playerId.IsKeeper || world.Player(playerId).KilledByPlayerId != 0) return;
        for (int slotIndex = 0; slotIndex < RoomBuildWaveRules.SlotsPerPlayer; slotIndex++)
        {
            ref RoomBuildWaveState? slot = ref world.RoomBuildWave(playerId, (RoomBuildSlot)slotIndex);
            if (slot is not RoomBuildWaveState wave) continue;
            RoomDef room = context.Database.Room(wave.RoomId);
            int cost = RoomRules.CostPerTile(room);
            bool bridge = BridgeRules.IsBridge(room);
            IReadOnlyList<TilePos> rectangle = bridge
                ? SlabChain.Wave(wave.Press.X, wave.Press.Y, wave.Release.X, wave.Release.Y) : Array.Empty<TilePos>();
            TilePos press = RoomBuildWaveRules.ClampPress(wave.Press);
            foreach (TilePos pos in RoomBuildWaveRules.Diagonal(wave, context.Tick, world))
            {
                if (!world.Contains(pos)) continue;
                // The bridge arm tests money before its span map; ordinary rooms test placement first.
                if (bridge && GoldStore.Total(world, playerId) < cost)
                {
                    slot = null;
                    return;
                }
                HashSet<TilePos>? spans = bridge
                    ? RoomRules.SpannableTiles(context.Database, world, rectangle, room, playerId) : null;
                RoomPlacement verdict = Verdict(context, world, spans, pos, room, playerId);
                if (verdict == RoomPlacement.Allowed)
                {
                    if (GoldStore.Total(world, playerId) < cost)
                    {
                        slot = null;
                        return;
                    }
                    bool built = PlacePaidTile(context, playerId, pos, room, announce: context.Tick != wave.QueuedTick);
                    if (built)
                    {
                        new AutoFurnitureSystem().Execute(context);
                        foreach (RoomInstance pit in CombatPitRules.PitsUnder(world, context.Database, [pos]))
                            CombatPitSystem.Reset(context, pit);
                    }
                    EmitConstructionEffect(context, playerId, pos);
                    wave = wave with { AccumulatedCost = unchecked((ushort)(wave.AccumulatedCost + cost)) };
                    slot = wave;
                    context.Emit(SystemName, "built", $"player={playerId} room={room.Name} tiles={(built ? 1 : 0)} cost={(built ? cost : 0)} first=({pos.X},{pos.Y})");
                }
                if (pos == press)
                {
                    slot = null;
                    // Completion exits the entire player drain; later slots do not catch up next tick.
                    return;
                }
            }
        }
    }

    /// <summary>One paid tile. <paramref name="announce"/> is the builder's own test for its sound (<c>0x004C5D1B</c>): the
    /// game turn is not the turn the caller passed, which is the turn a drag was queued on and 0 for the
    /// computer keeper's wrapper.</summary>
    private static bool PlacePaidTile(SimulationContext context, PlayerId playerId, TilePos pos, RoomDef room,
        bool announce)
    {
        if (!GoldStore.TryWithdraw(context.World, playerId, RoomRules.CostPerTile(room))) return false;
        context.World.Player(playerId).Statistics.MoneySpent += RoomRules.CostPerTile(room); // 0x004C5D0A
        ref TileState tile = ref context.World.Tile(pos);
        byte terrainBefore = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
        RoomRules.Place(context.Database, ref tile, room, playerId);
        SlabChain.Changed(context.World, context.Database, pos.Y * context.World.Width + pos.X, terrainBefore, ownerBefore);
        if (announce)
            ThingSoundRules.RequestTile(context, SystemName, room.TerrainId, pos.Y * context.World.Width + pos.X,
                ThingSoundEvent.RoomTileBuilt, kind: SoundSourceKind.Room);
        return true;
    }

    private static void EmitConstructionEffect(SimulationContext context, PlayerId playerId, TilePos pos) =>
        context.Events.Emit(new SimEvent(context.Tick, SystemName, "construction_effect",
            $"player={playerId} effect={RoomConstructionEffect.DefinitionId} tile=({pos.X},{pos.Y}) height=6144")
        {
            ConstructionEffect = new RoomConstructionEffect(playerId, pos),
        });

    private static void Apply(SimulationContext context, BuildRoomCommand command)
    {
        WorldState world = context.World;

        if (!context.Database.Rooms.TryGetValue(command.RoomId, out RoomDef? room))
        {
            context.Emit(SystemName, "build_refused",
                $"player={command.PlayerId} room={(byte)command.RoomId} reason=no_such_room");
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

        // The drag's pre-check (0x004C5A20, room-slab-chain.md §2): the tiles the placement
        // predicate accepts are counted first, and the whole drag is refused when their cost
        // exceeds the keeper's money. Level 3's recording shows it twice: a 12-tile Workshop
        // (7200) refused at 3290 and again at 7181 gold, then placed whole at 7667 (ADR-0315).
        int placeable = 0;
        foreach (TilePos pos in command.Tiles)
        {
            if (!world.Contains(pos)) continue;
            if (Verdict(context, world, spans, pos, room, command.PlayerId) == RoomPlacement.Allowed)
                placeable++;
        }

        if (placeable > 0 && cost * placeable > GoldStore.Total(world, command.PlayerId))
        {
            context.Emit(SystemName, "built",
                $"player={command.PlayerId} room={room.Name} tiles=0 cost=0 " +
                $"refused=NotEnoughGold={placeable} drag_cost={cost * placeable}");
            return;
        }

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
            if (!PlacePaidTile(context, command.PlayerId, pos, room, announce: true))   // the wrapper's turn 0 never equals DK2's game turn, our tick + 1
            {
                Count(refusals, RoomPlacement.NotEnoughGold);
                continue;
            }

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

    private static RoomPlacement Verdict(
        SimulationContext context, WorldState world, HashSet<TilePos>? spans, TilePos pos,
        RoomDef room, PlayerId playerId) =>
        spans is not null && spans.Contains(pos)
            ? RoomRules.CanBuild(context.Database, world.Tile(pos), room, playerId, touchesOwnLand: true)
            : RoomRules.CanBuild(context.Database, world, pos, room, playerId);

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
