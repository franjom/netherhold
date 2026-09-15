// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum SacrificeRewardKind : byte
{
    None = 0,
    Creature = 1,
    Object = 2,
    Unsupported = 255,
}

public readonly record struct SacrificeResult(
    bool Matched, byte SpeechId, SacrificeRewardKind RewardKind, int SpawnedThingId);

/// <summary>Temple offering intake, ordered recipe matching, and shipped rewards.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sacrifice.md")]
public static class TempleSacrificeRules
{
    public const byte CreatureIngredient = 1;
    public const byte ObjectIngredient = 2;
    public const byte KeeperSpellIngredient = 3;
    public const byte FailedSpeechId = 1;
    public const byte GoldSpeechId = 7;

    public static bool IsTemple(RoomInstance? room, byte playerId) =>
        room is not null
        && room.Definition.TypeId == RoomTypeId.Temple
        && room.OwnerPlayerId == playerId;

    /// <summary>Removes histories whose lowest tile is no longer the lowest tile
    /// of a Temple owned by the same player. The identity policy is ADR-0149.</summary>
    public static void Reconcile(WorldState world, GameDatabase database)
    {
        var current = new HashSet<(int Anchor, byte Owner)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Temple) continue;
            current.Add((room.TileIndices[0], room.OwnerPlayerId));
        }

        world.RawTempleSacrifices.RemoveAll(
            state => !current.Contains((state.AnchorTileIndex, state.OwnerPlayerId)));
    }

    public static SacrificeResult OfferCreature(
        WorldState world, GameDatabase database, RoomInstance temple,
        CreatureState creature, int offeringTileIndex)
    {
        var ingredient = new SacrificeIngredient(
            CreatureIngredient, creature.CreatureTypeId, CreatureLevelRules.Clamp(creature.Level));
        SacrificeResult result = Offer(world, database, temple, ingredient, offeringTileIndex);
        world.RemoveCreature(creature.Id);
        return result;
    }

    public static SacrificeResult OfferObject(
        WorldState world, GameDatabase database, RoomInstance temple,
        ObjectState thing, int offeringTileIndex)
    {
        if (database.ObjectById(thing.ObjectTypeId)!.IsGold)
        {
            world.RemoveObject(thing.Id);
            return new SacrificeResult(false, GoldSpeechId, SacrificeRewardKind.None, -1);
        }

        SacrificeResult result = Offer(
            world, database, temple,
            new SacrificeIngredient(ObjectIngredient, thing.ObjectTypeId, 0),
            offeringTileIndex);
        world.RemoveObject(thing.Id);
        return result;
    }

    /// <summary>Recipe intake for the not-yet-wired action 0x87. The caller owns
    /// any keeper-spell cost; this method only records and resolves the offering.</summary>
    public static SacrificeResult OfferKeeperSpell(
        WorldState world, GameDatabase database, RoomInstance temple,
        byte keeperSpellId, int offeringTileIndex) =>
        Offer(world, database, temple,
            new SacrificeIngredient(KeeperSpellIngredient, keeperSpellId, 0),
            offeringTileIndex);

    private static SacrificeResult Offer(
        WorldState world, GameDatabase database, RoomInstance temple,
        SacrificeIngredient ingredient, int offeringTileIndex)
    {
        TempleSacrificeState history = world.TempleSacrificeAt(
            temple.TileIndices[0], temple.OwnerPlayerId);
        history.Append(ingredient);

        SacrificeEntry? match = null;
        int ingredientCount = 0;
        foreach (SacrificeEntry recipe in database.Variables.Sacrifices)
        {
            int count = IngredientCount(recipe);
            if (count == 0 || count > history.Count || !Matches(history, recipe, count)) continue;
            match = recipe;
            ingredientCount = count;
            break;
        }

        if (match is null)
            return new SacrificeResult(false, FailedSpeechId, SacrificeRewardKind.None, -1);

        SacrificeIngredient[] matched = new SacrificeIngredient[ingredientCount];
        for (int i = 0; i < ingredientCount; i++)
            matched[ingredientCount - 1 - i] = history.Newest(i);

        // DK2 empties the room's ring before dispatching its reward.
        history.Clear();

        byte rewardId = unchecked((byte)match.RewardValue);
        TilePos at = new(offeringTileIndex % world.Width, offeringTileIndex / world.Width);
        switch (match.RewardType)
        {
            case 1:
            {
                CreatureState reward = world.SpawnCreature(
                    database, rewardId, temple.OwnerPlayerId, at);
                int levelTotal = 0;
                int creatureCount = 0;
                foreach (SacrificeIngredient offered in matched)
                {
                    if (offered.Type != CreatureIngredient) continue;
                    levelTotal += offered.CreatureLevel;
                    creatureCount++;
                }

                reward.Level = creatureCount == 0
                    ? CreatureLevelRules.FirstLevel
                    : CreatureLevelRules.Clamp(levelTotal / creatureCount);
                reward.Health = CreatureLevelRules.MaxHealth(
                    database, database.CreatureById(rewardId), reward.Level);
                return new SacrificeResult(
                    true, match.SpeechId, SacrificeRewardKind.Creature, reward.Id);
            }
            case 2:
            {
                ObjectState reward = world.SpawnObject(
                    database, rewardId, PlayerIds.Neutral, offeringTileIndex);
                return new SacrificeResult(
                    true, match.SpeechId, SacrificeRewardKind.Object, reward.Id);
            }
            default:
                return new SacrificeResult(
                    true, match.SpeechId, SacrificeRewardKind.Unsupported, -1);
        }
    }

    private static bool Matches(TempleSacrificeState history, SacrificeEntry recipe, int count)
    {
        for (int newestOffset = 0; newestOffset < count; newestOffset++)
        {
            int recipeIndex = count - 1 - newestOffset;
            (byte type, byte id) = IngredientAt(recipe, recipeIndex);
            SacrificeIngredient offered = history.Newest(newestOffset);
            if (offered.Type != type || offered.Id != id) return false;
        }
        return true;
    }

    private static int IngredientCount(SacrificeEntry recipe)
    {
        if (recipe.Type1 == 0) return 0;
        if (recipe.Type2 == 0) return 1;
        return recipe.Type3 == 0 ? 2 : 3;
    }

    private static (byte Type, byte Id) IngredientAt(SacrificeEntry recipe, int index) => index switch
    {
        0 => (recipe.Type1, recipe.Id1),
        1 => (recipe.Type2, recipe.Id2),
        2 => (recipe.Type3, recipe.Id3),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}
