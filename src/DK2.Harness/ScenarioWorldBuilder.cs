// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>Resolves a scenario's map and lays its setup into the world.</summary>
internal sealed class ScenarioWorldBuilder
{
    private const string CampaignPrefix = ScenarioRunner.CampaignPrefix;

    private readonly GameDatabase _database;
    private readonly string? _installDir;

    public ScenarioWorldBuilder(GameDatabase database, string? installDir)
    {
        _database = database;
        _installDir = installDir;
    }

    /// <summary>
    /// Lays down the scenario's rooms, and says what laying them down does not
    /// amount to.
    /// </summary>
    public void PlaceRooms(Scenario scenario, WorldState world, List<string> unsupported)
    {
        int placed = 0;
        int reowned = 0;

        foreach (ScenarioRoom r in scenario.Setup.Rooms)
        {
            RoomDef? room = _database.RoomByName(r.Type);
            if (room is null)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' is not a room in the database");
                continue;
            }

            if (r.At.Count != 2)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' needs at: [x, y]");
                continue;
            }

            int w = r.Size.Count == 2 ? r.Size[0] : 1;
            int h = r.Size.Count == 2 ? r.Size[1] : 1;
            if (w < 1 || h < 1)
            {
                unsupported.Add($"setup.rooms entry '{r.Type}' has size [{w},{h}]");
                continue;
            }

            int refused = 0;
            for (int y = r.At[1]; y < r.At[1] + h; y++)
            {
                for (int x = r.At[0]; x < r.At[0] + w; x++)
                {
                    if (!world.Contains(x, y))
                    {
                        refused++;
                        continue;
                    }

                    ref TileState tile = ref world.Tile(x, y);

                    // Already this room and only the owner differs: a scenario claiming a room the
                    // map shipped, not building one, and the only way to express a claimed portal.
                    // Room conversion is a real DK2 job with its own balance value
                    // (ConvertRoomHealth, -4000) and has no TileWorkKind yet (Q92), so a scenario
                    // cannot claim one the way an imp would. Reported as a shortfall below rather
                    // than treated as ordinary setup.
                    if (tile.TerrainId == room.TerrainId)
                    {
                        if (tile.OwnerPlayerId != r.Player)
                        {
                            tile.OwnerPlayerId = r.Player;
                            reowned++;
                        }
                        placed++;
                        continue;
                    }

                    if (!RoomRules.IsSuitableGround(_database, tile, room))
                    {
                        refused++;
                        continue;
                    }

                    RoomRules.Place(_database, ref tile, room, r.Player);
                    tile.KeptManaGain = 0; // a setup room is the map's: laid, not built over paid floor (ADR-0334)
                    placed++;
                }
            }

