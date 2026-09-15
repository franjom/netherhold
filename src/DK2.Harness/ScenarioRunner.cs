// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Harness;

/// <summary>What a scenario run produced, and what it could not do.</summary>
public sealed record ScenarioResult(
    Scenario Scenario,
    int TicksRun,
    ulong FinalStateHash,
    IReadOnlyList<ulong> HashPerTick,
    IReadOnlyList<SimEvent> Events,
    IReadOnlyList<string> Unsupported)
{
    /// <summary>Every expectation the run did not meet, one line each. Empty is the
    /// only passing value; a scenario with no expectations produces an empty list too,
    /// which is why the count of scenarios carrying any is tracked separately.</summary>
    public IReadOnlyList<string> ExpectationFailures { get; init; } = [];

    /// <summary>
    /// The world at the end of the run, drawn as text through the same
    /// presentation boundary a Unity view uses.
    ///
    /// <para>Worth having in a scenario report for a reason the hash series
    /// cannot cover: a hash tells you two runs differed, and a picture tells you
    /// <i>where</i>. When the imp-corridor scenario's hash moved after the work
    /// stance changed (ADR-0028), the question was always "moved how" — and
    /// answering it meant reading the event log line by line.</para>
    ///
    /// <para>Empty when the run rendered nothing, which is not an error.</para>
    /// </summary>
    public string Picture { get; init; } = "";

    /// <summary>
    /// Where the run's art came from — normally "all placeholders", and saying
    /// so is the point (plan §7.2).
    /// </summary>
    public string AssetSummary { get; init; } = "";

    /// <summary>
    /// Which balance table produced these numbers.
    ///
    /// <para>Not in <see cref="Unsupported"/>, because it is not a shortfall —
    /// it is provenance. A campaign level ships its own VARIABLES and 26 ids are
    /// overridden by at least one shipped level, so "which table" is a real
    /// question about any run on a campaign map, and a reader of the report
    /// should not have to guess.</para>
    /// </summary>
    public string BalanceTable { get; init; } = "GlobalVariables.kwd";
}

