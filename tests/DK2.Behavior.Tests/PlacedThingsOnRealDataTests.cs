// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// <see cref="ThingProjection"/> against the shipped campaign. See
/// docs/research/placed-things.md.
///
/// <para>The fixture-based half of these assertions lives in
/// <c>DK2.Simulation.Tests.ThingProjectionTests</c>; these are what stop the
/// fixture and the reading from being wrong together.</para>
/// </summary>
public class PlacedThingsOnRealDataTests
{
    private static string Install() => Dk2InstallGate.Path;

    /// <summary>Every level the install ships, by name.</summary>
    private static IEnumerable<string> LevelNames(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        Assert.True(Directory.Exists(maps), $"'{maps}' is missing from the installation.");

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
            yield return Path.GetFileName(file)[..^"Things.kld".Length];
    }

    private static IEnumerable<(int X, int Y)> Tiles(ThingRecord record)
    {
        switch (record)
        {
            case ObjectThing t: yield return (t.X, t.Y); break;
            case TrapThing t: yield return (t.X, t.Y); break;
            case DoorThing t: yield return (t.X, t.Y); break;
            case ActionPointThing t: yield return (t.StartX, t.StartY); yield return (t.EndX, t.EndY); break;
            case CreatureThing t: yield return (t.X, t.Y); break;
            case RoomThing t: yield return (t.X, t.Y); break;
            case EffectGeneratorThing t: yield return (t.X, t.Y); break;
            case DeadBodyThing t: yield return (t.X, t.Y); break;
        }
    }

    /// <summary>
    /// <b>A THING's coordinates are tile indices.</b> The reading that had to be
    /// checked rather than assumed: the fields are 32-bit ints and the
    /// simulation carries positions in <see cref="Fp32"/> subtile units, 4096 to
    /// the tile, so "the file stores what the simulation stores" is the natural
    /// guess and it is wrong.
    ///
    /// <para>Every coordinate in the corpus lands inside its own level's grid.
    /// Under the subtile reading not one of them would — the largest map is
    /// 121x105 and the largest coordinate in any record is 118.</para>
    ///
    /// <para>Hero-party members are excluded on purpose: their X and Y are zero
    /// in every shipped record, because a party is placed by the trigger that
    /// spawns it at an action point rather than by coordinates in the file.
    /// (0,0) is in range on every map, so including them would not fail — it
    /// would just be a meaningless pass.</para>
    /// </summary>
    [InstallFact]
    public void EveryPlacedThingIsInsideItsOwnLevelsGrid_WhichIsWhatMakesThemTileIndices()
    {
        string install = Install();

        int checkedThings = 0, maxX = 0, maxY = 0;
        var offenders = new List<string>();

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            Assert.NotNull(things);

            foreach (ThingRecord record in things)
            {
                foreach ((int x, int y) in Tiles(record))
                {
                    checkedThings++;
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    if (!map.Contains(x, y))
                        offenders.Add($"{level} {record.GetType().Name} ({x},{y}) on {map.Width}x{map.Height}");
                }
            }
        }

        Assert.Empty(offenders);

