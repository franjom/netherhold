// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The world-facing half of mana pressure: the inputs <see cref="ManaPressureRules"/>
/// takes from the world and the writes its selection makes to it (imp-mana-removal.md,
/// ADR-0277). <see cref="Systems.ManaSystem"/> calls it on a payment.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-mana-removal.md")]
public static class ManaRemovalLifecycle
{
    /// <summary>The removal message's subtype (Data1) for a chosen creature, from <c>0x004C1BC0</c>.</summary>
    public const int RemovalSubtype = 3;

    /// <summary>The heart position the player initialiser <c>0x004B8640</c> writes before any
    /// heart exists: tile (1, 1) in both words.</summary>
    public const ushort InitialHeartTile = 1;

    /// <summary>DK2's turn for the tick being executed: the clocks compare against it and the
    /// removal stamp stores it, as the trigger walk's turn already does.</summary>
    public static uint Turn(GameTick tick) => unchecked((uint)(tick.Value + 1));

    /// <summary>Owned creatures whose definition carries both the worker and the evil bit, held
    /// ones included (<c>0x004C15C0</c>).</summary>
    public static uint EvilWorkers(WorldState world, GameDatabase database, byte playerId)
    {
        uint count = 0;
        foreach (CreatureState creature in world.OwnedCreatures(playerId))
            if (IsEvilWorker(database.CreatureById(creature.CreatureTypeId))) count++;
        return count;
    }

    public static bool IsEvilWorker(CreatureDef definition) =>
        definition.Flags.HasFlag(CreatureFlags.IsWorker) && definition.Flags.HasFlag(CreatureFlags.IsEvil);

    /// <summary>The owner's creatures in acquisition order, as the selector walks them.</summary>
    public static List<ManaRemovalCandidate> Candidates(WorldState world, GameDatabase database, byte playerId)
    {
        var candidates = new List<ManaRemovalCandidate>();
        foreach (CreatureState creature in world.OwnedCreatures(playerId))
        {
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            candidates.Add(new ManaRemovalCandidate(
                creature.Id, creature.CreatureTypeId, (byte)creature.Level, IsEvilWorker(definition),
                creature.InHand, creature.WorkerIdle, creature.PosX, creature.PosY));
        }
        return candidates;
    }

    /// <summary>The tile the selector measures from: DK2's <c>DungeonHeartPosition</c>
    /// (<c>player+0xB43</c>), which <see cref="Combat.HeartSites.PositionOf"/> derives from the
    /// heart's tiles, or the initialiser's (1, 1) for a keeper that has none.</summary>
    public static (ushort X, ushort Y) HeartTile(WorldState world, GameDatabase database, byte playerId)
    {
        int index = Combat.HeartSites.PositionOf(world, database, playerId);
        if (index < 0) return (InitialHeartTile, InitialHeartTile);
        return ((ushort)(index % world.Width), (ushort)(index / world.Width));
    }

    /// <summary>One removal scan for <paramref name="playerId"/> (<c>0x004C1BC0</c>): the chosen
    /// creature loses its corpse bit, takes the stamp — a second selection refreshes it — and
    /// message 53 subtype 3 is posted. Null when the floor holds or nobody qualifies.</summary>
    public static CreatureState? Select(SimulationContext context, byte playerId, uint now, uint minimumWorkers)
    {
        WorldState world = context.World;
        (ushort x, ushort y) = HeartTile(world, context.Database, playerId);
        int? id = ManaPressureRules.SelectForRemoval(
            Candidates(world, context.Database, playerId), minimumWorkers, x, y);
        if (id is null) return null;

        CreatureState chosen = world.CreatureById(id.Value)!;
        chosen.CorpseSuppressed = true;
        chosen.ManaRemovalStamp = now;
        world.Messages.Post(world, playerId, PlayerMessageKind.ManaShortfall, RemovalSubtype, 0);
        return chosen;
    }
}
