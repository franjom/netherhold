// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The creature arm of DK2's walk-over gold pick-up (<c>0x00489246</c>–<c>0x0048954B</c>
/// in the creature update <c>0x00488930</c>): every creature the worker arm does not take puts
/// each neutral gold pile on its own tile into its purse, every tick, up to its level-scaled
/// <c>MaxGoldHeld</c> (gold-on-the-floor.md §12, ADR-0300). Runs after movement and before the
/// state choosers, as <c>ImpTaskSystem.PickUpGoldInPassing</c> does for the worker arm.
/// <para>Not built: the chicken side arm (a neutral Chicken eaten, action 26). Deviates: every
/// take here precedes every worker-arm take, and the tile's piles are walked newest first, the
/// tile list's own link order being unread (ADR-0300).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public sealed class WalkOverGoldSystem : ISimulationSystem
{
    private readonly List<ObjectState> _piles = [];

    public string Name => "walk-over gold";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        _piles.Clear();
        for (int i = world.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = world.Objects[i];
            if (IsTakeable(thing)) _piles.Add(thing);
        }
        if (_piles.Count == 0) return;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = creatures[i];
            // A corpse is DK2's separate dead-body thing, which has no creature update; ours keeps
            // it in the list (ADR-0206), on the piles its own death dropped.
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsCorpse(creature)
                || !world.Contains(creature.Tile)) continue;
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (IsWorkerArm(world, definition, creature)) continue;
            TakeFromTile(context, creature, definition);
        }
    }

    /// <summary>The worker arm's three tests (<c>0x0048904E</c>): not possessed, a worker
    /// definition, owned by a keeper. Everyone else is this arm's.</summary>
    public static bool IsWorkerArm(WorldState world, CreatureDef definition, CreatureState creature) =>
        definition.Flags.HasFlag(CreatureFlags.IsWorker)
        && PlayerIds.IsKeeper(creature.OwnerPlayerId)
        && !CreatureDeathRules.IsPossessed(world, creature);

    /// <summary>A gold object (type 1, 2 or 3) owned by the neutral player and lying on a tile.
    /// Ours has only object 1 on the floor; a Treasury's or Heart's gold is tile state
    /// (ADR-0015), so a Heart's own piles are never candidates.</summary>
    private static bool IsTakeable(ObjectState thing) =>
        thing.ObjectTypeId is >= 1 and <= 3 && thing.OwnerPlayerId == PlayerIds.Neutral
        && !thing.InHand && thing.CarriedByCreatureId < 0 && thing.TileIndex >= 0 && thing.GoldHeld > 0;

    private void TakeFromTile(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (creature.CasinoBigWinnerTicks != 0 || creature.Dk2StateId == DropLandingRules.LandingState) return;

        int tileIndex = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomDef? room = database.RoomByTerrain(world.TileAtIndex(tileIndex).TerrainId);
        if (room is { RoomId: (byte)RoomTypeId.Casino or (byte)RoomTypeId.Treasury }) return;

        // A Skeleton's maximum is 0, so with an empty purse it lacks nothing and takes nothing.
        int max = CreatureLevelRules.MaxGoldHeld(database, definition, creature.Level);
        foreach (ObjectState pile in _piles)
        {
            if (pile.TileIndex != tileIndex || pile.GoldHeld <= 0) continue;

            // Compared unsigned (0x004893B1): a purse already over its maximum takes the whole
            // pile, and the clamp after the add (0x0048AB60) loses the excess.
            uint lacks = unchecked((uint)(max - creature.GoldHeld));
            int taken = FloorGold.Take(world, database, pile, (int)Math.Min(lacks, (uint)pile.GoldHeld));
            if (taken == 0) continue;
            creature.GoldHeld = (int)Math.Min(unchecked((uint)(creature.GoldHeld + taken)), (uint)max);
            context.Emit(Name, "gold_taken",
                $"creature={creature.Id} at=({tileIndex % world.Width},{tileIndex / world.Width}) " +
                $"gold={taken} holding={creature.GoldHeld}");
        }
    }
}