            if (refused > 0)
            {
                unsupported.Add(
                    $"setup.rooms entry '{r.Type}' at [{r.At[0]},{r.At[1]}] size [{w},{h}]: " +
                    $"{refused} of its {w * h} tiles are not ground this room can sit on and were skipped");
            }
        }

        if (reowned > 0)
        {
            unsupported.Add(
                $"setup.rooms handed {reowned} tile(s) of a room the map already had to another " +
                "player. That stands in for claiming it, which no imp can do here: converting a " +
                "rival's or a neutral room is a job in DK2 with its own balance value " +
                "(ConvertRoomHealth, -4000) and there is no work kind for it yet (Q92). So a run " +
                "with a claimed portal says nothing about how long claiming one takes, or whether " +
                "a portal claimed mid-game behaves like one held from the start");
        }

        if (placed == 0) return;

        unsupported.Add(
            "setup.rooms lays room terrain down directly: nothing is charged for it and no imp " +
            "builds it. Most rooms still do nothing a room does beyond being terrain its owner " +
            "holds - a treasury stores gold because a variable says how much per tile, and a " +
            "library and a training room are inert. Four now do more: a portal produces " +
            "creatures; a lair attracts them and holds one bed per tile that a creature walks " +
            "to, puts its bed on, sleeps in and is healed by, and that an imp can carry a " +
            "collapsed creature back to (ADR-0060, ADR-0063); a graveyard takes the corpses an " +
            "imp brings it; and a prison holds the enemies an imp drags in, withers them and " +
            "raises them as Skeletons (ADR-0064). What a lair still cannot do is anger the " +
            "creature it turns away (Q106)");
        unsupported.Add(
            "a prison in a run cannot be fed, and the reason has narrowed to one thing. DK2 " +
            "keeps a prisoner alive by having its keeper drop chickens on it, and that whole " +
            "loop now exists. A prisoner WANDERS its cell (ADR-0073), gets hungry, hunts the " +
            "room's own tiles for anything carrying the food flag, walks to the nearest by " +
            "straight-line distance and eats it - paying off the WHOLE of HungerFill from one " +
            "bird where a hatchery bird pays one (ADR-0074) - and a keeper can lift a chicken " +
            "and put it in the cell (ADR-0075). Nothing reserves the bird, so two hungry " +
            "prisoners race for it and the loser looks again. What is still NOT here: the " +
            "prison's anger is charged only for a failed search for food, never for the " +
            "company a prisoner keeps; and a prisoner nobody feeds still runs its whole " +
            "PrisonModifyCreatureHealthPerSecond countdown out and becomes a Skeleton. How " +
            "many prisoners a prison holds is the original's own rule - its inner tiles " +
            "plus one, so a 3x3 holds two (Q166, ADR-0070) - and what health a captured " +
            "creature wakes with is read from DK2's damage path (Q167, ADR-0071)");
        unsupported.Add(
            "a hatchery LAYS and free creatures EAT (ADR-0066, ADR-0067): coops go down, an EGG " +
            "(object 47) is laid at a randomly chosen coop on the interval the room size implies " +
            "and hatches two seconds later, one bird per tile; a hungry creature walks to the " +
            "nearest hatchery of its own keeper, picks the nearest chicken once it is inside, " +
            "eats it for min(HpFromChicken, missing health) and pays off one helping of hunger. " +
            "What is NOT here: how the original walks it there (Q174 is answered - state 0x5B " +
            "under the frame ADR-0072 corrected - but this still composes two states rather " +
            "than running them), and the post-meal wander around the room (Q175). A sleeping " +
            "creature does not wake to eat (Q176), and the answer is right for a better reason " +
            "than this project used to give: DK2 skips the needs routine for the fourteen " +
            "states that ARE a need, sleeping among them. A PRISONER eats by a different route " +
            "entirely and now does - fed where it stands, by a keeper dropping a bird into the " +
            "cell (ADR-0074, ADR-0075)");
    }

    /// <summary>
    /// Applies starting mana, and stores starting gold — which can only go where
    /// there is room for it.
    /// </summary>
    public void PlaceResources(Scenario scenario, WorldState world, List<string> unsupported,
        HashSet<byte> goldPaidByLevel)
    {
        foreach (ScenarioPlayer p in scenario.Setup.Players)
        {
            world.Player(p.Id).Mana = p.Mana;
            foreach (int spell in p.Spells)
            {
                if (world.Player(p.Id).Availabilities.Exists(a => a.Kind == AvailabilityKind.Spell && a.TypeId == spell))
                    world.SetAvailability(p.Id, AvailabilityKind.Spell, spell, 3);
                else
                    world.Player(p.Id).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, spell, 3));
            }

            if (p.Gold <= 0 || goldPaidByLevel.Contains(p.Id)) continue;

            int stored = GoldStore.Deposit(world, _database, p.Id, p.Gold);
            if (stored == p.Gold) continue;

            unsupported.Add(
                $"setup.players[{p.Id}].gold: {p.Gold - stored} of {p.Gold} had nowhere to go. A " +
                "keeper's gold lives in treasury and dungeon-heart tiles and nowhere else here; " +
                "DK2 would drop the rest on the floor as gold piles, which are objects and are not " +
                "implemented (Q31). Give the player a treasury in setup.rooms to hold it");
        }
    }

    /// <summary>
    /// Places the scenario's objects, and says what placing one by hand costs
    /// in honesty.
    /// </summary>
    private void SpawnObjects(Scenario scenario, WorldState world, List<string> unsupported)
    {
        foreach (ScenarioObject o in scenario.Setup.Objects)
        {
            ObjectDef? definition = _database.Objects.Values
                .FirstOrDefault(d => string.Equals(
                    d.Name, o.Type, StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                unsupported.Add($"setup.objects entry '{o.Type}' is not an object in the database");
                continue;
            }

            if (o.At.Count != 2 || !world.Contains(o.At[0], o.At[1]))
            {
                unsupported.Add(
                    $"setup.objects entry '{o.Type}' at [{string.Join(",", o.At)}] is not a tile on this map");
                continue;
            }

            ObjectState placed = world.SpawnObject(
                _database, definition.ObjectId, o.Player,
                (o.At[1] * world.Width) + o.At[0]);
            placed.GoldHeld = o.Gold;
        }

        if (scenario.Setup.Objects.Count == 0) return;

        unsupported.Add(
            "setup.objects lays an object on the floor directly, which nothing in the game does. " +
            "A hatchery lays chickens on its own schedule and an imp carries the rest home, so a " +
            "scenario that wanted a bird in a particular place at a particular tick had no way to " +
            "ask for one. What is NOT shortcut is anything after that: the keeper still has to " +
            "lift it and put it down, and both go through the same rules play does");
    }

    /// <summary>
    /// Places the scenario's creatures, and says what having them still does not
    /// buy the run.
    /// </summary>
    public void SpawnCreatures(Scenario scenario, WorldState world, List<string> unsupported)
    {
        // Setup runs before the simulation exists, so there is no trigger executor to give the hooks.
        var setupHooks = new StateHookContext(world, _database);
        int workers = 0;

        foreach (ScenarioCreature c in scenario.Setup.Creatures)
        {
            CreatureDef? definition = _database.CreatureByName(c.Type);
            if (definition is null)
            {
                unsupported.Add($"setup.creatures entry '{c.Type}' is not a creature in the database");
                continue;
            }

            if (c.At.Count != 2 || !world.Contains(c.At[0], c.At[1]))
            {
                unsupported.Add(
                    $"setup.creatures entry '{c.Type}' at [{string.Join(",", c.At)}] is not a tile on this map");
                continue;
            }

            CreatureState placed = world.SpawnCreature(
                _database, definition.CreatureId, c.Player, new TilePos(c.At[0], c.At[1]));

            // A starting level, as the shipped THINGS records give one, through the same
            // level-up the portal and the hero lair use.
            if (c.Level is int level)
                while (placed.Level < Math.Min(level, CreatureLevelRules.MaxLevel)
                    && CreatureLevelRules.LevelUp(setupHooks, definition, placed)) { }

            // Wounded on purpose, because nothing in a scenario can wound one.
            // Clamped at both ends: a typo cannot make a creature stronger than
            // its record, and cannot give it negative health either.
            if (c.Health is int health)
            {
                placed.Health =
                    Math.Clamp(health, 0, CreatureHealthRules.MaxHealth(_database, definition, placed));
            }

            // Hungry on purpose, and for the prison it is the only way: see
            // ScenarioCreature.Hunger. Clamped at zero so a typo cannot make a
            // creature owe negative food.
            if (c.Hunger is int hunger) placed.HungerNeed = Math.Max(0, hunger);

            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) workers++;
        }

        SpawnObjects(scenario, world, unsupported);

        if (scenario.Setup.Creatures.Count == 0) return;

        unsupported.Add(
            "setup.creatures places creatures directly, which is no longer the only way one can " +
            "exist: imps come from a dungeon heart and from the Create Imp spell at a mana cost " +
            "(ImpSupplySystem) and everything else comes out of a portal (PortalSystem). What is " +
            "still missing is a level's own starting roster - THINGS now parses, but M6 has not " +
            "projected those level records into WorldState - so a creature placed here stands in " +
            "for one the level file would have put there");

        if (workers == 0)
        {
            unsupported.Add(
                "none of the placed creatures is a worker, so nothing digs, claims, carries or " +
                "builds in this run. Non-workers are no longer inert - they take a bed and sleep " +
                "in it, grow hungry and walk to a hatchery to eat, get angry and walk out - but " +
                "they still have no combat and no job of their own");
            return;
        }

        unsupported.Add(
            "workers do marked excavation first, then claim floor, then reinforce walls - the " +
            "order is CONFIRMED (Q24) but what an imp does when all three run out is not: in DK2 " +
            "it wanders the dungeon and interacts with creatures and scenery, and here it stands " +
            "still (Q41). Dragging the unconscious to a prison and fleeing a fight are still absent");
        unsupported.Add(
            "routing is ours, not DK2's (Q5): a four-connected shortest-path search with uniform " +
            "step costs. Travel times in this run are a consequence of that choice");
        unsupported.Add(
            "a worker mining gold carries it to a treasury or dungeon-heart tile itself (Q31), but " +
            "gold it cannot hold is LOST: DK2 grows a lump on the floor that somebody can come back " +
            "for. Gold on the floor is three of the 145 objects and the world now has a list to put " +
            "one in, so what is left is the mechanic rather than the record. A keeper with " +
            "nowhere to store gold still mines, and " +
            "keeps only what fits in one pair of hands");
    }

    /// <summary>Places the level's own THINGS, when the scenario asks for them. Refused rather
    /// than ignored on a synthetic map: a scenario that says <c>placed_things: true</c> over
    /// <c>flat_24x20</c> is asking for something that does not exist, and silently giving it an
    /// empty level would make the scenario look like it had run.</summary>
    /// <returns>The players whose starting gold the level's heart record paid in, with the
    /// scenario's <c>setup.players[].gold</c> standing in for the record's amount. Their gold is
    /// not deposited a second time: adding it on top gave Harkan 20,000 where DK2 gives 10,000,
    /// enough to pass the room planner's cost test DK2's keeper fails.</returns>
    public HashSet<byte> PlaceShippedThings(Scenario scenario, WorldState world, List<string> unsupported)
    {
        var paid = new HashSet<byte>();
        if (!scenario.Setup.PlacedThings) return paid;

        if (!scenario.Map.StartsWith(CampaignPrefix, StringComparison.OrdinalIgnoreCase))
        {
            unsupported.Add(
                $"setup.placed_things needs a '{CampaignPrefix}' map; '{scenario.Map}' has no THINGS file");
            return paid;
        }

        string level = scenario.Map[CampaignPrefix.Length..].Trim();
        if (_installDir is null)
            throw new InvalidOperationException(
                $"Scenario needs the placed things of '{level}' but no DK2 installation was found. Set DK2_DIR.");

        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(_installDir, level)
            ?? throw new InvalidOperationException(
                $"No THINGS file for campaign level '{level}' under Data/editor/maps.");

        // The players too: a heart's record pays its keeper's starting gold in. A scenario's
        // positive gold replaces the record's amount; zero leaves the level's own.
        List<PlayerDef>? players = GameDatabaseLoader.LoadCampaignPlayers(_installDir, level);
        Dictionary<byte, int>? startingGold = players?.ToDictionary(p => p.PlayerId, p => p.StartingGold);
        if (startingGold is not null)
            foreach (ScenarioPlayer p in scenario.Setup.Players)
                if (p.Gold > 0 && startingGold.ContainsKey(p.Id))
                {
                    startingGold[p.Id] = p.Gold;
                    paid.Add(p.Id);
                }
        ThingProjectionResult result = ThingProjection.Place(world, _database, things, startingGold);
        // The record pays only through a placed, unruined heart (ADR-0360); otherwise the
        // scenario's deposit stands.
        List<RoomInstance> rooms = RoomIndex.Build(world, _database);
        paid.RemoveWhere(id => world.Player(id).HeartRuined || !rooms.Any(
            r => r.OwnerPlayerId == id && r.Definition.TypeId == RoomTypeId.DungeonHeart));

        // Reported, not swallowed. Doors, traps, hero parties and effect
        // generators have nowhere to go yet, so a scenario built on a level
        // with any of them says so in its own output.
        if (result.NotYetProjected > 0)
            unsupported.Add(
                $"{level}: {result.NotYetProjected} placed things this build cannot project yet "
                + "(doors, traps, hero parties, effect generators)");
        return paid;
    }

    public TriggerTree? ResolveShippedTriggers(Scenario scenario, string? campaignLevel)
    {
        if (!scenario.Setup.PlacedThings || campaignLevel is null || _installDir is null)
            return null;

        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(_installDir, campaignLevel)
            ?? throw new InvalidOperationException($"No LEVEL file for '{campaignLevel}'.");
        List<TriggerRecord> records = GameDatabaseLoader.LoadCampaignTriggers(
            _installDir, campaignLevel)
            ?? throw new InvalidOperationException($"No TRIGGERS file for '{campaignLevel}'.");
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(
            _installDir, campaignLevel)
            ?? throw new InvalidOperationException($"No PLAYERS file for '{campaignLevel}'.");
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(
            _installDir, campaignLevel)
            ?? throw new InvalidOperationException($"No THINGS file for '{campaignLevel}'.");

        return new TriggerTree(
            records, level.TriggerId, things.OfType<HeroPartyThing>(),
            players.Select(player => new TriggerPlayerRoot(player.PlayerId, player.TriggerId)));
    }

    /// <summary>Resolves a scenario's map field. <c>campaign:level1</c> loads a shipped map;
    /// <c>flat_24x20</c> builds a synthetic dungeon with an impenetrable border and dirt path
    /// inside, <c>rock_24x20</c> the same filled with diggable rock, and <c>chamber_24x20</c>
    /// solid rock with a small open chamber at the centre, which is what an experiment with
    /// workers needs since a worker has to stand somewhere. All three are testing conveniences
    /// and none is a DK2 layout.</summary>
    public LevelMap ResolveMap(string spec)
    {
        const string campaign = CampaignPrefix;
        const string flat = "flat_";
        const string rock = "rock_";
        const string chamber = "chamber_";

        if (spec.StartsWith(campaign, StringComparison.OrdinalIgnoreCase))
        {
            string level = spec[campaign.Length..].Trim();
            if (_installDir is null)
                throw new InvalidOperationException(
                    $"Scenario needs campaign map '{level}' but no DK2 installation was found. Set DK2_DIR.");

            return GameDatabaseLoader.LoadCampaignMap(_installDir, level)
                ?? throw new InvalidOperationException(
                    $"No campaign map named '{level}' under the installation's Data/editor/maps.");
        }

        foreach (string prefix in new[] { flat, rock, chamber })
        {
            if (!spec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = spec[prefix.Length..].Split('x', 'X');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || w < 3 || h < 3)
                throw new InvalidDataException(
                    $"Map spec '{spec}' is not of the form {prefix}WxH (min 3x3).");

            if (prefix == chamber && (w < 7 || h < 7))
                throw new InvalidDataException(
                    $"Map spec '{spec}' needs at least 7x7 to hold a chamber and a wall around it.");

            return SyntheticMap(w, h, solid: prefix != flat, chamber: prefix == chamber);
        }

        throw new InvalidDataException(
            $"Unrecognised map spec '{spec}'. Use 'campaign:<level>', 'flat_WxH', 'rock_WxH' or 'chamber_WxH'.");
    }

    private LevelMap SyntheticMap(int width, int height, bool solid, bool chamber = false)
    {
        byte border = FindTerrain(t => t.IsImpenetrable);
        byte floor = FindTerrain(t => t.Name.Equals("Dirt Path", StringComparison.OrdinalIgnoreCase));
        byte fill = solid
            ? FindTerrain(t => t.IsSolid && t.IsTaggable && !t.IsImpenetrable && t.GoldValue == 0)
            : floor;

        // A 3x3 opening at the centre. Small on purpose: it is a place to stand,
        // not a dungeon, and anything larger would start to look like a claim
        // about how DK2 lays a level out.
        int chamberX = width / 2;
        int chamberY = height / 2;

        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool open = chamber
                    && Math.Abs(x - chamberX) <= 1
                    && Math.Abs(y - chamberY) <= 1;

                byte terrain = edge ? border : open ? floor : fill;
                tiles[(y * width) + x] = new MapTile(terrain, 0, 0, 0);
            }
        return new LevelMap(width, height, tiles);
    }

    private byte FindTerrain(Func<TerrainDef, bool> predicate)
    {
        TerrainDef? match = _database.Terrain.Values.FirstOrDefault(predicate);
        return match?.TerrainId
            ?? throw new InvalidOperationException("The loaded database has no terrain matching that requirement.");
    }
}