/// <summary>
/// Runs a scenario headlessly against the simulation and writes an evidence
/// trail: a per-tick state-hash series, the event log, and a report naming
/// every part of the scenario this build could not honour.
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>How a scenario names one of the original campaign maps.</summary>
    private const string CampaignPrefix = "campaign:";

    private readonly GameDatabase _database;
    private readonly string? _installDir;

    public ScenarioRunner(GameDatabase database, string? installDir)
    {
        _database = database;
        _installDir = installDir;
    }

    /// <summary>
    /// Runs a scenario, on the balance table the level it names actually uses.
    ///
    /// <para>A campaign level ships its own VARIABLES, and 26 ids are overridden
    /// by at least one shipped level — so a run on <c>campaign:level1</c> must
    /// use level 1's numbers, not the global defaults. Rather than thread a
    /// second database through every helper, the level's database is resolved
    /// first and a runner built around it does the work; every rule below then
    /// reads the one <c>_database</c> that is correct for this run.</para>
    /// </summary>
    public ScenarioResult Run(Scenario scenario)
    {
        GlobalVariables? levelVariables = ResolveLevelVariables(scenario.Map);
        return levelVariables is null
            ? RunAgainstThisDatabase(scenario, null)
            : new ScenarioRunner(_database.ForLevel(levelVariables), _installDir)
                .RunAgainstThisDatabase(scenario, LevelNameOf(scenario.Map));
    }

    /// <summary>The level's own VARIABLES, for a scenario that names a campaign map.</summary>
    private GlobalVariables? ResolveLevelVariables(string mapSpec)
    {
        string? level = LevelNameOf(mapSpec);
        return level is null || _installDir is null
            ? null
            : GameDatabaseLoader.LoadCampaignVariables(_installDir, level);
    }

    private static string? LevelNameOf(string mapSpec) =>
        mapSpec.StartsWith(CampaignPrefix, StringComparison.OrdinalIgnoreCase)
            ? mapSpec[CampaignPrefix.Length..].Trim()
            : null;

    private ScenarioResult RunAgainstThisDatabase(Scenario scenario, string? campaignLevel)
    {
        var unsupported = new List<string>();

        // Which balance table this run used, recorded as provenance rather than
        // as a shortfall. A run that silently used the global defaults on a
        // level that overrides them would be evidence about a game nobody plays.
        string balanceTable = campaignLevel is null
            ? "GlobalVariables.kwd"
            : $"GlobalVariables.kwd + {campaignLevel}Variables.kld " +
              $"({_database.Variables.Misc.Count} values, " +
              $"{Count(_database.Variables.Availabilities.Count, "availability entry", "availability entries")}, " +
              $"{Count(_database.Variables.CreaturePool.Count, "creature-pool entry", "creature-pool entries")})";

        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        foreach (string key in scenario.UnknownKeys)
            unsupported.Add($"unknown key '{key}' in the scenario file");

        foreach (string observation in scenario.Observe)
            if (observation is not ("state_hash" or "events"))
                unsupported.Add($"observe.{observation} - nothing produces this yet");

        if (scenario.Work.Count > 0)
        {
            unsupported.Add(
                $"work ({scenario.Work.Count} entries) is injected by the harness, bypassing the " +
                "worker that would produce it. Nothing has decided that a creature wanted this job, " +
                "could reach the tile or was standing beside it, so this run says nothing about " +
                "who would do the work or how long it would take. Use setup.creatures and a tag " +
                "command instead to watch a worker do it");
            unsupported.Add(
                "how many swings a tile takes and how long they take are both known now (Q47, " +
                "Q1): an application is a fifth of the tabled delta - the worker's Efficiency of " +
                "80% over the four ticks of a game second, a stat DK2 prints on its status bar " +
                "and one a slap or a well-aimed drop raises to 90% - and one lands every tick, " +
                "four a second, a second being a real second. So a tick count here IS a wall-clock " +
                "timing at 250 ms a tick");
        }

        // A run that used a number the game's own files do not contain has to
        // say which, every time. There is exactly one (ADR-0023), and this
        // enumerates rather than naming it so a second would report itself.
        List<string> corrected = Enum.GetValues<TileWorkKind>()
            .Select(TileWorkRules.Variable)
            .Where(v => ObservedCorrections.IsCorrected(_database, v))
            .Select(v => $"{v} (shipped {_database[v]}, " +
                         $"used {ObservedCorrections.TileWorkPerSecond(_database, v)})")
            .ToList();

        if (corrected.Count > 0)
            unsupported.Add(
                $"this run used {corrected.Count} corrected balance value(s) rather than what the " +
                $"game ships: {string.Join(", ", corrected)}. Each is a case where the shipped " +
                "number demonstrably cannot produce behaviour measured in the original, the " +
                "balance table was searched for an alternative and came back empty, and the " +
                "correction is guarded on the exact shipped value. See ObservedCorrections and " +
                "ADR-0023");

        LevelMap map = ResolveMap(scenario.Map);

        IEnumerable<byte> playerIds = scenario.Setup.Players.Count > 0
            ? scenario.Setup.Players.Select(p => p.Id)
            : map.Tiles.Select(t => t.PlayerId).Distinct();

        WorldState world = WorldState.FromMap(
            _database, map, playerIds, fogOfWarEnabled: scenario.FogOfWar);

        // The level's own contents before the scenario's, so a scenario that
        // asks for both is adding to the shipped level rather than racing it.
        PlaceShippedThings(scenario, world, unsupported);

        // Rooms first, gold second: gold has to be stored somewhere, and a
        // treasury is the only somewhere there is.
        PlaceRooms(scenario, world, unsupported);
        PlaceResources(scenario, world, unsupported);
        SpawnCreatures(scenario, world, unsupported);

        // Registration order is part of the simulation's definition, and there
        // is exactly one of it — see SimulationSystems. A harness that ran the
        // systems in a different order from the test suite would be producing
        // evidence about neither.
        var simulation = new GameSimulation(
            _database, world, scenario.Seed, SimulationSystems.Default());

        var sink = new ListEventSink();
        simulation.ObserveWith(sink);

        Dictionary<int, InputFrame> input = BuildInput(scenario, unsupported);
        List<ScenarioWork> work = ResolveWork(scenario, world, unsupported);

        var hashes = new List<ulong>(scenario.DurationTicks + 1) { simulation.StateHash() };
        for (int t = 0; t < scenario.DurationTicks; t++)
        {
            InjectWork(world, work, t);
            simulation.Tick(input.TryGetValue(t, out InputFrame frame) ? frame : InputFrame.Empty);
            hashes.Add(simulation.StateHash());
        }

        if (simulation.Systems.Count == 0)
            unsupported.Add("no simulation systems are registered - the world cannot change");

        AddManaCaveats(world, unsupported);

        // Draw the finished world through the presentation boundary. Runs with
        // no assets installed, which is every run: see the summary line.
        AssetProviderChain assets = AssetProviderChain.Standard(
            replacementRoot: null, convertedRoot: ConvertedAssetRoot);
        var renderer = new TextRenderer(new WorldView(_database, scenario.ViewingPlayer), assets);
        string picture = renderer.Render(world);

        return new ScenarioResult(
            scenario, scenario.DurationTicks, simulation.StateHash(), hashes, sink.Events, unsupported)
        {
            ExpectationFailures = CheckExpectations(scenario, sink.Events),
            Picture = picture,
            AssetSummary = assets.Summarise(),
            BalanceTable = balanceTable,
        };
    }

    /// <summary>
    /// Where the converter puts its output (plan §7.1). Named here rather than
    /// inline because it is the one path the harness knows, and it is a
    /// directory that normally does not exist — which is exactly the "no assets"
    /// case the placeholder covers.
    /// </summary>
    /// <summary>Tests every <see cref="ScenarioExpectation"/> against the events the run
    /// produced, and returns one line per unmet claim. Reports the count it did see, because
    /// "expected 531, saw 530" is a different problem from "expected 531, saw none".</summary>
    private static IReadOnlyList<string> CheckExpectations(
        Scenario scenario, IReadOnlyList<SimEvent> events)
    {
        var failures = new List<string>();
        foreach (ScenarioExpectation expectation in scenario.Expect)
        {
            int seen = 0;
            foreach (SimEvent observed in events)
            {
                if (observed.Kind != expectation.Event) continue;
                if (expectation.Detail.Length > 0
                    && !observed.Detail.Contains(expectation.Detail, StringComparison.Ordinal))
                    continue;
                if (expectation.AtTick is int tick && observed.Tick.Value != tick) continue;
                seen++;
            }

            bool met = expectation.Absent
                ? seen == 0
                : expectation.Count is int want ? seen == want : seen > 0;

            if (!met) failures.Add($"expected {expectation}, saw {seen}");
        }

        return failures;
    }

    public const string ConvertedAssetRoot = "build/converted";

    /// <summary>
    /// Mana accrues in every run now, so every run has to say what that number
    /// is not. Only said when somebody actually earned some — a run with no
    /// claimed land has nothing to disclaim.
    /// </summary>
    private void AddManaCaveats(WorldState world, List<string> unsupported)
    {
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, _database);
        if (gain.All(g => g == 0)) return;

        unsupported.Add(
            "mana here has both halves of the economy but not all of either. Income is land and " +
            "a dungeon heart; outgoings are Create Imp's cast cost and the 7 a second every imp " +
            "charges its keeper (Q53, confirmed and now read off DK2's own Create Imp record). " +
            "What is missing is every other keeper spell - a summoned Horned Reaper costs 2000 a " +
            "second and Call To Arms 90, and neither spell exists yet - plus research and the " +
            "mana traps draw, which is reported and uninvestigated. A mana total in this run is " +
            "close to what a keeper would have, and short of every outgoing but one");
        unsupported.Add(
            "mana is paid once a game second, and a game second is four ticks of 250 ms - read " +
            "from the binary and stopwatched by the owner, so Q1 is settled and the wall-clock " +
            "rate these totals imply is now a claim this project stands behind");

        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(_database);
        if (heartTerrain is null) return;

        for (int i = 0; i < world.TileCount; i++)
        {
            if (world.TileAtIndex(i).TerrainId != heartTerrain) continue;

            unsupported.Add(
                "a dungeon heart on this map is terrain and a mana source and nothing else. In DK2 " +
                "losing it is defeat, and defeat is not implemented: a keeper whose heart is beaten " +
                "down here simply stops earning the heart's 30 a second and plays on");
            return;
        }
    }

    /// <summary>
    /// Lays down the scenario's rooms, and says what laying them down does not
    /// amount to.
    /// </summary>
    private void PlaceRooms(Scenario scenario, WorldState world, List<string> unsupported)
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
    private void PlaceResources(Scenario scenario, WorldState world, List<string> unsupported)
    {
        foreach (ScenarioPlayer p in scenario.Setup.Players)
        {
            world.Player(p.Id).Mana = p.Mana;

            if (p.Gold <= 0) continue;

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

            world.SpawnObject(
                _database, definition.ObjectId, o.Player,
                (o.At[1] * world.Width) + o.At[0]);
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
    private void SpawnCreatures(Scenario scenario, WorldState world, List<string> unsupported)
    {
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

    /// <summary>Turns the scenario's scheduled commands into one input frame per tick.</summary>
    private Dictionary<int, InputFrame> BuildInput(Scenario scenario, List<string> unsupported)
    {
        var byTick = new Dictionary<int, List<PlayerCommand>>();

        foreach (ScenarioCommand c in scenario.Commands)
        {
            if (c.AtTick < 0 || c.AtTick >= scenario.DurationTicks)
            {
                unsupported.Add(
                    $"commands entry at_tick={c.AtTick} falls outside the run (0..{scenario.DurationTicks - 1})");
                continue;
            }

            if (!byTick.TryGetValue(c.AtTick, out List<PlayerCommand>? list))
                byTick[c.AtTick] = list = [];

            if (c.Tag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Tag), true));
            if (c.Untag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Untag), false));

            bool built = c.Build is not null && TryBuild(c, list, unsupported);
            if (c.Sell.Count > 0) list.Add(new SellRoomCommand(c.Player, ToTiles(c.Sell)));
            if (c.CreateImp) list.Add(new CreateImpCommand(c.Player));

            if (c.PickUp > 0) list.Add(new PickUpCreatureCommand(c.Player, c.PickUp));
            if (c.PickUpObject > 0)
                list.Add(new PickUpObjectCommand(c.Player, c.PickUpObject));
            if (c.Slap > 0) list.Add(new SlapCreatureCommand(c.Player, c.Slap));

            bool dropped = c.Drop.Count > 0;
            if (dropped)
            {
                if (c.Drop.Count != 2)
                {
                    unsupported.Add(
                        $"commands entry at_tick={c.AtTick} has a drop that is not [x, y]");
                    dropped = false;
                }
                else
                {
                    list.Add(new DropCommand(
                        c.Player, new TilePos(c.Drop[0], c.Drop[1])));
                }
            }

            if (c.Tag.Count == 0 && c.Untag.Count == 0 && !built
                && c.Sell.Count == 0 && !c.CreateImp
                && c.PickUp == 0 && c.PickUpObject == 0 && c.Slap == 0 && !dropped)
                unsupported.Add($"commands entry at_tick={c.AtTick} names nothing this build can do");
        }

        return byTick.ToDictionary(kv => kv.Key, kv => new InputFrame(kv.Value));
    }

    private bool TryBuild(ScenarioCommand c, List<PlayerCommand> into, List<string> unsupported)
    {
        ScenarioBuild build = c.Build!;
        RoomDef? room = _database.RoomByName(build.Type);
        if (room is null)
        {
            unsupported.Add(
                $"commands entry at_tick={c.AtTick} builds '{build.Type}', which is not a room in the database");
            return false;
        }

        List<TilePos> tiles = ToTiles(build.Tiles);
        if (tiles.Count == 0)
        {
            unsupported.Add($"commands entry at_tick={c.AtTick} builds '{build.Type}' on no tiles");
            return false;
        }

        into.Add(new BuildRoomCommand(c.Player, room.RoomId, tiles));
        return true;
    }

    private static List<TilePos> ToTiles(List<List<int>> pairs) =>
        pairs.Where(p => p.Count == 2).Select(p => new TilePos(p[0], p[1])).ToList();

    /// <summary>Validates the scenario's work entries, dropping and reporting bad ones.</summary>
    private static List<ScenarioWork> ResolveWork(
        Scenario scenario, WorldState world, List<string> unsupported)
    {
        var usable = new List<ScenarioWork>();

        foreach (ScenarioWork w in scenario.Work)
        {
            if (!Enum.TryParse(w.Kind, ignoreCase: true, out TileWorkKind _))
            {
                unsupported.Add(
                    $"work kind '{w.Kind}' is not one of {string.Join(", ", Enum.GetNames<TileWorkKind>())}");
                continue;
            }

            if (w.At.Count != 2 || !world.Contains(w.At[0], w.At[1]))
            {
                unsupported.Add($"work entry at [{string.Join(",", w.At)}] is not a tile on this map");
                continue;
            }

            if (w.Repeat < 1)
            {
                unsupported.Add($"work entry at_tick={w.AtTick} has repeat={w.Repeat}");
                continue;
            }

            usable.Add(w);
        }

        return usable;
    }

    private static void InjectWork(WorldState world, List<ScenarioWork> work, int tick)
    {
        foreach (ScenarioWork w in work)
        {
            int last = w.UntilTick < w.AtTick ? w.AtTick : w.UntilTick;
            if (tick < w.AtTick || tick > last) continue;

            var kind = Enum.Parse<TileWorkKind>(w.Kind, ignoreCase: true);
            for (int i = 0; i < w.Repeat; i++)
                world.QueueWork(w.At[0], w.At[1], kind, w.Player);
        }
    }

    /// <summary>Places the level's own THINGS, when the scenario asks for them. Refused rather
    /// than ignored on a synthetic map: a scenario that says <c>placed_things: true</c> over
    /// <c>flat_24x20</c> is asking for something that does not exist, and silently giving it an
    /// empty level would make the scenario look like it had run.</summary>
    private void PlaceShippedThings(Scenario scenario, WorldState world, List<string> unsupported)
    {
        if (!scenario.Setup.PlacedThings) return;

        if (!scenario.Map.StartsWith(CampaignPrefix, StringComparison.OrdinalIgnoreCase))
        {
            unsupported.Add(
                $"setup.placed_things needs a '{CampaignPrefix}' map; '{scenario.Map}' has no THINGS file");
            return;
        }

        string level = scenario.Map[CampaignPrefix.Length..].Trim();
        if (_installDir is null)
            throw new InvalidOperationException(
                $"Scenario needs the placed things of '{level}' but no DK2 installation was found. Set DK2_DIR.");

        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(_installDir, level)
            ?? throw new InvalidOperationException(
                $"No THINGS file for campaign level '{level}' under Data/editor/maps.");

        ThingProjectionResult result = ThingProjection.Place(world, _database, things);

        // Reported, not swallowed. Doors, traps, hero parties and effect
        // generators have nowhere to go yet, so a scenario built on a level
        // with any of them says so in its own output.
        if (result.NotYetProjected > 0)
            unsupported.Add(
                $"{level}: {result.NotYetProjected} placed things this build cannot project yet "
                + "(doors, traps, hero parties, effect generators)");
    }

    /// <summary>Resolves a scenario's map field. <c>campaign:level1</c> loads a shipped map;
    /// <c>flat_24x20</c> builds a synthetic dungeon with an impenetrable border and dirt path
    /// inside, <c>rock_24x20</c> the same filled with diggable rock, and <c>chamber_24x20</c>
    /// solid rock with a small open chamber at the centre, which is what an experiment with
    /// workers needs since a worker has to stand somewhere. All three are testing conveniences
    /// and none is a DK2 layout.</summary>
    private LevelMap ResolveMap(string spec)
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

    /// <summary>Writes the run's evidence trail under the given directory.</summary>
    public static void WriteReport(ScenarioResult result, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        File.WriteAllLines(
            Path.Combine(outputDir, "hashes.tsv"),
            result.HashPerTick.Select((h, i) => i.ToString(CultureInfo.InvariantCulture) + "\t" + h.ToString("x16", CultureInfo.InvariantCulture)));

        File.WriteAllLines(
            Path.Combine(outputDir, "events.log"),
            result.Events.Select(e => e.ToString()));

        var report = new StringBuilder();
        report.AppendLine($"# Scenario run: {result.Scenario.Id}");
        report.AppendLine();
        report.AppendLine($"- source: {result.Scenario.SourcePath}");
        report.AppendLine($"- map: {result.Scenario.Map}");
        report.AppendLine($"- seed: {result.Scenario.Seed}");
        report.AppendLine($"- balance table: {result.BalanceTable}");
        report.AppendLine($"- ticks run: {result.TicksRun}");
        report.AppendLine($"- final state hash: {result.FinalStateHash:x16}");
        report.AppendLine($"- events recorded: {result.Events.Count}");
        report.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.Scenario.Question))
        {
            report.AppendLine("## Question");
            report.AppendLine();
            report.AppendLine(result.Scenario.Question.Trim());
            report.AppendLine();
        }

        if (result.Scenario.Expect.Count > 0)
        {
            report.AppendLine("## What this run claims");
            report.AppendLine();
            report.AppendLine(result.ExpectationFailures.Count == 0
                ? $"All {result.Scenario.Expect.Count} expectation(s) held."
                : $"{result.ExpectationFailures.Count} of {result.Scenario.Expect.Count} expectation(s) FAILED.");
            report.AppendLine();
            foreach (ScenarioExpectation expectation in result.Scenario.Expect)
                report.AppendLine($"- {expectation}{Environment.NewLine}  because {expectation.Because.Trim()}");

            foreach (string failure in result.ExpectationFailures)
                report.AppendLine($"- **FAILED**: {failure}");

            report.AppendLine();
        }

        report.AppendLine("## Not answered by this run");
        report.AppendLine();
        if (result.Unsupported.Count == 0)
        {
            report.AppendLine("Nothing - every part of the scenario was applied.");
        }
        else
        {
            report.AppendLine("This build could not act on the following. Any conclusion drawn");
            report.AppendLine("from this run covers the remainder only.");
            report.AppendLine();
            foreach (string item in result.Unsupported)
                report.AppendLine($"- {item}");
        }

        if (!string.IsNullOrEmpty(result.Picture))
        {
            report.AppendLine();
            report.AppendLine("## The world at the end of the run");
            report.AppendLine();
            report.AppendLine($"Art: {result.AssetSummary}.");
            report.AppendLine();
            report.AppendLine(TextRenderer.Legend());
            report.AppendLine();
            report.AppendLine("```");
            report.Append(result.Picture);
            report.AppendLine("```");

            File.WriteAllText(Path.Combine(outputDir, "world.txt"), result.Picture);
        }

        File.WriteAllText(Path.Combine(outputDir, "report.md"), report.ToString());
    }
}
