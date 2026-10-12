// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>Keeper rat generation and object process 11, from the original binary.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison-rats.md")]
public static class RatRules
{
    public const byte RatObjectId = 11;
    public static readonly ObjectProcessId WanderingProcess = new(11);
    private static readonly TilePos[] Directions = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

    public static int PopulationLimit(IEnumerable<RoomInstance> rooms, PlayerId owner)
    {
        int tiles = 0;
        foreach (RoomInstance room in rooms)
            if (room.OwnerPlayerId == owner.Value && room.Definition.TypeId is RoomTypeId.Prison or RoomTypeId.TortureChamber)
                tiles += room.TileCount;
        return Math.Min(16, tiles / 9);
    }

    public static void Spawn(SimulationContext context, IReadOnlyList<RoomInstance> rooms, PlayerId owner)
    {
        int limit = PopulationLimit(rooms, owner);
        int population = 0;
        foreach (ObjectState thing in context.World.Objects)
            if (thing.ObjectTypeId == RatObjectId && thing.OwnerPlayerId == owner.Value) population++;
        if (population >= limit || context.Rng.NextInt(100) <= 94) return;
        RoomInstance[] prisons = rooms.Where(r => r.OwnerPlayerId == owner.Value && r.Definition.TypeId == RoomTypeId.Prison).Reverse().ToArray();
        if (prisons.Length == 0) return;
        RoomInstance prison = prisons[context.Rng.NextInt(prisons.Length)];
        int tile = prison.TileIndices[context.Rng.NextInt(prison.TileCount)];
        ObjectState rat = context.World.SpawnObject(context.Database, RatObjectId, owner, tile);
        rat.PosZ = Fp32.One;
        context.Emit("RatSpawn", "rat_spawned", $"player={owner.Value} rat={rat.Id} tile={tile}");
    }

    public static void Tick(SimulationContext context, ObjectState rat)
    {
        if (rat.InHand || rat.CarriedByCreatureId >= 0) return;
        if (rat.ProcessState == 0)
            ObjectMovementRules.SetProcess(rat, new ObjectProcessId(context.Database.ObjectById(RatObjectId)!.StartState));
        else
        {
            rat.ProcessAgeTicks = unchecked(rat.ProcessAgeTicks + 1);
            if (rat.ProcessState == ObjectMovementRules.MovingProcess.Value)
                ObjectMovementRules.StepMove(context, rat);
            else if (rat.ProcessState == WanderingProcess.Value && !ChoosePoint(context, rat))
                ObjectMovementRules.SetProcess(rat, default);
        }
        if (!context.World.Objects.Contains(rat)) return;
        if (rat.Health > 0) rat.Health--;
        if (rat.Health <= 0) context.World.RemoveObject(rat.Id);
    }

    private static bool ChoosePoint(SimulationContext context, ObjectState rat)
    {
        int start = context.Rng.NextInt(Directions.Length);
        var here = new TilePos(rat.PosX >> 12, rat.PosY >> 12);
        if (!context.World.Contains(here) || context.World.Tile(here).OwnerPlayerId != rat.OwnerPlayerId)
        {
            context.World.RemoveObject(rat.Id);
            return false;
        }
        for (int i = 0; i < Directions.Length; i++)
        {
            TilePos offset = Directions[(start + i) & 3];
            var target = new TilePos(here.X + offset.X, here.Y + offset.Y);
            if (!context.World.Contains(target) || context.World.Tile(target).OwnerPlayerId != rat.OwnerPlayerId
                || NavBytes.IsSolid(context.Navigation.NavByte(target.X, target.Y))) continue;
            RoomInstance? room = RoomIndex.RoomAt(context.World, context.Database, target.Y * context.World.Width + target.X);
            if (room?.Definition.Construction == TileConstruction.DoubleQuad
                && room.IsInterior(target.Y * context.World.Width + target.X)) continue;
            var destination = new ObjectMovePoint(new Fp32((target.X << 12) + context.Rng.NextInt(Fp32.One)),
                new Fp32((target.Y << 12) + context.Rng.NextInt(Fp32.One)));
            if (ObjectMovementRules.StartMove(context, rat, destination, WanderingProcess, default, interruptible: true))
                return true;
        }
        return false;
    }
}
