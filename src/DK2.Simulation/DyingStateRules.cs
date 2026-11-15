// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
namespace DK2.Simulation;

public enum PitTransportPhase : byte { None, Sinking, Rising }

/// <summary>State 217's dying countdown and Combat Pit transport (combat-pit.md section 4).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit.md")]
public static class DyingStateRules
{
    private const string SystemName = "DyingState";
    private static readonly EffectId TransportEffect = new(0x149);

    internal static void Update(SimulationContext context, CreatureState creature)
    {
        if (!context.DyingStatesUpdatedThisTick.Add(creature)) return;
        if (creature.Dk2StateId == CreatureDeathRules.FallingState)
        {
            DeathFallRules.Fall(context, creature, context.Database.CreatureById(creature.CreatureTypeId));
            return;
        }
        if (creature.Dk2StateId != CreatureDeathRules.DyingState) return;
        creature.CombatPitFighter = false;
        if (Transport(context, creature) || creature.CarriedByCreatureId >= 0) return;
        creature.DyingTicks++;
        if (CreatureDeathRules.DyingWindowElapsed(context.Database, creature))
            CreatureHealthSystem.Die(context, "CreatureHealth", context.Database.CreatureById(creature.CreatureTypeId), creature);
    }

    private static bool Transport(SimulationContext context, CreatureState creature)
    {
        if (context.Tick.Value <= creature.PitTransportAfter.Value) return false;
        if (creature.PitTransportPhase == PitTransportPhase.Sinking)
        {
            creature.PosZ -= 0x200;
            if (creature.PosZ <= -FloorHeightRules.Ordinary)
            {
                creature.PosZ = -FloorHeightRules.Ordinary;
                creature.PitTransportPhase = PitTransportPhase.Rising;
                if (FloorPit(context, creature) is { } pit
                    && RoomWalkRules.RoomPoint(context.World, context.Database, pit, context.Rng) is int tile)
                {
                    TilePos rim = context.World.TilePosition(tile);
                    creature.PosX = creature.LastPosX = (rim.X << 12) + 0x400;
                    creature.PosY = creature.LastPosY = (rim.Y << 12) + 0x400;
                    creature.LastPosZ = creature.PosZ;
                    creature.Heading = 0x300;
                    RequestEffect(context, creature);
                }
            }
            return true;
        }
        if (creature.PitTransportPhase == PitTransportPhase.Rising)
        {
            creature.PosZ += 0x200;
            if (creature.PosZ >= 0)
            {
                creature.PosZ = 0;
                creature.PitTransportPhase = PitTransportPhase.None;
            }
            return true;
        }
        if (context.Tick.Value < creature.ActionUntilTick || FloorPit(context, creature) is null) return false;
        creature.PitTransportFloorHeight = new Fp32(FloorHeightRules.At(
            context.World, context.Database, creature.PosX, creature.PosY));
        creature.PitTransportPhase = PitTransportPhase.Sinking;
        RequestEffect(context, creature);
        return true;
    }

    private static RoomInstance? FloorPit(SimulationContext context, CreatureState creature)
    {
        if (!context.World.Contains(creature.Tile)) return null;
        int tile = context.World.TileIndex(creature.Tile);
        RoomInstance? pit = CombatPitRules.PitAt(context.World, context.Database, tile);
        return CombatPitRules.IsFloor(pit, tile) ? pit : null;
    }

    private static void RequestEffect(SimulationContext context, CreatureState creature) =>
        context.RequestEffect(SystemName, new ViewEffectRequest(TransportEffect, creature.OwnerPlayerId,
            new FpPosition3(new(creature.PosX), new(creature.PosY), new(creature.PosZ + FloorHeightRules.Ordinary)),
            new Dk2Angle((ushort)creature.Heading)));
}