        // Non-vacuous: an empty sweep would also produce no offenders.
        Assert.True(checkedThings > 6000, $"only {checkedThings} coordinates checked");
        Assert.True(maxX < 4096 && maxY < 4096,
            $"largest coordinate ({maxX},{maxY}) is under one subtile, so this proves nothing");
    }

    /// <summary>
    /// <b><c>InitialHealth</c> is a percentage, not a hit-point count.</b>
    /// Nothing in the corpus carries more than 100, on a creature or on a room,
    /// and the values below it are round percentages rather than the ragged
    /// numbers an absolute health field would hold.
    ///
    /// <para>The absolute reading is the dangerous one because it is plausible:
    /// it would spawn every creature in the game at 100 health, which for a
    /// Goblin looks like a slightly weak Goblin rather than like a bug.</para>
    /// </summary>
    [InstallFact]
    public void NoPlacedThingAsksForMoreThanAHundred_BecauseItIsAPercentage()
    {
        string install = Install();

        var creatureValues = new SortedSet<int>();
        var roomValues = new SortedSet<int>();
        int partySlots = 0, emptySlots = 0;

        foreach (string level in LevelNames(install))
        {
            foreach (ThingRecord record in GameDatabaseLoader.LoadCampaignThings(install, level) ?? [])
            {
                switch (record)
                {
                    case KeeperCreatureThing t: creatureValues.Add(t.InitialHealth); break;
                    case NeutralCreatureThing t: creatureValues.Add(t.InitialHealth); break;
                    case GoodCreatureThing t: creatureValues.Add(t.InitialHealth); break;
                    // A party is a FIXED ARRAY OF SIXTEEN member slots and most
                    // of them are empty, an empty slot being creature id 0.
                    // Counting the empty ones put a 0 in this set and made the
                    // percentage claim look false; they are not creatures.
                    case HeroPartyThing t:
                        foreach (GoodCreatureThing m in t.Members)
                        {
                            partySlots++;
                            if (m.CreatureId == 0) { emptySlots++; continue; }
                            creatureValues.Add(m.InitialHealth);
                        }
                        break;
                    case RoomThing t: roomValues.Add(t.InitialHealth); break;
                }
            }
        }

        Assert.Equal(new[] { 5, 10, 15, 20, 25, 30, 45, 50, 60, 70, 80, 100 }, creatureValues.ToArray());

        // Rooms are all at full health but two, and both of those are dungeon
        // hearts - a heart placed at 1% and one placed at 0%. Pinned because a
        // heart that starts destroyed is a scripted level opening, not a typo,
        // and a projection that ever does place rooms has to survive it.
        Assert.Equal(new[] { 0, 1, 100 }, roomValues.ToArray());

        // Sixteen slots per party, and the overwhelming majority stand empty.
        Assert.Equal(220 * 16, partySlots);
        Assert.True(emptySlots > partySlots / 2, $"{emptySlots} of {partySlots} slots empty");
    }

    /// <summary>
    /// <b>A ROOM record annotates the grid; it does not place anything.</b> All
    /// 275 of them carry the terrain id the MAP already holds at that tile, with
    /// no exceptions, so projecting one as a placement would build every dungeon
    /// heart and hero gate in the campaign twice.
    ///
    /// <para>The name <c>RoomType</c> comes from the external field list and is
    /// misleading: the four values that occur — 14, 33, 34 and 37 — are the
    /// <b>terrain</b> ids of the Dungeon Heart and the three Hero Gates. Read as
    /// room ids, 14 would be the Graveyard and 33, 34 and 37 would not exist at
    /// all, the table stopping at 26.</para>
    ///
    /// <para><b>Owner is the one thing they can disagree on</b>, and five do:
    /// two hero gates on Level13, one on Onslaught and two on Siege, each of
    /// which the record gives to the good player while the grid gives it to a
    /// keeper. Which one the original believes is Q223.</para>
    /// </summary>
    [InstallFact]
    public void ARoomRecordAlwaysAgreesWithTheTerrainUnderIt_SoItIsNotAPlacement()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        int rooms = 0, ownerDisagreements = 0;
        var terrainDisagreements = new List<string>();
        var roomTypes = new SortedSet<int>();

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            foreach (ThingRecord record in GameDatabaseLoader.LoadCampaignThings(install, level) ?? [])
            {
                if (record is not RoomThing room || !map.Contains(room.X, room.Y)) continue;

                rooms++;
                roomTypes.Add(room.RoomType);
                MapTile tile = map[room.X, room.Y];
                if (tile.TerrainId != room.RoomType)
                    terrainDisagreements.Add($"{level} ({room.X},{room.Y}) record {room.RoomType} grid {tile.TerrainId}");
                if (tile.PlayerId != room.PlayerId) ownerDisagreements++;
            }
        }

        Assert.Equal(275, rooms);
        Assert.Empty(terrainDisagreements);
        Assert.Equal(5, ownerDisagreements);

        // And the values are terrain ids, which is what makes the transcribed
        // name wrong. Every one of them resolves to a room through its TERRAIN,
        // and three of the four are not room ids at all.
        Assert.Equal(new[] { 14, 33, 34, 37 }, roomTypes.ToArray());
        foreach (int terrainId in roomTypes)
            Assert.NotNull(db.RoomByTerrain((byte)terrainId));
    }

    /// <summary>
    /// <b>Level 1, as shipped.</b> Four imps on the keeper's dungeon heart, one
    /// object, and two rooms the grid already holds.
    ///
    /// <para>This is the assertion that turns campaign level 1 from hand-placed
    /// test furniture into the level the game ships, so it pins the specifics
    /// rather than the counts alone.</para>
    /// </summary>
    [InstallFact]
    public void CampaignLevelOneProjectsItsFourImpsOntoTheHeart()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, "level1")!;

        WorldState world = WorldState.FromMap(
            db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);

        ThingProjectionResult result = ThingProjection.Place(world, db, things);

        Assert.Equal(4, result.Creatures);
        Assert.Equal(1, result.Objects);
        Assert.Equal(27, result.ActionPoints);
        Assert.Equal(2, result.RoomsAlreadyInTheGrid);

        Assert.Equal(2, result.HeroParties);
        Assert.Equal(0, result.EffectGenerators);
        Assert.Equal(0, result.NotYetProjected);
        Assert.Equal(27, world.ActionPoints.Count);
        Assert.Equal(2, world.HeroParties.Count);

        // Every one an Imp, every one the human keeper's, every one standing on
        // a dungeon-heart tile that keeper already owns.
        CreatureDef imp = db.CreatureByName("Imp")!;
        Assert.Equal(4, world.Creatures.Count);
        foreach (CreatureState creature in world.Creatures)
        {
            Assert.Equal(imp.CreatureId, creature.CreatureTypeId);
            Assert.Equal(PlayerIds.FirstKeeper, creature.OwnerPlayerId);
            Assert.Equal(CreatureLevelRules.FirstLevel, creature.Level);
            Assert.Equal(CreatureLevelRules.MaxHealth(db, imp, 1), creature.Health);

            MapTile under = map[creature.Tile.X, creature.Tile.Y];
            Assert.Equal(PlayerIds.FirstKeeper, under.PlayerId);
            Assert.NotNull(db.RoomByTerrain(under.TerrainId));
        }

        Assert.Equal(
            new[] { (12, 26), (12, 30), (16, 26), (16, 30) },
            world.Creatures.Select(c => (c.Tile.X, c.Tile.Y)).OrderBy(t => t).ToArray());
    }

    /// <summary>
    /// <b>Every shipped level projects.</b> The sweep that catches a creature or
    /// object id the database has never heard of: both spawns refuse an unknown
    /// id rather than placing a nameless entity, so an id this reading gets
    /// wrong throws here instead of becoming a silent hole in a level.
    /// </summary>
    [InstallFact]
    public void EveryShippedLevelProjectsWithoutRefusingAnId()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        int levels = 0, levelsWithDoorsOrTraps = 0, levelsWithHeroParties = 0;
        int creatures = 0, objects = 0, actionPoints = 0, doors = 0, traps = 0;
        int heroParties = 0, effectGenerators = 0, rooms = 0, notYet = 0;

        foreach (string level in LevelNames(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            Assert.NotNull(things);

            WorldState world = WorldState.FromMap(
                db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, .. Keepers()]);

            ThingProjectionResult result = ThingProjection.Place(world, db, things);

            levels++;
            creatures += result.Creatures;
            objects += result.Objects;
            actionPoints += result.ActionPoints;
            doors += result.Doors;
            traps += result.Traps;
            heroParties += result.HeroParties;
            effectGenerators += result.EffectGenerators;
            rooms += result.RoomsAlreadyInTheGrid;
            notYet += result.NotYetProjected;
            if (result.Doors != 0 || result.Traps != 0) levelsWithDoorsOrTraps++;
            if (result.HeroParties != 0) levelsWithHeroParties++;

            Assert.Equal(result.Creatures, world.Creatures.Count);
            Assert.Equal(result.Objects, world.Objects.Count);
            Assert.Equal(result.ActionPoints, world.ActionPoints.Count);
            Assert.Equal(result.Doors, world.Doors.Count);
            Assert.Equal(result.Traps, world.Traps.Count);
            Assert.Equal(result.HeroParties, world.HeroParties.Count);
            Assert.Equal(result.EffectGenerators, world.EffectGenerators.Count);

            ActionPointThing[] authored = things.OfType<ActionPointThing>().ToArray();
            for (int i = 0; i < authored.Length; i++)
            {
                ActionPointThing source = authored[i];
                ActionPointState projected = world.ActionPoints[i];
                Assert.Equal(source.ActionPointId, projected.Id);
                Assert.Equal(unchecked((ushort)source.StartX), projected.StartX);
                Assert.Equal(unchecked((ushort)source.StartY), projected.StartY);
                Assert.Equal(unchecked((ushort)source.EndX), projected.EndX);
                Assert.Equal(unchecked((ushort)source.EndY), projected.EndY);
                Assert.Equal(source.TriggerId, projected.TriggerId);
                Assert.Equal(source.NextWaypointId, projected.NextWaypointId);
                Assert.Equal((byte)(source.Flags & ActionPointState.LoadedFlagMask),
                             projected.Flags);
                Assert.Equal((ushort)0, projected.PassageCount);
                Assert.Equal(unchecked((ushort)(source.WaitDelay
                                 * SimulationTiming.OriginalTicksPerGameSecond)),
                             projected.WaitTimerTicks);
            }

            DoorThing[] authoredDoors = things.OfType<DoorThing>().ToArray();
            for (int i = 0; i < authoredDoors.Length; i++)
            {
                DoorThing source = authoredDoors[i];
                DoorState projected = world.Doors[i];
                Assert.Equal(source.X, projected.X);
                Assert.Equal(source.Y, projected.Y);
                Assert.Equal(source.Unknown_0008, projected.Unknown_0008);
                Assert.Equal((ushort)0, source.TriggerId);
                Assert.Equal(source.TriggerId, projected.TriggerId);
                Assert.Equal(source.DoorId, projected.DoorId);
                Assert.Equal(source.PlayerId, projected.PlayerId);
                Assert.Equal(source.Flags, projected.Flags);
                Assert.Equal(source.Unknown_0011.ToArray(), projected.Unknown_0011.ToArray());
            }

            TrapThing[] authoredTraps = things.OfType<TrapThing>().ToArray();
            for (int i = 0; i < authoredTraps.Length; i++)
            {
                TrapThing source = authoredTraps[i];
                TrapState projected = world.Traps[i];
                Assert.Equal(source.X, projected.X);
                Assert.Equal(source.Y, projected.Y);
                Assert.Equal(source.Unknown_0008, projected.Unknown_0008);
                Assert.Equal(source.NumberOfShots, projected.NumberOfShots);
                Assert.Equal(source.TrapId, projected.TrapId);
                Assert.Equal(source.PlayerId, projected.PlayerId);
                Assert.Equal(source.Unknown_000F, projected.Unknown_000F);
            }

            HeroPartyThing[] authoredParties = things.OfType<HeroPartyThing>().ToArray();
            for (int i = 0; i < authoredParties.Length; i++)
            {
                HeroPartyThing source = authoredParties[i];
                HeroPartyState projected = world.HeroParties[i];
                Assert.Equal(source.Name, projected.Name);
                Assert.Equal(source.TriggerId, projected.TriggerId);
                Assert.Equal(source.PartyId, projected.PartyId);
                Assert.Equal(source.Unknown_0023, projected.Unknown_0023);
                Assert.Equal(source.Unknown_0027, projected.Unknown_0027);
                Assert.Equal(source.Members.Count, projected.Members.Count);
                for (int m = 0; m < source.Members.Count; m++)
                {
                    GoodCreatureThing member = source.Members[m];
                    HeroPartyMemberState state = projected.Members[m];
                    Assert.Equal(member.X, state.X); Assert.Equal(member.Y, state.Y);
                    Assert.Equal(member.Z, state.Z); Assert.Equal(member.GoldHeld, state.GoldHeld);
                    Assert.Equal(member.Level, state.Level); Assert.Equal(member.Flags, state.Flags);
                    Assert.Equal(member.ObjectiveTargetActionPointId, state.ObjectiveTargetActionPointId);
                    Assert.Equal(member.InitialHealth, state.InitialHealth);
                    Assert.Equal(member.TriggerId, state.TriggerId);
                    Assert.Equal(member.ObjectiveTargetPlayerId, state.ObjectiveTargetPlayerId);
                    Assert.Equal(member.Objective, state.Objective);
                    Assert.Equal(member.CreatureId, state.CreatureId);
                    Assert.Equal(member.Unknown_001D.ToArray(), state.Unknown_001D.ToArray());
                    Assert.Equal(member.Flags2, state.Flags2);
                }
            }

            EffectGeneratorThing[] authoredGenerators = things.OfType<EffectGeneratorThing>().ToArray();
            for (int i = 0; i < authoredGenerators.Length; i++)
            {
                EffectGeneratorThing source = authoredGenerators[i];
                EffectGeneratorState projected = world.EffectGenerators[i];
                Assert.Equal(source.X, projected.X); Assert.Equal(source.Y, projected.Y);
                Assert.Equal(source.Unknown_0008, projected.Unknown_0008);
                Assert.Equal(source.Unknown_000C, projected.Unknown_000C);
                Assert.Equal(source.Unknown_0010, projected.Unknown_0010);
                Assert.Equal(source.Unknown_0012, projected.Unknown_0012);
                Assert.Equal(source.EffectIds, projected.EffectIds);
                Assert.Equal(source.Frequency, projected.Frequency);
                Assert.Equal(source.GeneratorId, projected.GeneratorId);
                Assert.Equal(source.Unknown_001E.ToArray(), projected.Unknown_001E.ToArray());
            }
        }

        Assert.Equal(66, levels);
        Assert.Equal(48, levelsWithDoorsOrTraps);
        Assert.Equal(35, levelsWithHeroParties);
        Assert.Equal(1944, creatures);
        Assert.Equal(788, objects);
        Assert.Equal(1152, actionPoints);
        Assert.Equal(940, doors);
        Assert.Equal(959, traps);
        Assert.Equal(220, heroParties);
        Assert.Equal(1, effectGenerators);
        Assert.Equal(275, rooms);

        Assert.Equal(0, notYet);

        static IEnumerable<byte> Keepers() =>
            Enumerable.Range(PlayerIds.FirstKeeper, 5).Select(i => (byte)i);
    }

    /// <summary>
    /// <b>Why <see cref="ThingProjection"/> carries no floor on placed health.</b>
    /// A creature that rounded to zero would die on the tick it arrived, so the
    /// obvious move is to clamp to 1 — but the clamp could never fire, and a
    /// branch that cannot run is a branch nobody maintains. This asserts the
    /// guarantee the branch would have defended: the smallest health in the
    /// creature table, at the smallest percentage any level asks for, is still
    /// well above zero.
    /// </summary>
    [InstallFact]
    public void TheSmallestPlaceableHealthIsStillAboveZero_WhichIsWhyThereIsNoClamp()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        int weakest = db.Creatures.Values.Min(c => c.Hp);

        Assert.Equal(240, weakest);

        // 5 is the smallest InitialHealth anywhere in the corpus, asserted in
        // NoPlacedThingAsksForMoreThanAHundred. The level curve only scales
        // health up, so level 1 is the worst case.
        Assert.True(weakest * 5 / ThingProjection.FullHealthPercent > 0);
        Assert.Equal(12, weakest * 5 / ThingProjection.FullHealthPercent);
    }

    /// <summary>
    /// <b>The loader keeps five flag bits and discards the rest</b>, and the
    /// shipped corpus carries at least one bit it discards — which is what
    /// makes the mask testable at all.
    ///
    /// <para><b>Why this exists.</b> Every other assertion about the mask
    /// compares a projected value against
    /// <see cref="ActionPointState.LoadedFlagMask"/> itself, so the constant is
    /// checked against itself and any value would pass. The mask is the one
    /// number in the action-point projection that is read from the binary
    /// rather than copied from the file, so it is the one that most needs a
    /// test that can fail. The loader at <c>0x00521244</c>–<c>0x0052128C</c>
    /// extracts bits 2, 3, 4, 0 and 5 in that order and calls one setter each;
    /// bits 1, 6 and 7 are never copied.</para>
    /// </summary>
    [InstallFact]
    public void TheLoaderFlagMaskKeepsFiveBitsAndDropsWhatShipsOutsideThem()
    {
        // Named bit by bit, so changing it means disagreeing with the
        // disassembly rather than editing one hex digit.
        const int kept = (1 << 0) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
        Assert.Equal(0x3D, kept);
        Assert.Equal(kept, ActionPointState.LoadedFlagMask);

        string install = Install();

        int sourceBits = 0, points = 0;
        foreach (string level in LevelNames(install))
        {
            foreach (ThingRecord record in
                     GameDatabaseLoader.LoadCampaignThings(install, level) ?? [])
            {
                if (record is not ActionPointThing point) continue;
                sourceBits |= point.Flags;
                points++;
            }
        }

        Assert.Equal(1152, points);

        // Discovered from the corpus and then pinned, so that a level file the
        // reader starts decoding differently shows up here. 0x7D is bits 0, 2,
        // 3, 4, 5 and 6: every bit the loader keeps, plus bit 6, which ships
        // and is discarded. Bits 1 and 7 are never authored at all, so the
        // loader ignoring bit 1 is untestable from data and rests on the
        // disassembly alone.
        Assert.Equal(0x7D, sourceBits);

        // The point of the test: something outside the mask is actually
        // present to be dropped. Without this the mask could be 0xFF and
        // nothing would notice.
        Assert.NotEqual(0, sourceBits & ~kept);
    }
}
