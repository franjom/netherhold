// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>The Casino room: its lever, and the gold it settles at the lever every four game
/// seconds (ADR-0188).</summary>
/// <remarks>The settlement is the room's own update, the body around <c>0x004D6783</c>. For each
/// creature in the room it calls <c>0x004D7180</c> with four seconds of that creature's purse
/// change, negated. Stingy, that is +32: added to a gold pile on the lever's tile, or a new pile
/// made there. Generous, it is −4: taken from that pile first and from the keeper's treasury for
/// the rest, as far as the keeper has it. So a generous Casino costs its keeper a coin a second
/// a gambler, and a stingy one leaves its takings beneath the lever for an imp to fetch.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/state-category-hooks.md")]
public sealed class CasinoSystem : ISimulationSystem
{
    private const string SystemName = "Casino";

    private readonly List<ObjectState> _piles = [];

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        PullLevers(context);

        long period = (long)CasinoRules.SettlementSeconds * SimulationTiming.TicksPerSecond;
        if ((context.Tick.Value + 1) % period != 0) return;

        WorldState world = context.World;
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if (room.Definition.TypeId != RoomTypeId.Casino) continue;
            if (CasinoRules.Lever(world, room) is not { } lever) continue;
            Settle(context, room, lever);
        }
    }

    /// <summary>The owner's lever command (<c>0x00513E50</c>): a lever of this player's, toggled.
    /// A tile with no lever of theirs does nothing.</summary>
    private static void PullLevers(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is not PullCasinoLeverCommand request) continue;
            WorldState world = context.World;
            if (!world.Contains(request.Tile)) continue;
            int tile = (request.Tile.Y * world.Width) + request.Tile.X;

            foreach (ObjectState thing in world.Objects)
            {
                if (thing.ObjectTypeId != AutoFurnitureRules.CasinoLeverObjectId) continue;
                if (thing.TileIndex != tile || thing.OwnerPlayerId != request.PlayerId) continue;

                thing.Setting = thing.Setting == CasinoRules.Generous
                    ? CasinoRules.Stingy
                    : CasinoRules.Generous;
                context.Emit(SystemName, "lever_pulled",
                    $"player={request.PlayerId} at=({request.Tile.X},{request.Tile.Y}) " +
                    $"setting={(thing.Setting == CasinoRules.Stingy ? "stingy" : "generous")}");
            }
        }
    }

    private void Settle(SimulationContext context, RoomInstance room, ObjectState lever)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int each = CasinoRules.SettlementPerGambler(database, lever.Setting);
        int gamblers = 0, dropped = 0, paid = 0;

        foreach (CreatureState creature in world.Creatures)
        {
            if (CreatureStateMachine.UpdatingCategory(creature) != CreatureStateCategory.Casino) continue;
            if (!world.Contains(creature.Tile)) continue;
            if (!EatingRules.Contains(room, (creature.Tile.Y * world.Width) + creature.Tile.X)) continue;

            gamblers++;
            if (each > 0) dropped += Drop(world, database, lever, each);
            else paid += PayOut(world, database, room.OwnerPlayerId, lever, -each);
        }

        if (gamblers == 0) return;
        context.Emit(SystemName, "settled",
            $"player={room.OwnerPlayerId} lever={lever.TileIndex} gamblers={gamblers} " +
            $"dropped={dropped} paid={paid} under_lever={FloorGold.OnTile(world, database, lever.TileIndex)}");
    }

    /// <summary>Stingy: onto the first pile on the lever's tile, or a new one there.</summary>
    private int Drop(WorldState world, GameDatabase database, ObjectState lever, int amount)
    {
        _piles.Clear();
        FloorGold.PilesOn(world, database, lever.TileIndex, _piles);
        if (_piles.Count > 0) _piles[0].GoldHeld += amount;
        else FloorGold.Drop(world, database, lever.TileIndex, amount);
        return amount;
    }

    /// <summary>Generous: off the pile beneath the lever first, then from the treasury, capped at
    /// what the keeper has (<c>0x004BA420</c> with <c>min(rest, player+0x7E)</c>).</summary>
    private int PayOut(
        WorldState world, GameDatabase database, byte keeper, ObjectState lever, int amount)
    {
        _piles.Clear();
        FloorGold.PilesOn(world, database, lever.TileIndex, _piles);
        int rest = amount;
        if (_piles.Count > 0) rest -= FloorGold.Take(world, database, _piles[0], rest);

        int fromTreasury = Math.Min(rest, GoldStore.Total(world, keeper));
        GoldStore.TryWithdraw(world, keeper, fromTreasury);
        return amount - rest + fromTreasury;
    }
}
