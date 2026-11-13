// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The creature arm of DK2's walk-over gold pick-up (<c>0x00489246</c>–<c>0x0048954B</c>
/// in the creature update <c>0x00488930</c>): every creature the worker arm does not take puts
/// each neutral gold pile on its own tile into its purse, every tick, up to its level-scaled
/// <c>MaxGoldHeld</c> (gold-on-the-floor.md §12, ADR-0300). Ours is a pass over the whole list after
/// every creature's update (<see cref="CreatureUpdateSystem"/>), not inside each one.
/// <para>The same walk destroys a loose neutral Chicken on the tile and starts the eating action 26
/// (<c>0x004892C5</c>..<c>0x0048932E</c>, ADR-0775): no hunger, health or purse change. Deviates:
/// every take here follows every worker-arm take, at the creature's tile after it moved where DK2
/// reads it at the update's entry; the tile's things are walked chickens first, then piles newest
/// first, the tile list's own order being unread (ADR-0300); the room is the tile's terrain's, the
/// same as DK2's room object while every room tile carries its room's terrain; and of action 26
/// only its begin sound is mirrored, not the art-13 cycle that replaces the current action.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/gold-on-the-floor.md")]
public sealed class WalkOverGoldSystem : ISimulationSystem
{
    private readonly List<ObjectState> _piles = [];
    private readonly List<ObjectState> _chickens = [];

    public string Name => "walk-over gold";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        _piles.Clear();
        _chickens.Clear();
        for (int i = world.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState thing = world.Objects[i];
            if (IsTakeable(thing)) _piles.Add(thing);
            else if (IsLooseNeutralChicken(thing)) _chickens.Add(thing);
        }
        if (_piles.Count == 0 && _chickens.Count == 0) return;

        EntityList<CreatureState> creatures = world.Creatures;
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
        ((definition.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker)
        && PlayerIds.IsKeeper(creature.OwnerPlayerId)
        && !CreatureDeathRules.IsPossessed(world, creature);

    /// <summary>A gold object (type 1, 2 or 3) owned by the neutral player and lying on a tile.
    /// Ours has only object 1 on the floor; a Treasury's or Heart's gold is tile state
    /// (ADR-0015), so a Heart's own piles are never candidates.</summary>
    private static bool IsTakeable(ObjectState thing) =>
        thing.ObjectTypeId is >= 1 and <= 3 && thing.OwnerPlayerId == PlayerIds.Neutral
        && !thing.InHand && thing.CarriedByCreatureId < 0 && thing.TileIndex >= 0 && thing.GoldHeld > 0;

    /// <summary>A Chicken (object 9) owned by the neutral player, on a tile and in nobody's hands.</summary>
    private static bool IsLooseNeutralChicken(ObjectState thing) =>
        EatingRules.IsEdibleChicken(thing) && thing.OwnerPlayerId == PlayerIds.Neutral && thing.TileIndex >= 0;

    private void TakeFromTile(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        // A Casino tile and +0x3F2 skip the whole walk (0x00489248..0x0048926A), chickens included.
        if (creature.CasinoBigWinnerTicks != 0) return;
        int tileIndex = (creature.Tile.Y * world.Width) + creature.Tile.X;
        RoomDef? room = database.RoomByTerrain(world.TileAtIndex(tileIndex).TerrainId);
        if (room is { RoomId: (byte)RoomTypeId.Casino }) return;
        if (creature.Dk2StateId == DropLandingRules.LandingState) return;

        // The chicken arm (0x004892A6..0x0048932E): not in 76 (tested above), not on a Portal or
        // Hatchery tile; whatever the creature's hunger the bird is destroyed and action 26 begins at
        // its own position, the begin's result ignored. Its sound, as at the other CreatureActionSounds
        // callers, assumes a begin not refused (0x00496743 refuses over some current actions; ours
        // has no action layer to refuse it).
        if (room is not { RoomId: (byte)RoomTypeId.Portal or (byte)RoomTypeId.Hatchery })
            foreach (ObjectState chicken in _chickens)
            {
                if (chicken.TileIndex != tileIndex || world.ObjectById(chicken.Id) is null) continue;
                // DK2 zeroes the bird's health (0x004892EF-0x00489318, vtable +0x20) and its own update, after the
                // creature's, removes it. Ours walks over after the objects have updated, so the bird has had
                // this turn's update already and goes now (Q796; a second walker this turn finds none, a deviation).
                world.RemoveObject(chicken.Id);
                CreatureActionSounds.Begin(context, Name, creature, CreatureActionTable.Eat);
                context.Emit(Name, "chicken_walked_over",
                    $"creature={creature.Id} chicken={chicken.Id} at=({tileIndex % world.Width},{tileIndex / world.Width}) " +
                    $"need={creature.HungerNeed}");
            }

        if (room is { RoomId: (byte)RoomTypeId.Treasury }) return;

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
