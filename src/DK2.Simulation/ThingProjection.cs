// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What one level's THINGS placed, and what it did not.
///
/// <para>Returned rather than logged so a caller can assert on it. A projection
/// that silently ignores two thirds of a file looks identical to one that
/// works, and the counts are the only thing that tells them apart.</para>
/// </summary>
public readonly record struct ThingProjectionResult(
    int Creatures,
    int Objects,
    int ActionPoints,
    int Doors,
    int Traps,
    int HeroParties,
    int EffectGenerators,
    int RoomsAlreadyInTheGrid,
    int NotYetProjected)
{
    public int Placed => Creatures + Objects + ActionPoints + Doors + Traps + HeroParties + EffectGenerators;
}

/// <summary>Turns a level's placed THINGS records into world contents.</summary>
/// <remarks>
/// <para>A THING's coordinates are bare tile indices, not the fixed-point subtile units the
/// simulation carries positions in (<see cref="Fp32"/>, 4096 to the tile). Across all 66 shipped
/// levels they run x 0..118 and y 0..102 against a largest map of 121x105, and not one of the 6279
/// records lands outside its own level's grid; under the subtile reading every one would.
/// <c>dk2tools kwd things</c> re-checks it.</para>
/// <para>Places every parsed THINGS kind that is not already represented by the
/// map grid. Hero parties and effect generators are stored without behaviour
/// (ADR-0108); party members remain unspawned templates, not creatures.</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/placed-things.md")]
public static class ThingProjection
{
    /// <summary>
    /// A creature or room record's <c>InitialHealth</c> is a <b>percentage of
    /// the entity's maximum</b>, not an absolute hit-point count.
    ///
    /// <para>Read off the shipped corpus: of 2891 placed creatures 2851 carry
    /// exactly 100, the rest carry 5, 10, 15, 20, 25, 30, 45, 50, 60, 70 or 80, and
    /// <b>nothing anywhere carries more than 100</b>. An absolute reading would
    /// make every creature in the game a 100 hp creature regardless of its
    /// record — which is close enough to plausible for a Goblin that it could
    /// have survived a long time unnoticed.</para>
    /// </summary>
    public const int FullHealthPercent = 100;

    /// <summary>
    /// Places every THING this projection understands into <paramref name="world"/>.
    ///
    /// <para>Order is the file's order, and that is deliberate: creature update
    /// order is behaviour (see <see cref="WorldState.Creatures"/>), so two imps
    /// that reach for the same job must resolve it the way the shipped level
    /// lists them, not the way a sort would.</para>
    /// </summary>
    public static ThingProjectionResult Place(
        WorldState world, GameDatabase database, IEnumerable<ThingRecord> things,
        IReadOnlyDictionary<byte, int>? startingGold = null)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (things is null) throw new ArgumentNullException(nameof(things));

        int creatures = 0, objects = 0, actionPoints = 0, doors = 0, traps = 0;
        int heroParties = 0, effectGenerators = 0;
        int rooms = 0, skipped = 0;
        var paid = new HashSet<byte>();

        foreach (ThingRecord record in things)
        {
            switch (record)
            {
                case KeeperCreatureThing t:
                    PlaceCreature(
                        world, database, t, t.CreatureId, t.PlayerId, t.InitialHealth, t.TriggerId,
                        RescueRules.OtherCreatureFlagMask);
                    creatures++;
                    break;

                // Neither the neutral nor the good creature record carries a
                // player field, because their side is fixed by which record
                // they are. PlayerIds documents both ids as CONFIRMED.
                case NeutralCreatureThing t:
                    PlaceCreature(
                        world, database, t, t.CreatureId, PlayerIds.Neutral,
                        t.InitialHealth, t.TriggerId, RescueRules.OtherCreatureFlagMask);
                    creatures++;
                    break;

                case GoodCreatureThing t:
                    PlaceCreature(
                        world, database, t, t.CreatureId, PlayerIds.Good,
                        t.InitialHealth, t.TriggerId, RescueRules.GoodCreatureFlagMask);
                    creatures++;
                    break;

                case ObjectThing t:
                    PlaceObject(world, database, t);
                    objects++;
                    break;

                case ActionPointThing t:
                    PlaceActionPoint(world, t);
                    actionPoints++;
                    break;

                case DoorThing t:
                    PlaceDoor(world, database, t);
                    doors++;
                    break;

                case TrapThing t:
                    PlaceTrap(world, database, t);
                    traps++;
                    break;

                case HeroPartyThing t:
                    PlaceHeroParty(world, t);
                    heroParties++;
                    break;

                case EffectGeneratorThing t:
                    PlaceEffectGenerator(world, t);
                    effectGenerators++;
                    break;

                // A ROOM record does not place a room: the MAP grid already
                // carries the room's terrain, and WorldState.FromMap has
                // already laid it down. Checked rather than assumed — across
                // the corpus all 275 of them agree with the terrain id under
                // them, with no exceptions. Projecting one as a placement would
                // build every dungeon heart and hero gate twice.
                case RoomThing t:
                    if (startingGold is not null) PayStartingGold(world, database, t, startingGold, paid);
                    rooms++;
                    break;

                default:
                    skipped++;
                    break;
            }
        }

