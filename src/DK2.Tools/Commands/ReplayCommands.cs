// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Tools.Commands;

internal static class ReplayCommands
{
    /// <summary>
    /// Runs one campaign level's trigger tree for a number of turns and writes the
    /// probe's CSV shape, so our side can be diffed against a live capture.
    /// </summary>
    internal static int RunTriggerCsv(
        string levelName, int turns, string outPath, InputLog? log = null, RoomLog? rooms = null,
        string? tilePath = null, PacketLog? packet = null,
        PacketEntityMap? entityIds = null, string? creaturePath = null,
        string? eventPath = null, bool dk2Random = false, string? seedPath = null, uint? seed = null,
        ResyncState? resync = null, long resyncAt = 0, string? jobPath = null,
        IReadOnlyDictionary<int, int>? resyncMap = null, ResyncParts resyncParts = ResyncParts.All,
        bool retryBlueprints = false)
    {
        string? install = Dk2Install.Find();
        if (install is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }

        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, levelName);
        LevelDef? level = GameDatabaseLoader.LoadCampaignLevel(install, levelName);
        List<TriggerRecord>? records = GameDatabaseLoader.LoadCampaignTriggers(install, levelName);
        List<PlayerDef>? players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName);
        List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, levelName);
        if (map is null || level is null || records is null || players is null || things is null)
        {
            Console.Error.WriteLine($"'{levelName}' is missing map, level, trigger, player or thing data.");
            return 2;
        }

        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
        WorldState world = WorldState.FromMap(
            database, map, players.Select(p => p.PlayerId).ToArray());
        // The parties come from THINGS, not TRIGGERS, and without them twenty-four of the
        // world's twenty-five roots are empty and most of a level's script never runs.
        HeroPartyThing[] parties = things.OfType<HeroPartyThing>().ToArray();
        // And the action points, which carry a root each and the rectangle every area
        // condition measures over. A world with none of them walks no area tree.
        ThingProjection.Place(world, database, things, players.ToDictionary(p => p.PlayerId, p => p.StartingGold));
        var tree = new TriggerTree(
            records, level.TriggerId, parties,
            players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        // A triggers-only run is right for an idle capture -- nothing in one depends on the
        // world changing, which is why both matched -- but it cannot honour input at all,
        // because TileTagSystem is not there to read it. A replay therefore runs the
        // declared system list; an idle run keeps the shape its baselines were measured in.
        // --rng dk2: the original's generator from its level-start seed (ADR-0275), so a replay
        // draws as DK2 does wherever our draws line up with its own.
        var simulation = new GameSimulation(
            database, world, dk2Random
                ? new SimRandom(seed is { } s ? new Dk2Lcg(s) : new Dk2Lcg())
                : new SimRandom(seed ?? 0),
            systems: log is null && rooms is null && packet is null ? null : SimulationSystems.Default(),
            triggers: tree)
        {
            TrackTriggerReach = true,
        };

        // A replay's tags are worth nothing if they never land, and a wrong reading of the
        // task pool looks exactly like a level nobody dug: counting what TileTagSystem
        // accepted turns that silent failure into a number.
        using var eventWriter = eventPath is null ? null : new StreamWriter(eventPath);
        eventWriter?.WriteLine("turn\tsystem\tkind\tdetail");
        var tally = new TagTally(eventWriter);
        if (log is not null || rooms is not null || packet is not null) simulation.ObserveWith(tally);

        // The same tiles the probe was sampling, so the two traces line up tile for
        // tile. Watching only those costs a handful of reads a tick instead of a sweep,
        // and a tile the capture never mentions has nothing to compare against anyway.
        // A packet replay has no probe to line up with: it watches every tile, for comparing
        // against a full-state capture's tile log.
        TilePos[] watched = tilePath is null
            ? []
            : log is not null
                ? [.. log.Watched.OrderBy(t => t.Y).ThenBy(t => t.X)]
                : [.. Enumerable.Range(0, world.Height)
                    .SelectMany(y => Enumerable.Range(0, world.Width).Select(x => new TilePos(x, y)))];
        using var tileWriter = tilePath is null ? null : new StreamWriter(tilePath);
        // --seeds: DK2's generator state after each tick, to count our draws against a capture's.
        using var seedWriter = seedPath is null ? null : new StreamWriter(seedPath);
        seedWriter?.WriteLine("tick,seed");
        tileWriter?.WriteLine("tick,x,y,terrain,health,owner,tagged");
        var seen = new Dictionary<TilePos, (byte Terrain, int Health, byte Owner, ushort Tagged)>();
        using var creatureWriter = creaturePath is null ? null : new StreamWriter(creaturePath);
        creatureWriter?.WriteLine("tick,id,type,owner,x,y,health,state,target");
        var seenCreatures = new Dictionary<int,
            (byte Type, byte Owner, TilePos Tile, int Health, int State, int Target)>();
        // --jobs: each creature's state, resume state and task as a worker's choice leaves them,
        // a row on change, for comparing Imp choices after a resync with the capture's.
        using var jobWriter = jobPath is null ? null : new StreamWriter(jobPath);
        jobWriter?.WriteLine("tick,id,state,resume,task,work,lastkind,lasttask,gold");
        var seenJobs = new Dictionary<int, (int State, int Resume, int Task, TileWorkKind Work, ImpTaskKind Last, int LastTile, int Gold)>();

        int goldAsked = 0, goldMissed = 0, goldMoved = 0, dropsMoved = 0;
        // A recorded door or trap placement on a tile DK2's keeper owned but ours does not yet,
        // held until ours owns it (see Deferred below).
        var pendingBlueprints = new List<(long Turn, PlayerCommand Command)>();
        int blueprintsHeld = 0, blueprintsPlacedLater = 0;
        using (var writer = new StreamWriter(outPath))
        {
            simulation.EmitTriggerTurnsTo(writer);
            for (int i = 0; i < turns; i++)
            {
                // The logs are in DK2's turn numbers, which run one ahead of our tick.
                long turn = simulation.CurrentTick.Value + 1;
                simulation.Tick(packet is null
                    ? Merge(log?.At(turn), rooms?.At(turn))
                    : Deferred(turn, packet.At(turn, entityIds ?? new PacketEntityMap(), GoldOnTile, DropAt)));

                if (seedWriter is not null && simulation.Rng.Algorithm is Dk2Lcg lcg)
                    seedWriter.WriteLine($"{turn},{lcg.Seed}");

                foreach (TilePos at in watched)
                {
                    ref TileState tile = ref world.Tile(at);
                    var now = (tile.TerrainId, tile.Health, tile.OwnerPlayerId, tile.TaggedBy.Bits);
                    if (seen.TryGetValue(at, out (byte, int, byte, ushort) was) && was == now) continue;
                    seen[at] = now;
                    tileWriter?.WriteLine(
                        $"{turn},{at.X},{at.Y},{tile.TerrainId},{tile.Health},{tile.OwnerPlayerId},{tile.TaggedBy.Bits}");
                }

                if (creatureWriter is not null)
                    foreach (CreatureState creature in world.Creatures)
                    {
                        var now = (creature.CreatureTypeId, creature.OwnerPlayerId,
                            creature.Tile, creature.Health, creature.Dk2StateId,
                            creature.FightTargetId);
                        if (seenCreatures.TryGetValue(creature.Id, out var was) && was == now)
                            continue;
                        seenCreatures[creature.Id] = now;
                        creatureWriter.WriteLine($"{turn},{creature.Id},{now.CreatureTypeId},"
                            + $"{now.OwnerPlayerId},{now.Tile.X},{now.Tile.Y},"
                            + $"{now.Health},{now.Dk2StateId},{now.FightTargetId}");
                    }

                if (jobWriter is not null)
                    foreach (CreatureState creature in world.Creatures)
                    {
                        var now = (creature.Dk2StateId, creature.UtilityNewState, creature.TargetTileIndex,
                            creature.TargetWork, creature.LastTaskKind, creature.LastTaskTileIndex, creature.GoldHeld);
                        if (seenJobs.TryGetValue(creature.Id, out var was) && was == now) continue;
                        seenJobs[creature.Id] = now;
                        jobWriter.WriteLine($"{turn},{creature.Id},{now.Dk2StateId},{now.UtilityNewState},"
                            + $"{now.TargetTileIndex},{now.TargetWork},{now.LastTaskKind},{now.LastTaskTileIndex},{now.GoldHeld}");
                    }

                // --resync: this turn's state is DK2's, and the rest of the run continues from it.
                if (resync is not null && turn == resyncAt)
                {
                    // A slot named in --resync-map is that creature; the rest resolve through the
                    // packet replay's entity map at this turn. The packets after it name DK2's
                    // creatures by slot, so the resync's pairs replace the map's from the next turn.
                    PacketEntityMap ids = entityIds ??= new PacketEntityMap();
                    ResyncReport report = WorldResync.Apply(simulation, resync,
                        slot => resyncMap is not null && resyncMap.TryGetValue(slot, out int named) ? named
                            : ids.TryResolve((uint)slot, turn, PacketEntityKind.Creature, out int id) ? id : null,
                        resyncParts);
                    if (resyncParts != ResyncParts.All) Console.WriteLine($"  resync skips: {ResyncParts.All & ~resyncParts}");
                    foreach ((int slot, int ours) in report.Pairs ?? [])
                        ids.Set((uint)slot, turn + 1, ours, PacketEntityKind.Creature);
                    Console.WriteLine($"  resync at turn {turn} from DK2's tick {resync.Tick}: {report}");
                    foreach (string note in report.Notes) Console.WriteLine($"    {note}");
                }
            }
        }

        if (tilePath is not null)
            Console.WriteLine($"  tiles: {watched.Length} watched -> {tilePath}");
        // A pickup of nothing is dropped silently by the hand, so the miss is counted here.
        if (goldAsked > 0)
            Console.WriteLine($"  gold: {goldAsked} pickups, {goldMissed} found no pile of ours on DK2's tile or next to it, "
                + $"{goldMoved} took ours from the next tile");
        if (dropsMoved > 0)
            Console.WriteLine($"  drops: {dropsMoved} moved to the nearest tile they land on, as the player would");
        if (blueprintsHeld > 0)
            Console.WriteLine($"  blueprints: {blueprintsHeld} held for a tile ours did not own yet, {blueprintsPlacedLater} "
                + $"sent once it did, {pendingBlueprints.Count} never");

        // How many workers were available to answer the marks. A capture's approach
        // times cannot be compared against ours without it: two imps and five imps
        // dig the same tile at the same rate and reach the second one very differently.
        foreach (PlayerDef p in players)
        {
            int imps = ImpSupplyRules.ImpCount(world, database, p.PlayerId);
            if (imps > 0) Console.WriteLine($"  imps: player {p.PlayerId} has {imps}");
            if (log is not null || rooms is not null || packet is not null)
            {
                PlayerState player = world.Player(p.PlayerId);
                Console.WriteLine($"  result: player {p.PlayerId} status={player.Status} "
                    + $"at tick={player.StatusTick.Value}");
            }
        }

        if (log is not null || rooms is not null || packet is not null) tally.Report();

        int occupied = tree.Parties.Count(s => s.TriggerId != 0);
        Console.WriteLine($"{levelName}: {turns} turns, root trigger {tree.RootTriggerId} "
            + $"repeat {tree.RootRepeat}, {occupied} occupied party roots -> {outPath}");

        // The entity-root counts make an empty output column readable: zero roots and roots
        // that all answered false are different observations.
        int apRoots = things.OfType<ActionPointThing>().Count(a => a.TriggerId != 0);
        int creatureRoots = things.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0)
            + things.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0);
        Console.WriteLine($"  walked: {tree.PlayerRoots.Count} player roots "
            + $"[{string.Join(",", tree.PlayerRoots)}], {apRoots} action point roots, "
            + $"{creatureRoots} creature roots");
        // The count is what the walk declined; `gates` is what that cost -- the records
        // behind those declines it never reached because of them. They are not the same
        // number and do not rank the same way: one decline can foreclose a hundred records
        // and a dozen can foreclose none (`TriggerReach`).
        IReadOnlyList<TriggerGate> gates = simulation.LastTriggerRun is null
            ? []
            : TriggerReach.Gated(tree, simulation.LastTriggerRun);
        foreach (TriggerCensusRow r in
                 (simulation.LastTriggerRun?.Summary ?? []).OrderByDescending(s => s.Count))
        {
            int foreclosed = gates
                .Where(g => g.Blocked.Type == r.Type && g.Blocked.Context == r.Context)
                .Sum(g => g.Records);
            Console.WriteLine(
                $"  blocked tag {r.Tag} type {r.Type} in {r.Context}: {r.Count}"
                + $" -- gates {foreclosed} records");
        }

        // Say plainly what ran only in part. These descend as DK2's do and leave the world
        // untouched, so anything downstream that waits on their effect -- a hero existing, a
        // tile changing hands -- cannot happen in this run, and a divergence there is ours.
        foreach (TriggerCensusRow r in
                 (simulation.LastTriggerRun?.PartialSummary ?? []).OrderByDescending(s => s.Count))
            Console.WriteLine($"  PARTIAL tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
                + "-- descended, effect missing");

        // And what DK2 itself refuses: dead script, not a gap of ours.
        foreach (TriggerCensusRow r in
                 (simulation.LastTriggerRun?.UnhandledSummary ?? []).OrderByDescending(s => s.Count))
            Console.WriteLine($"  UNHANDLED tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
                + "-- DK2's own dispatcher returns zero here");

        // And what was answered from an assumption rather than from state.
        foreach (TriggerCensusRow r in
                 (simulation.LastTriggerRun?.AssumedSummary ?? []).OrderByDescending(s => s.Count))
            Console.WriteLine($"  ASSUMED tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
                + "-- taken as true: no modal interface state in a headless run");
        return 0;

        // A recorded door or trap placement was made on a tile DK2's keeper had claimed. Ours may not
        // have claimed it yet, and refusing it would leave the dungeon without a door or trap the
        // player built for the rest of the run, which no player does: hold the placement and send
        // it on the first turn ours owns the tile (owner, 2026-10-28). Every other refusal (solid,
        // a room, occupied, gold) is the simulation's, as recorded.
        // Off unless --blueprints retry: a replay's results stay comparable with the frozen rigs'.
        InputFrame Deferred(long turn, InputFrame frame)
        {
            if (!retryBlueprints) return frame;
            List<PlayerCommand>? kept = null;
            foreach (PlayerCommand command in frame.All)
            {
                if (command is PlaceDoorCommand or PlaceTrapCommand && !OwnsTile(command))
                {
                    pendingBlueprints.Add((turn, command));
                    blueprintsHeld++;
                    eventWriter?.WriteLine($"{turn}	replay	blueprint_held	{Describe(command)}");
                    continue;
                }
                (kept ??= []).Add(command);
            }
            for (int i = 0; i < pendingBlueprints.Count; i++)
            {
                (long heldAt, PlayerCommand command) = pendingBlueprints[i];
                if (heldAt == turn || !OwnsTile(command)) continue;
                (kept ??= []).Add(command);
                pendingBlueprints.RemoveAt(i--);
                blueprintsPlacedLater++;
                eventWriter?.WriteLine($"{turn}	replay	blueprint_sent_later	{Describe(command)} recorded={heldAt}");
            }
            return kept is null ? (frame.All.Count == 0 ? frame : InputFrame.Empty) : new InputFrame(kept);
        }

        bool OwnsTile(PlayerCommand command)
        {
            TilePos at = command switch { PlaceDoorCommand d => d.Tile, PlaceTrapCommand t => t.Tile, _ => default };
            return world.Contains(at) && world.Tile(at).OwnerPlayerId == command.PlayerId;
        }

        static string Describe(PlayerCommand command) => command switch
        {
            PlaceDoorCommand d => $"player={d.PlayerId} kind=door type={d.DoorId} at=({d.Tile.X},{d.Tile.Y})",
            PlaceTrapCommand t => $"player={t.PlayerId} kind=trap type={t.TrapId} at=({t.Tile.X},{t.Tile.Y})",
            _ => command.ToString() ?? string.Empty,
        };

        // A packet's gold pickup names DK2's pile; ours is whichever loose pile lies on its tile.
        // A recorded drop lands where DK2's world let it. Ours may not have dug or claimed that
        // tile yet, and a refused drop would leave the creature in the hand for the rest of the
        // run, which no player does: put it on the nearest tile it lands on instead (owner,
        // 2026-09-29: the nearest claimed tile, or dirt for an imp). Objects are left alone.
        TilePos DropAt(byte player, TilePos tile)
        {
            List<HandItem> hand = world.Player(player).Hand;
            if (hand.Count == 0 || hand[^1].IsObject) return tile;
            HandItem held = hand[^1];
            if (world.CreatureById(held.Id) is null) return tile;
            if (HandRules.DropLands(world, database, player, held, tile)) return tile;

            TilePos best = tile;
            long bestDistance = long.MaxValue;
            for (int r = 1; r <= Math.Max(world.Width, world.Height) && bestDistance == long.MaxValue; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var at = new TilePos(tile.X + dx, tile.Y + dy);
                    long d = ((long)dx * dx) + ((long)dy * dy);
                    if (d >= bestDistance || !HandRules.DropLands(world, database, player, held, at))
                        continue;
                    best = at;
                    bestDistance = d;
                }
            }
            if (bestDistance != long.MaxValue) dropsMoved++;
            return best;
        }

        // Our imps put gold down where they stand, which can be the next tile over from DK2's
        // pile; a player grabs the pile they see, so a pile on a neighbouring tile stands in
        // (l2nohand: 16 of 39 grabs missed, and the Library then lacked the gold for 6 tiles).
        int GoldOnTile(int tileIndex)
        {
            goldAsked++;
            int x = tileIndex % world.Width, y = tileIndex / world.Width;
            int best = 0, bestDistance = int.MaxValue;
            foreach (ObjectState thing in world.Objects)
            {
                if (thing.InHand || thing.CarriedByCreatureId >= 0
                    || database.ObjectById(thing.ObjectTypeId) is not { IsGold: true })
                    continue;
                int dx = Math.Abs(thing.TileIndex % world.Width - x), dy = Math.Abs(thing.TileIndex / world.Width - y);
                if (Math.Max(dx, dy) > 1 || dx + dy >= bestDistance) continue;
                best = thing.Id;
                bestDistance = dx + dy;
            }
            if (best == 0) goldMissed++;
            else if (bestDistance > 0) goldMoved++;
            return best;
        }
    }

    /// <summary>
    /// Runs a level driven by a captured task-pool log, so our side of a played
    /// capture can be diffed against DK2's the way an idle one already is.
    /// </summary>
    internal static int Replay(string[] arguments)
    {
        int turns = 600;
        long lag = 1;
        long origin = 1;
        string? roomPath = null;
        string? tilePath = null;
        string? packetPath = null;
        int? packetOffset = null;
        string? entityMapPath = null;
        string? creaturePath = null;
        string? eventPath = null;
        bool dk2Random = false;
        string? seedPath = null;
        uint? seed = null;
        string? resyncPath = null;
        long resyncAt = 0;
        string? jobPath = null;
        string? resyncMapPath = null;
        ResyncParts resyncParts = ResyncParts.All;
        bool retryBlueprints = false;
        for (int i = 4; i < arguments.Length; i += 2)
        {
            if (i + 1 >= arguments.Length) return ReplayUsage();
            string value = arguments[i + 1];
            System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
            switch (arguments[i])
            {
                case "--turns" when int.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out int t):
                    turns = t;
                    break;
                case "--lag" when long.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out long l):
                    lag = l;
                    break;
                case "--origin" when long.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out long o):
                    origin = o;
                    break;
                case "--rooms":
                    roomPath = value;
                    break;
                case "--tiles":
                    tilePath = value;
                    break;
                case "--packet-offset" when int.TryParse(value, out int offset) && offset >= 0:
                    packetOffset = offset;
                    break;
                case "--packet":
                    packetPath = value;
                    break;
                case "--creature-map":
                case "--entity-map":
                    entityMapPath = value;
                    break;
                case "--creatures":
                    creaturePath = value;
                    break;
                case "--events":
                    eventPath = value;
                    break;
                case "--seeds":
                    seedPath = value;
                    break;
                // --seed: a different starting seed, so repeated replays sample the spread of
                // outcomes one recorded playback can take.
                case "--seed" when uint.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out uint sd):
                    seed = sd;
                    break;
                case "--rng" when value is "dk2" or "xoshiro":
                    dk2Random = value == "dk2";
                    break;
                // --resync: a capture_state.py tick (--state-only) copied over our world after the
                // turn --resync-at, the controlled-input test (WorldResync).
                case "--resync":
                    resyncPath = value;
                    break;
                case "--resync-at" when long.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out long at) && at > 0:
                    resyncAt = at;
                    break;
                case "--jobs":
                    jobPath = value;
                    break;
                // --resync-map: slot,ours rows naming our creature for a native slot at the resync,
                // where the replay's entity map (converged for another build) names a wrong one.
                // --blueprints retry: hold a recorded door or trap placement on a tile ours does not
                // own yet and send it once ours does (the drop rule's reasoning, for blueprints).
                case "--blueprints" when value == "retry":
                    retryBlueprints = true;
                    break;
                case "--resync-map":
                    resyncMapPath = value;
                    break;
                // --resync-skip: parts of the capture left ours (WorldResync's ResyncParts, by name,
                // comma-separated), to split which of DK2's state an outcome depends on.
                case "--resync-skip":
                    foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // By name only: a number would parse too, and a combination would skip silently.
                        if (!Enum.GetNames<ResyncParts>().Contains(part, StringComparer.OrdinalIgnoreCase)
                            || !Enum.TryParse(part, ignoreCase: true, out ResyncParts skip) || skip is ResyncParts.None or ResyncParts.All)
                            return ReplayUsage();
                        resyncParts &= ~skip;
                    }
                    break;
                default:
                    return ReplayUsage();
            }
        }

        InputLog? log = null;
        RoomLog? rooms = null;
        PacketLog? packet = null;
        PacketEntityMap? entityIds = null;
        ResyncState? resync = null;
        Dictionary<int, int>? resyncMap = null;
        try
        {
            if (resyncPath is null && resyncParts != ResyncParts.All)
                throw new InvalidDataException("--resync-skip needs --resync.");
            if (resyncPath is not null)
            {
                if (packetPath is null || resyncAt <= 0)
                    throw new InvalidDataException("--resync needs a packet replay and --resync-at <turn>.");
                resync = ResyncStateFile.Load(resyncPath);
                if (resync.Tick != resyncAt)
                    Console.WriteLine($"  note: {resyncPath} is DK2's tick {resync.Tick}, applied after our turn {resyncAt}");
            }
            if (resyncMapPath is not null)
            {
                resyncMap = [];
                foreach (string line in File.ReadLines(resyncMapPath))
                {
                    if (line.Length == 0 || line.StartsWith("slot", StringComparison.Ordinal)) continue;
                    string[] fields = line.Split(',');
                    if (fields.Length != 2 || !int.TryParse(fields[0], out int slot) || !int.TryParse(fields[1], out int ours))
                        throw new InvalidDataException($"{resyncMapPath}: expected slot,ours rows, got '{line}'.");
                    resyncMap[slot] = ours;
                }
            }
            if (packetPath is null)
            {
                log = InputLog.Read(arguments[2], lag, origin);
                if (roomPath is not null) rooms = RoomLog.Read(roomPath, lag, origin);
            }
            else
            {
                if (arguments[2] != "-" || roomPath is not null)
                    throw new InvalidDataException("Packet replay requires tags path - and no --rooms input.");
                if (packetOffset is not int start)
                    throw new InvalidDataException("Packet replay requires --packet-offset for the verified first record (69 for the Level 1 capture).");
                packet = PacketLog.Read(packetPath, turns, start);
                entityIds = PacketEntityMap.Read(entityMapPath);
                string[] unmapped = [.. packet.EntityReferences
                    .Where(reference => !entityIds.TryResolve(
                        reference.Original, reference.Turn, reference.Kind, out _))
                    .Select(reference => $"{reference.Original}@{reference.Turn}")];
                if (unmapped.Length > 0)
                    throw new InvalidDataException(
                        $"Packet entities need observed ID mappings: {string.Join(", ", unmapped)}. "
                        + "Pass --entity-map original,from_turn,ours,kind.csv.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (packet is not null)
        {
            Console.WriteLine($"{packetPath}: {packet.ActionCount} actions on {packet.TurnCount} turns");
        }
        if (log is not null)
        {
        InputLogTally t2 = log.Tally;
        Console.WriteLine($"{arguments[2]}: {t2.Rows} rows -> {t2.Commands} commands over "
            + $"{log.Turns.Count} turns, probe turns {t2.FirstStamp}..{t2.LastStamp}");
        if (t2.Migrations > 0)
            Console.WriteLine($"  {t2.Migrations} rows moved a tile between kind lists without "
                + "adding or removing it -- the pool is a pipeline, not a set of marks");
        if (t2.Worked > 0)
            Console.WriteLine($"  {t2.Worked} pool changes were DK2's own imps taking or "
                + $"finishing work, not the player (mark kinds are "
                + $"{string.Join(", ", InputLog.TagKinds.Order())})");
        if (t2.Before > 0)
            Console.WriteLine($"  {t2.Before} commands dated before turn 1 and dropped -- the "
                + $"probe attached after the level began, or --origin {origin} is wrong");
        if (log.Turns.Count > 0 && log.Turns[^1] > turns)
            Console.WriteLine($"  the log runs to turn {log.Turns[^1]} but the run stops at "
                + $"{turns}; raise --turns to replay all of it");
        }

        if (rooms is not null)
        {
            RoomLogTally r = rooms.Tally;
            Console.WriteLine($"{roomPath}: {r.Rows} rows -> {r.Commands} commands over "
                + $"{rooms.Turns.Count} turns");
            if (r.Unsettled > 0)
                Console.WriteLine($"  {r.Unsettled} rows skipped -- bounds did not enclose exactly "
                    + "the slab count, so the room was still being built");
            if (r.Before > 0)
                Console.WriteLine($"  {r.Before} commands dated before turn 1 and dropped");
            if (r.Disowned > 0)
                Console.WriteLine($"  {r.Disowned} rows whose room names a different owner than "
                    + "the list it was found in -- THE ROOM WALK IS WRONG, not the room");
        }

        return RunTriggerCsv(arguments[1], turns, arguments[3],
            log, rooms, tilePath, packet, entityIds, creaturePath, eventPath, dk2Random, seedPath, seed,
            resync, resyncAt, jobPath, resyncMap, resyncParts, retryBlueprints);
    }

    /// <summary>
    /// One tick's input from both logs. They are separate recordings of one
    /// player's session, so a turn can carry commands from each; tags come first,
    /// which is the declared system order and so cannot change what a room build
    /// sees.
    /// </summary>
    internal static InputFrame Merge(InputFrame? tags, InputFrame? rooms)
    {
        IReadOnlyList<PlayerCommand> a = tags?.All ?? [];
        IReadOnlyList<PlayerCommand> b = rooms?.All ?? [];
        if (b.Count == 0) return tags ?? InputFrame.Empty;
        if (a.Count == 0) return rooms ?? InputFrame.Empty;
        return new InputFrame([.. a, .. b]);
    }

    /// <summary><c>decide &lt;level&gt; &lt;decisions.jsonl&gt; &lt;out.tsv&gt;</c>: DK2's state-10 choices
    /// put to ours, one at a time, in DK2's world (<see cref="DecisionCheck"/>).</summary>
    internal static int Decide(string levelName, string inPath, string outPath)
    {
        string? install = Dk2Install.Find();
        if (install is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, levelName);
        List<PlayerDef>? players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName);
        if (map is null || players is null)
        {
            Console.Error.WriteLine($"'{levelName}' is missing map or player data.");
            return 2;
        }
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
        WorldState world = WorldState.FromMap(database, map, players.Select(p => p.PlayerId).ToArray());
        try
        {
            return DecisionCheck.Run(database, world, inPath, outPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    internal static int ReplayUsage()
    {
        Console.Error.WriteLine("usage: dk2tools replay <level> <tags.csv> <out.csv> "
            + "[--turns N] [--lag L] [--origin T] [--rooms rooms.csv] [--tiles tiles.csv]\n"
            + "       dk2tools replay <level> - <out.csv> "
            + "--packet recording.sav --packet-offset N [--entity-map ids.csv] [--turns N] "
            + "[--tiles tiles.csv] [--creatures creatures.csv] [--events events.tsv] [--jobs jobs.csv] "
            + "[--rng dk2|xoshiro] [--seed N] [--seeds seeds.csv] "
            + "[--resync tick.json --resync-at N [--resync-map slot,ours.csv] [--resync-skip part,...]] [--blueprints retry]");
        return 2;
    }
}

/// <summary>Counts what the replayed tags did, from the events the tag system already emits.</summary>
sealed class TagTally : ISimulationEventSink
{
    private readonly TextWriter? _events;
    private int _applied;
    private int _rejected;
    private int _picked;
    private int _dropped;
    private int _dropStunned;
    private int _pickupRefused;
    private int _dropRefused;
    private int _slapped;

    public TagTally(TextWriter? events = null) => _events = events;

    public void Emit(in SimEvent simEvent)
    {
        _events?.WriteLine($"{simEvent.Tick.Value + 1}\t{simEvent.System}\t"
            + $"{simEvent.Kind}\t{simEvent.Detail}");
        if (simEvent.System == "Hand")
        {
            switch (simEvent.Kind)
            {
                case "picked_up": _picked++; break;
                case "dropped": _dropped++; break;
                case "dropped_stunned": _dropped++; _dropStunned++; break;
                case "pickup_refused": _pickupRefused++; break;
                case "drop_refused": _dropRefused++; break;
                case "slapped": _slapped++; break;
            }
            return;
        }
        if (simEvent.System != "TileTag") return;
        foreach (string part in simEvent.Detail.Split(' '))
        {
            if (part.StartsWith("applied=", StringComparison.Ordinal))
                _applied += int.Parse(part[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (part.StartsWith("rejected=", StringComparison.Ordinal))
                _rejected += int.Parse(part[9..], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Rejections are the first-capture check. The pool's layout has been read but
    /// never sampled, so if its coordinates are not tile coordinates every tag lands
    /// off the map or on floor and this is where that shows, rather than in a
    /// divergence report that blames a rule.
    /// </summary>
    public void Report()
    {
        Console.WriteLine($"  tags: {_applied} applied, {_rejected} rejected");
        if (_picked + _dropped + _pickupRefused + _dropRefused + _slapped > 0)
            Console.WriteLine($"  hand: {_picked} picked, {_dropped} dropped "
                + $"({_dropStunned} stunned), "
                + $"{_pickupRefused} pickups refused, {_dropRefused} drops refused, "
                + $"{_slapped} slapped");
        if (_applied == 0 && _rejected > 0)
            Console.WriteLine("  NOTHING landed -- suspect the pool's coordinates rather "
                + "than the level");
    }
}