        // Every placed blueprint without a crate goes on its keeper's manufacture list (ADR-0203).
        ManufactureRules.QueueBlueprints(world, database);

        return new ThingProjectionResult(
            creatures, objects, actionPoints, doors, traps,
            heroParties, effectGenerators, rooms, skipped);
    }

    /// <summary>
    /// A dungeon heart's record pays its keeper's starting gold into the heart, once.
    ///
    /// <para>STRONGLY INFERRED (Q75, <c>player-object.md</c>): the heart's builder
    /// <c>0x004DB700</c>, run by the THINGS loader for a room on terrain 14, moves the loader's
    /// <c>+0x7E</c> into the heart along its chain (<c>0x004F4090</c>), the rest into the keeper's
    /// Treasuries, and drops what is left. Level 2's 16000 fills its heart.</para>
    /// <para>Deviates: the Treasuries are taken in our room order, not the player's room list.</para>
    /// </summary>
    private static void PayStartingGold(
        WorldState world, GameDatabase database, RoomThing thing,
        IReadOnlyDictionary<byte, int> startingGold, HashSet<byte> paid)
    {
        if (database.RoomByTerrain(thing.RoomType)?.TypeId != RoomTypeId.DungeonHeart) return;
        if (!world.Contains(new TilePos(thing.X, thing.Y))) return;

        int tileIndex = (thing.Y * world.Width) + thing.X;
        RoomInstance? heart = RoomIndex.RoomAt(world, database, tileIndex);
        if (heart is null || !paid.Add(heart.OwnerPlayerId)) return;

        if (!startingGold.TryGetValue(heart.OwnerPlayerId, out int gold) || gold <= 0) return;

        int left = GoldStore.FillAlongChain(world, database, heart, gold);
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (left <= 0) break;
            if (room.OwnerPlayerId == heart.OwnerPlayerId && room.Definition.TypeId == RoomTypeId.Treasury)
                left = GoldStore.FillAlongChain(world, database, room, left);
        }
    }

    private static void PlaceCreature(
        WorldState world,
        GameDatabase database,
        CreatureThing thing,
        byte creatureTypeId,
        byte ownerPlayerId,
        int initialHealthPercent,
        ushort triggerId,
        byte flagMask)
    {
        CreatureState state = world.SpawnCreature(
            database, creatureTypeId, ownerPlayerId, new TilePos(thing.X, thing.Y));

        // Level before health, because maximum health is the record's Hp scaled
        // by the level curve. Setting health first and the level after would
        // leave a level-10 creature standing at a level-1 creature's maximum.
        state.Level = CreatureLevelRules.Clamp(thing.Level);
        state.GoldHeld = thing.GoldHeld;
        state.TriggerId = triggerId;

        // The record's Flags into the creature's, the bits the loader copies (ADR-0201).
        state.Flags = (byte)(thing.Flags & flagMask);

        // No floor on the result, deliberately: a creature that rounded to zero health would die on
        // the tick it arrived, but the clamp could never fire — the smallest Hp in the shipped table
        // is 240, the smallest placed percentage is 5, and the level curve only scales health up. A
        // branch that cannot run is a branch nobody maintains, so the guarantee is asserted in
        // PlacedThingsOnRealDataTests instead.
        CreatureDef def = database.CreatureById(creatureTypeId);
        int max = CreatureLevelRules.MaxHealth(database, def, state.Level);
        state.Health = max * initialHealthPercent / FullHealthPercent;
    }

    private static void PlaceObject(WorldState world, GameDatabase database, ObjectThing thing)
    {
        ObjectState state = world.SpawnObject(
            database, thing.ObjectId, thing.PlayerId, (thing.Y * world.Width) + thing.X);

        // MoneyAmount is what a placed gold pile is worth. It is zero on every
        // object that is not gold, so this needs no type test.
        state.GoldHeld = thing.MoneyAmount;
    }

    private static void PlaceActionPoint(WorldState world, ActionPointThing thing)
    {
        // DK2 copies coordinate words, copies only five bits out of Flags, and
        // scales WaitDelay once at load. Unchecked casts reproduce those
        // 16-bit stores rather than adding validation the original lacks.
        world.AddActionPoint(new ActionPointState
        {
            Id = thing.ActionPointId,
            StartX = unchecked((ushort)thing.StartX),
            StartY = unchecked((ushort)thing.StartY),
            EndX = unchecked((ushort)thing.EndX),
            EndY = unchecked((ushort)thing.EndY),
            TriggerId = thing.TriggerId,
            NextWaypointId = thing.NextWaypointId,
            Flags = (byte)(thing.Flags & ActionPointState.LoadedFlagMask),
            PassageCount = 0,
            WaitTimerTicks = unchecked((ushort)(
                thing.WaitDelay * SimulationTiming.OriginalTicksPerGameSecond)),
        });
    }

    private static void PlaceDoor(WorldState world, GameDatabase database, DoorThing thing) =>
        world.AddDoor(new DoorState
        {
            X = thing.X,
            Y = thing.Y,
            Unknown_0008 = thing.Unknown_0008,
            TriggerId = thing.TriggerId,
            DoorId = thing.DoorId,
            PlayerId = thing.PlayerId,
            Flags = thing.Flags,
            UnderConstruction = (thing.Flags & 0x02) != 0,
            LockMode = (byte)((thing.Flags & 0x01) != 0 ? 3 : 0),
            Orientation = BlueprintPlacementRules.Orientation(world, database, new TilePos(thing.X, thing.Y),
                database.Doors.GetValueOrDefault(thing.DoorId)),
            Health = database.Doors.GetValueOrDefault(thing.DoorId)?.Health ?? 0,
            Unknown_0011 = thing.Unknown_0011.ToArray(),
        });

    /// <summary>The authored loader <c>0x00520E50</c>: the trap made under construction
    /// (<c>0x004A7940</c>), then completed through <c>0x004A96D0</c> when the byte parsed as
    /// <c>NumberOfShots</c> is nonzero. Completion counts Triggers both ways, so the THINGS order
    /// does not change the counts.</summary>
    private static void PlaceTrap(WorldState world, GameDatabase database, TrapThing thing)
    {
        var trap = new TrapState
        {
            X = thing.X,
            Y = thing.Y,
            Unknown_0008 = thing.Unknown_0008,
            NumberOfShots = thing.NumberOfShots,
            TrapId = thing.TrapId,
            PlayerId = thing.PlayerId,
            Unknown_000F = thing.Unknown_000F,
            UnderConstruction = true,
        };
        TrapRules.Seed(world, database, trap, database.Traps.GetValueOrDefault(thing.TrapId));
        world.AddTrap(trap);
        if (thing.NumberOfShots != 0) TrapRules.Complete(world, database, trap);
    }

    private static void PlaceHeroParty(WorldState world, HeroPartyThing thing) =>
        world.AddHeroParty(new HeroPartyState
        {
            Name = thing.Name,
            TriggerId = thing.TriggerId,
            PartyId = thing.PartyId,
            Unknown_0023 = thing.Unknown_0023,
            Unknown_0027 = thing.Unknown_0027,
            Members = thing.Members.Select(member => new HeroPartyMemberState
            {
                X = member.X, Y = member.Y, Z = member.Z,
                GoldHeld = member.GoldHeld, Level = member.Level, Flags = member.Flags,
                ObjectiveTargetActionPointId = member.ObjectiveTargetActionPointId,
                InitialHealth = member.InitialHealth, TriggerId = member.TriggerId,
                ObjectiveTargetPlayerId = member.ObjectiveTargetPlayerId,
                Objective = member.Objective, CreatureId = member.CreatureId,
                WanderRadius = member.WanderRadius, PartyId = member.PartyId, Flags2 = member.Flags2,
            }).ToArray(),
        });

    private static void PlaceEffectGenerator(WorldState world, EffectGeneratorThing thing) =>
        world.AddEffectGenerator(new EffectGeneratorState
        {
            X = thing.X, Y = thing.Y,
            Unknown_0008 = thing.Unknown_0008, Unknown_000C = thing.Unknown_000C,
            Unknown_0010 = thing.Unknown_0010, Unknown_0012 = thing.Unknown_0012,
            EffectIds = thing.EffectIds.ToArray(), Frequency = thing.Frequency,
            GeneratorId = thing.GeneratorId, Unknown_001E = thing.Unknown_001E.ToArray(),
        });
}
