// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;
using static DK2.Tools.Commands.CommandHelpers;

namespace DK2.Tools.Commands;

internal static partial class KwdCommands
{
    internal static int RunAlterTerrain(string[] args)
    {
        // Q74 (can a dug-out tile become rock again?): sweeps every shipped map's
        // TRIGGERS chunk for ALTER_TERRAIN_TYPE (action type 22) and reports the
        // terrain those actions write. Payload layout and its OpenKeeper
        // provenance: docs/research/external-resources.md; the 16-byte TRIGGERS
        // payload is CONFIRMED in docs/research/kwd-record-fields.md. Guard: every
        // decoded type lands in the enumerated range, every terrain id resolves.
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");
        GameDatabase db = GameDatabaseLoader.Load(root);

        const int ActionTypeAlterTerrain = 22;
        const int TriggerActionTag = 214;

        var writtenTerrain = new SortedDictionary<byte, int>();
        var actionTypeHistogram = new SortedDictionary<int, int>();
        int filesScanned = 0, actionRecords = 0, alterActions = 0;
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(mapsDir, "*Triggers.kld").OrderBy(f => f))
        {
            filesScanned++;

            // The level's own MAP, so each action can be compared against what
            // the tile starts as. "Writes Rock" only means "closes a tile" if
            // the tile was open to begin with.
            string mapFile = file[..^"Triggers.kld".Length] + "Map.kld";
            LevelMap? levelGrid = File.Exists(mapFile)
                ? MapGrid.Parse(KwdReader.ReadFile(mapFile)) : null;
            foreach (KwdChunk c in KwdReader.ReadFile(file))
            {
                if (!KwdTaggedRecords.IsTagged(c.Type)) continue;

                foreach (KwdTaggedRecords.TaggedRecord r in KwdTaggedRecords.Split(c))
                {
                    if (r.Tag != TriggerActionTag) continue;
                    ReadOnlySpan<byte> p = r.Payload.Span;
                    if (p.Length < 8) continue;

                    actionRecords++;
                    int actionType = p[p.Length - 2];
                    actionTypeHistogram.TryGetValue(actionType, out int seen);
                    actionTypeHistogram[actionType] = seen + 1;

                    if (actionType != ActionTypeAlterTerrain) continue;

                    alterActions++;
                    byte terrainId = p[0];
                    writtenTerrain.TryGetValue(terrainId, out int n);
                    writtenTerrain[terrainId] = n + 1;

                    // Trigger coordinates are 1-based (OpenKeeper subtracts one
                    // before touching the map); HYPOTHESIS, and the "was" column
                    // below is what judges it.
                    int posX = p[4] | (p[5] << 8);
                    int posY = p[6] | (p[7] << 8);
                    string name = db.Terrain.TryGetValue(terrainId, out TerrainDef? t)
                        ? t.Name : "UNKNOWN TERRAIN";
                    bool solid = t is not null && t.IsSolid;

                    string was = "?";
                    bool closesATile = false;
                    if (levelGrid is not null
                        && posX >= 1 && posY >= 1
                        && posX <= levelGrid.Width && posY <= levelGrid.Height)
                    {
                        byte before = levelGrid[posX - 1, posY - 1].TerrainId;
                        TerrainDef beforeDef = db.TerrainById(before);
                        was = $"{before} {beforeDef.Name}";
                        closesATile = solid && !beforeDef.IsSolid;
                    }

                    offenders.Add(
                        $"    {Path.GetFileNameWithoutExtension(file),-24} ({posX,2},{posY,2}) " +
                        $"p{p[1]}  was {was,-22} -> {terrainId,2} {name,-18} " +
                        $"{(solid ? "SOLID" : "open"),-5} " +
                        $"{(closesATile ? "  *** CLOSES AN OPEN TILE ***" : "")}");
                }
            }
        }

        Console.WriteLine($"{filesScanned} trigger files, {actionRecords} action records, " +
                          $"{alterActions} ALTER_TERRAIN_TYPE");
        Console.WriteLine();

        // The layout check: an action type outside the enumerated range would
        // mean the second-to-last byte is not the action type at all.
        int outOfRange = actionTypeHistogram.Keys.Count(k => k is < 1 or > 60);
        Console.WriteLine($"action types seen: {string.Join(", ", actionTypeHistogram.Keys)}");
        Console.WriteLine($"outside the enumerated 1..60 range: {outOfRange}" +
                          (outOfRange == 0 ? "  (layout hypothesis holds)" : "  (LAYOUT WRONG)"));
        Console.WriteLine();

        Console.WriteLine("terrain written by ALTER_TERRAIN_TYPE:");
        foreach ((byte id, int count) in writtenTerrain)
        {
            string name = db.Terrain.TryGetValue(id, out TerrainDef? t) ? t.Name : "UNKNOWN";
            string kind = t is null ? "?" : t.IsSolid ? "SOLID" : "open";
            Console.WriteLine($"  {id,3} {name,-24} {kind,-6} {count,4} times");
        }

        if (offenders.Count > 0 && args.Length > 2 && args[2] == "-v")
        {
            Console.WriteLine();
            Console.WriteLine("every occurrence:");
            foreach (string o in offenders) Console.WriteLine(o);
        }

        return 0;
    }

    internal static int RunTriggerTypes(string[] args)
    {
        IReadOnlyList<KwdTriggerCorpusAudit.Entry> distribution =
            KwdTriggerCorpusAudit.Run(Dk2Install.Require());
        int total = 0;
        foreach (KwdTriggerCorpusAudit.Entry entry in distribution)
        {
            total += entry.Count;
            Console.WriteLine($"tag={entry.Tag} type={entry.Type,3} count={entry.Count,4} " +
                              $"levels={entry.Levels.Count,2}: {string.Join(",", entry.Levels)}");
        }
        Console.WriteLine($"-- {total} TRIGGERS records; {distribution.Count} tag/type combinations");
        return 0;
    }

    internal static int RunOracle(string[] args)
    {
        // Which level makes the best two-sided experiment -- and, as it turns out,
        // that none of them does yet. A recorded capture is a played session, so a
        // column whose chain runs through something only a human does can never
        // agree. This measures the opposite: what a headless run moves on its own.
        // The reading is docs/EXPERIMENTS.md, 2026-09-09.
        int oracleTurns = args.Length == 3
            ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)
            : 1200;
        string oracleInstall = Dk2Install.Require();
        string oracleMaps = Path.Combine(oracleInstall, "Data", "editor", "maps");
        var oracleRows = new List<OracleReach>();

        foreach (string file in Directory.EnumerateFiles(oracleMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[.. ^"Triggers.kld".Length];
            List<TriggerRecord>? recs = GameDatabaseLoader.LoadCampaignTriggers(oracleInstall, level);
            LevelDef? def = GameDatabaseLoader.LoadCampaignLevel(oracleInstall, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(oracleInstall, level);
            List<PlayerDef>? plyrs = GameDatabaseLoader.LoadCampaignPlayers(oracleInstall, level);
            if (recs is null || def is null || things is null || plyrs is null) continue;

            oracleRows.Add(MeasureOracleReach(
                oracleInstall, level, oracleTurns, recs, def, things, plyrs));
        }

        Console.WriteLine($"Every level run headless for {oracleTurns} turns, ranked by what a capture "
            + "of it could actually discriminate.");
        Console.WriteLine("moved  = flag/timer ids this run ever set.   after1 = those that moved past turn 1,");
        Console.WriteLine("         which is the only part a played capture can be compared against.");
        Console.WriteLine("could  = ids a walk reaching every type 1/2/34 gate would set -- an upper bound.");
        Console.WriteLine("         The gap to `moved` is NOT the frontier: level 4 has 24 ids of gap and its");
        Console.WriteLine("         one stall forecloses one record. Most of that gap is script the walk does");
        Console.WriteLine("         reach and answers false, because an idle world has nothing happening.");
        Console.WriteLine("writes = ids the file writes at all.");
        Console.WriteLine("stalls = distinct condition records the run reaches and cannot answer.");
        Console.WriteLine("gates  = records those stalls foreclose -- the subtrees they cut off that nothing");
        Console.WriteLine("         else reaches. THIS is the cost of the frontier; `stalls` only counts the");
        Console.WriteLine("         declines, and the two rank differently (see `reach` for the rest).");
        Console.WriteLine();
        Console.WriteLine($"{"level",-18} {"moved",5} {"after1",6} {"could",5} {"writes",6}  "
            + $"{"stalls",6} {"gates",5}  {"reach",5} {"of",5}  {"creature",8}");
        foreach (OracleReach r in oracleRows
                     .OrderByDescending(r => r.MovedAfterFirstTurn).ThenByDescending(r => r.Moved)
                     .ThenBy(r => r.Level, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{r.Level,-18} {r.Moved,5} {r.MovedAfterFirstTurn,6} "
                + $"{r.Flags + r.Timers,5} {r.AllFlags + r.AllTimers,6}  "
                + $"{r.Stalls,6} {r.Gates,5}  {r.Reached,5} {r.Records,5}  {r.CreatureRoots,8}");

        int usable = oracleRows.Count(r => r.MovedAfterFirstTurn > 0);
        Console.WriteLine();
        Console.WriteLine($"{usable} of {oracleRows.Count} levels move any column past turn 1. "
            + "A capture is worth taking of a level that moves many.");
        return 0;
    }

    internal static int RunParties(string[] args)
    {
        // Which of the world's 24 party slots a shipped HERO_PARTY lands in, and
        // which of its sixteen member slots count as used. Action type 14 indexes
        // that array by the id in its own body, so an executor has to choose
        // between the file's order and the record's own PartyId, and has to
        // decide what an unused member is. This prints the evidence for both
        // (docs/research/trigger-tree.md, "The 24 slots at +0x4030").
        string partyMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int partyFiles = 0, parties = 0, orderDiffers = 0, duplicates = 0, overSlots = 0;
        int withCreature = 0, creatureNoTrigger = 0, triggerNoCreature = 0, maxPartyId = -1;
        foreach (string file in Directory.EnumerateFiles(partyMaps, "*Things.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            partyFiles++;
            var seen = new HashSet<byte>();
            int index = 0;
            foreach (HeroPartyThing party in KwdThings
                         .Parse(KwdReader.ReadFile(file)).OfType<HeroPartyThing>())
            {
                parties++;
                if (party.PartyId != index) orderDiffers++;
                if (!seen.Add(party.PartyId)) duplicates++;
                if (party.PartyId >= 24) overSlots++;
                maxPartyId = Math.Max(maxPartyId, party.PartyId);
                index++;
                foreach (GoodCreatureThing member in party.Members)
                {
                    if (member.CreatureId != 0)
                    {
                        withCreature++;
                        if (member.TriggerId == 0) creatureNoTrigger++;
                    }
                    else if (member.TriggerId != 0)
                    {
                        triggerNoCreature++;
                    }
                }
            }
        }
        Console.WriteLine($"{partyFiles} THINGS files, {parties} HERO_PARTY records, ids 0..{maxPartyId}");
        Console.WriteLine($"  {orderDiffers} whose PartyId differs from their position in the file");
        Console.WriteLine($"  {duplicates} sharing a PartyId with an earlier party in the same file");
        Console.WriteLine($"  {overSlots} with a PartyId past the 24 runtime slots");
        Console.WriteLine($"  {withCreature} members carry a creature id, {creatureNoTrigger} of those no trigger");
        Console.WriteLine($"  {triggerNoCreature} members carry a trigger but no creature id");
        return 0;
    }

    internal static int RunUnreached(string[] args)
    {
        // Where a level's script is when the run never touches it. `kwd oracle`'s
        // `reach` is a STATIC walk descending only through types 1/2/34, so its gap
        // is not "what the run missed" -- this is. One bucket per record, first to
        // claim it keeps it (ADR-0125). The default hour is deliberate: at 600 turns
        // the biggest closed group is level-time gates not yet due (ADR-0126).
        int unreachedTurns = args.Length == 3 ? int.Parse(args[2]) : 14400;
        string reachMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int total = 0, ran = 0, declined = 0, creature = 0, party = 0, orphan = 0, closed = 0;
        var closedByType = new Dictionary<byte, int>();
        var partialByArm = new Dictionary<(int Tag, byte Type, TriggerContext Context),
            HashSet<(string Level, ushort TriggerId)>>();
        Console.WriteLine($"Every level run headless for {unreachedTurns} turns. Records the run never");
        Console.WriteLine("walked, by why -- first bucket that claims a record keeps it:");
        Console.WriteLine("  declined = behind a condition this executor cannot answer (the frontier)");
        Console.WriteLine("  creature = only reachable below a creature root that stayed closed or inactive");
        Console.WriteLine("  party    = only reachable from a party subtree nothing armed (action 14");
        Console.WriteLine("             creates no creatures, Q254)");
        Console.WriteLine("  orphan   = reachable from no root at all -- dead script in the file");
        Console.WriteLine("  closed   = reached by no route above: the walk got there and a condition");
        Console.WriteLine("             answered FALSE. Only a mechanism, not an arm, opens these.");
        Console.WriteLine();
        Console.WriteLine($"{"level",-18} {"records",7} {"ran",6} {"declined",8} {"creature",8} "
            + $"{"party",6} {"orphan",6} {"closed",6}");

        foreach (string file in Directory.EnumerateFiles(reachMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            string reachInstall = Dk2Install.Require();
            LevelMap? rmap = GameDatabaseLoader.LoadCampaignMap(reachInstall, level);
            LevelDef? rdef = GameDatabaseLoader.LoadCampaignLevel(reachInstall, level);
            List<TriggerRecord>? rrecords = GameDatabaseLoader.LoadCampaignTriggers(reachInstall, level);
            List<PlayerDef>? rplayers = GameDatabaseLoader.LoadCampaignPlayers(reachInstall, level);
            List<ThingRecord>? rthings = GameDatabaseLoader.LoadCampaignThings(reachInstall, level);
            if (rmap is null || rdef is null || rrecords is null || rplayers is null || rthings is null)
                continue;

            GameDatabase rdatabase = GameDatabaseLoader.LoadForCampaignLevel(reachInstall, level);
            WorldState rworld = WorldState.FromMap(
                rdatabase, rmap, rplayers.Select(p => p.PlayerId).ToArray());
            ThingProjection.Place(rworld, rdatabase, rthings, rplayers.ToDictionary(p => p.PlayerId, p => p.StartingGold));
            var rtree = new TriggerTree(
                rrecords, rdef.TriggerId,
                rthings.OfType<HeroPartyThing>(),
                rplayers.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
            var rsim = new GameSimulation(rdatabase, rworld, seed: 0, triggers: rtree)
            {
                TrackTriggerReach = true,
            };

            // The union across turns, not one turn's: a record first reached on turn
            // 300 was reached. Same for the declines, whose subtrees only stop being
            // foreclosed if some later turn walks them anyway.
            var walked = new HashSet<ushort>();
            var foreclosed = new HashSet<ushort>();
            var closedBy = new Dictionary<ushort, byte>();
            var closedSets = new Dictionary<ushort, HashSet<ushort>>();
            for (int turn = 1; turn <= unreachedTurns; turn++)
            {
                rsim.Tick();
                if (rsim.LastTriggerRun is not { } run) continue;
                if (run.Reached is not null) walked.UnionWith(run.Reached);
                foreach (BlockedTrigger item in run.Partial)
                {
                    var key = (item.Tag, item.Type, item.Context);
                    if (!partialByArm.TryGetValue(key, out HashSet<(string, ushort)>? records))
                        partialByArm[key] = records = [];
                    records.Add((level, item.TriggerId));
                }
                foreach (TriggerGate gate in TriggerReach.Gated(rtree, run))
                    foreclosed.UnionWith(gate.Foreclosed);
                foreach (TriggerClosure c in TriggerReach.Closed(rtree, run))
                {
                    closedBy[c.TriggerId] = c.Type;
                    if (!closedSets.TryGetValue(c.TriggerId, out HashSet<ushort>? set))
                        closedSets[c.TriggerId] = set = [];
                    set.UnionWith(c.Foreclosed);
                }
            }

            foreclosed.ExceptWith(walked);
            HashSet<ushort> fromCreatures = TriggerReach.Reachable(
                rtree,
                rthings.OfType<KeeperCreatureThing>().Select(c => c.TriggerId)
                    .Concat(rthings.OfType<GoodCreatureThing>().Select(c => c.TriggerId))
                    .Where(id => id != 0));
            HashSet<ushort> fromParties = TriggerReach.Reachable(
                rtree,
                rthings.OfType<HeroPartyThing>()
                    .SelectMany(p => p.Members.Select(m => m.TriggerId).Append(p.TriggerId))
                    .Where(id => id != 0));

            HashSet<ushort> fromAnyRoot = TriggerReach.Reachable(
                rtree,
                new[] { rdef.TriggerId }
                    .Concat(rplayers.Select(p => p.TriggerId))
                    .Concat(rthings.OfType<ActionPointThing>().Select(a => a.TriggerId))
                    .Concat(fromCreatures).Concat(fromParties)
                    .Where(id => id != 0));

            int lDeclined = 0, lCreature = 0, lParty = 0, lOrphan = 0, lClosed = 0;
            foreach (TriggerRecord record in rrecords)
            {
                ushort id = record.TriggerId;
                if (walked.Contains(id)) continue;
                if (foreclosed.Contains(id)) lDeclined++;
                else if (fromCreatures.Contains(id)) lCreature++;
                else if (fromParties.Contains(id)) lParty++;
                else if (!fromAnyRoot.Contains(id)) lOrphan++;
                else lClosed++;
            }

            // Attribute each still-unreached record to the false condition nearest it,
            // by taking the smallest foreclosed set that contains it: a record deep under
            // several false conditions belongs to the one that stopped the walk last.
            foreach (ushort id in rrecords.Select(r => r.TriggerId))
            {
                if (walked.Contains(id) || foreclosed.Contains(id)) continue;
                ushort best = 0;
                int bestSize = int.MaxValue;
                foreach ((ushort gateId, HashSet<ushort> set) in closedSets)
                {
                    if (!set.Contains(id) || set.Count >= bestSize) continue;
                    best = gateId;
                    bestSize = set.Count;
                }
                if (bestSize != int.MaxValue) closedByType[closedBy[best]] =
                    closedByType.GetValueOrDefault(closedBy[best]) + 1;
            }

            total += rrecords.Count;
            ran += walked.Count;
            declined += lDeclined; creature += lCreature; party += lParty;
            orphan += lOrphan; closed += lClosed;
            Console.WriteLine($"{level,-18} {rrecords.Count,7} {walked.Count,6} {lDeclined,8} "
                + $"{lCreature,8} {lParty,6} {lOrphan,6} {lClosed,6}");
        }

        Console.WriteLine();
        Console.WriteLine($"{"TOTAL",-18} {total,7} {ran,6} {declined,8} {creature,8} "
            + $"{party,6} {orphan,6} {closed,6}");

        // And what closes them: the condition type nearest each unreached record that
        // the walk did reach and answered false. This is the list a mechanism opens --
        // no dispatcher arm does, because these arms already work.
        Console.WriteLine();
        Console.WriteLine("closed by condition type (the nearest false condition above each record):");
        foreach ((byte type, int count) in closedByType.OrderByDescending(e => e.Value).Take(20))
            Console.WriteLine($"  type {type,3}: {count,5}");

        Console.WriteLine();
        Console.WriteLine("partial action records reached at least once, ranked by unique record:");
        foreach (((int tag, byte type, TriggerContext context), HashSet<(string, ushort)> records)
                 in partialByArm.OrderByDescending(e => e.Value.Count)
                     .ThenBy(e => e.Key.Type).ThenBy(e => e.Key.Context))
            Console.WriteLine($"  tag {tag} type {type,3} in {context,-8}: {records.Count,5}");
        return 0;
    }

    internal static int RunRoots(string[] args)
    {
        // Every trigger root in the campaign, by the walker that owns it. A level
        // spreads its script over four trees and the world one is often the
        // smallest, so this is what says how much of a level any given executor
        // can reach (docs/research/trigger-walkers.md).
        string rootMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int levels = 0, worldRoots = 0, partyRoots = 0, playerRoots = 0;
        int creatureRoots = 0, apRoots = 0, multiPlayer = 0;
        foreach (string file in Directory.EnumerateFiles(rootMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            LevelDef? def = GameDatabaseLoader.LoadCampaignLevel(Dk2Install.Require(), level);
            List<ThingRecord>? rootThings = GameDatabaseLoader.LoadCampaignThings(Dk2Install.Require(), level);
            List<PlayerDef>? rootPlayers = GameDatabaseLoader.LoadCampaignPlayers(Dk2Install.Require(), level);
            if (def is null || rootThings is null || rootPlayers is null) continue;
            levels++;

            int players = rootPlayers.Count(p => p.TriggerId != 0);
            int parties = rootThings.OfType<HeroPartyThing>().Count(p => p.TriggerId != 0);
            int points = rootThings.OfType<ActionPointThing>().Count(a => a.TriggerId != 0);
            int creatures = rootThings.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0)
                + rootThings.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0);

            if (def.TriggerId != 0) worldRoots++;
            partyRoots += parties;
            playerRoots += players;
            creatureRoots += creatures;
            apRoots += points;

            // Order within a turn is the world's runtime player list, not this
            // file's order, and the two are only distinguishable where a level
            // has more than one player root.
            if (players > 1) multiPlayer++;
        }
        Console.WriteLine($"{levels} levels: {worldRoots} with a world root, "
            + $"{partyRoots} party, {playerRoots} player, {creatureRoots} creature, "
            + $"{apRoots} action point roots");
        Console.WriteLine($"  {multiPlayer} levels have more than one player root "
            + "-- only those can show whether player order matters");
        return 0;
    }

    internal static int RunTriggers(string[] args)
    {
        // One level's trigger records with their links and decoded parameters.
        // `kwd records` prints ids and types; `kwd trigger-types` counts them
        // corpus-wide; neither shows a tree. This does, because to design an
        // observation in the running game you have to know which condition
        // leads to which visible action.
        var byId = new Dictionary<int, TriggerRecord>();
        var order = new List<TriggerRecord>();
        foreach (TriggerRecord record in KwdTriggers.Parse(KwdReader.ReadFile(ResolveEditor(args[2]))))
        {
            order.Add(record);
            byId[record.TriggerId] = record;
        }

        // Optional filter: a tag and type, so "show me every timer condition"
        // is one command rather than a grep over thousands of lines.
        int? wantTag = args.Length >= 4 ? int.Parse(args[3]) : null;
        int? wantType = args.Length >= 5 ? int.Parse(args[4]) : null;

        foreach (TriggerRecord record in order)
        {
            if (wantTag is not null && record.Tag != wantTag) continue;
            if (wantType is not null && record.Type != wantType) continue;

            Console.WriteLine(
                $"tag={record.Tag} id={record.TriggerId,4} type={record.Type,3} "
                + $"next={record.NextTriggerId,4} child={record.ChildTriggerId,4} "
                + $"repeat={record.RepeatCount,3}  "
                + string.Join(" ", record.Parameters.Select(p => $"{p.Key}={p.Value}")));

            // One level of the tree, which is what says what the condition does.
            for (int child = record.ChildTriggerId; child != 0 && byId.ContainsKey(child);)
            {
                TriggerRecord c = byId[child];
                Console.WriteLine(
                    $"    -> tag={c.Tag} id={c.TriggerId,4} type={c.Type,3} "
                    + string.Join(" ", c.Parameters.Select(p => $"{p.Key}={p.Value}")));
                child = c.NextTriggerId == child ? 0 : c.NextTriggerId;
            }
        }
        return 0;
    }

    internal static int RunTriggerParams(string[] args)
    {
        // The distribution of ONE named parameter across every shipped record
        // of one tag and type. `kwd trigger-types` says how many records exist;
        // this says what they carry, which is what settles a unit question:
        // a timer threshold authored in seconds and one authored in ticks are
        // the same field with values that differ by a factor of four.
        if (!int.TryParse(args[2], out int wantTag))
        {
            Console.Error.WriteLine("usage: kwd trigger-params <tag> <type> [parameterName]");
            return 2;
        }

        bool anyType = args[3] == "*";
        int wantType = anyType ? -1 : int.Parse(args[3]);
        string? wantParam = args.Length >= 5 ? args[4] : null;

        string maps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int records = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").Order(StringComparer.OrdinalIgnoreCase))
        {
            foreach (TriggerRecord record in KwdTriggers.Parse(KwdReader.ReadFile(file)))
            {
                if (record.Tag != wantTag) continue;
                if (!anyType && record.Type != wantType) continue;

                records++;
                foreach ((string name, long value) in record.Parameters)
                {
                    if (wantParam is not null && name != wantParam) continue;
                    string key = $"{name}={value}";
                    tally[key] = tally.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
        }

        foreach ((string key, int count) in tally.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {key,-28} {count,5}");
        Console.WriteLine($"-- {records} records of tag {wantTag} type {(anyType ? "*" : $"{wantType}")}");
        return 0;
    }

    /// <summary>
    /// Walks one level's trigger roots the way <c>TriggerExecutor</c> would, descending
    /// only where a headless run could get through, and reports what that reaches.
    /// </summary>
    /// <remarks>
    /// Condition types 1, 2 and 34 are the whole of the passable set: a flag and a timer
    /// are the script's own state and level time is the clock. Every other type either
    /// measures a dungeon somebody is playing or is one the executor declines, and both
    /// stop the walk in exactly the same place the executor stops it.
    /// </remarks>
    static OracleReach MeasureOracleReach(
        string install,
        string level,
        int turns,
        List<TriggerRecord> records,
        LevelDef def,
        List<ThingRecord> things,
        List<PlayerDef> players)
    {
        var conditions = new Dictionary<ushort, TriggerRecord>();
        var actions = new Dictionary<ushort, TriggerRecord>();
        foreach (TriggerRecord record in records)
        {
            if (record.Tag == 213) conditions[record.TriggerId] = record;
            else if (record.Tag == 214) actions[record.TriggerId] = record;
        }

        var seen = new HashSet<(int Tag, ushort Id)>();
        var flags = new HashSet<byte>();
        var timers = new HashSet<byte>();
        var armed = new HashSet<int>();

        void Walk(short id)
        {
            while (id != 0)
            {
                bool isCondition = id > 0;
                ushort raw = unchecked((ushort)id);
                TriggerRecord? record = isCondition
                    ? conditions.GetValueOrDefault(raw)
                    : actions.GetValueOrDefault(raw);

                // A record already seen has had its whole next chain followed.
                if (record is null || !seen.Add((record.Tag, raw))) return;

                bool descend;
                if (isCondition)
                {
                    descend = record.Type is 1 or 2 or 34;
                }
                else
                {
                    descend = record.Type is 7 or 8 or 14;
                    if (record.Type == 7 && record.Parameters.TryGetValue("FlagId", out long flag))
                        flags.Add(unchecked((byte)flag));
                    if (record.Type == 8 && record.Parameters.TryGetValue("TimerId", out long timer))
                        timers.Add(unchecked((byte)timer));
                    if (record.Type == 14 && record.Parameters.TryGetValue("PartyId", out long party))
                        armed.Add(unchecked((byte)party));
                }

                if (descend) Walk(unchecked((short)record.ChildTriggerId));
                id = unchecked((short)record.NextTriggerId);
            }
        }

        if (def.TriggerId != 0) Walk(unchecked((short)def.TriggerId));
        foreach (PlayerDef player in players)
            if (player.TriggerId != 0) Walk(unchecked((short)player.TriggerId));
        foreach (ActionPointThing point in things.OfType<ActionPointThing>())
            if (point.TriggerId != 0) Walk(unchecked((short)point.TriggerId));

        // A party subtree starts disabled and only a type 14 that actually ran arms it,
        // so those roots join the walk as they are reached rather than at the start.
        HeroPartyThing[] parties = things.OfType<HeroPartyThing>().ToArray();
        int before;
        do
        {
            before = seen.Count;
            foreach (HeroPartyThing party in parties)
            {
                if (!armed.Contains(party.PartyId)) continue;
                if (party.TriggerId != 0) Walk(unchecked((short)party.TriggerId));
                foreach (GoodCreatureThing member in party.Members)
                    if (member.CreatureId != 0 && member.TriggerId != 0)
                        Walk(unchecked((short)member.TriggerId));
            }
        }
        while (seen.Count != before);

        var allFlags = new HashSet<byte>();
        var allTimers = new HashSet<byte>();
        foreach (TriggerRecord record in records)
        {
            if (record.Tag != 214) continue;
            if (record.Type == 7 && record.Parameters.TryGetValue("FlagId", out long flag))
                allFlags.Add(unchecked((byte)flag));
            if (record.Type == 8 && record.Parameters.TryGetValue("TimerId", out long timer))
                allTimers.Add(unchecked((byte)timer));
        }

        int creatureRoots = things.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0)
            + things.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0);

        (int moved, int after, int stalls, int gates) =
            RunOracleLevel(install, level, turns, records, def, things, players);

        return new OracleReach(
            level, flags.Count, timers.Count, seen.Count, records.Count,
            allFlags.Count, allTimers.Count, creatureRoots, moved, after, stalls, gates);
    }

    /// <summary>
    /// Runs one level headless and counts the flag and timer ids that ever leave zero,
    /// and how many of those do it after the opening turn.
    /// </summary>
    /// <remarks>Turn 1 is where a level's unconditional opening fires, and a played
    /// capture agrees with us there for free. Everything a comparison can actually learn
    /// from is in the columns that move later.</remarks>
    static (int Moved, int MovedAfterFirstTurn, int Stalls, int Gates) RunOracleLevel(
        string install,
        string level,
        int turns,
        List<TriggerRecord> records,
        LevelDef def,
        List<ThingRecord> things,
        List<PlayerDef> players)
    {
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
        if (map is null) return (0, 0, 0, 0);

        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        WorldState world = WorldState.FromMap(
            database, map, players.Select(p => p.PlayerId).ToArray());
        ThingProjection.Place(world, database, things, players.ToDictionary(p => p.PlayerId, p => p.StartingGold));

        var tree = new TriggerTree(
            records, def.TriggerId,
            things.OfType<HeroPartyThing>(),
            players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        var simulation = new GameSimulation(database, world, seed: 0, triggers: tree)
        {
            TrackTriggerReach = true,
        };

        var firstTurn = new Dictionary<string, int>();
        var stalls = new HashSet<ushort>();
        var gated = new HashSet<ushort>();
        for (int turn = 1; turn <= turns; turn++)
        {
            simulation.Tick();
            foreach (BlockedTrigger b in simulation.LastTriggerRun?.Blocked ?? [])
                if (b.Tag == 213) stalls.Add(b.TriggerId);
            // Union across turns, like `stalls`: a decline on turn 300 forecloses records the
            // turn-1 walk had no reason to want.
            if (simulation.LastTriggerRun is { } run)
                foreach (TriggerGate gate in TriggerReach.Gated(tree, run))
                    gated.UnionWith(gate.Foreclosed);
            TriggerState state = world.Triggers;
            for (byte id = 0; id < TriggerState.FlagCount; id++)
                if (state.Read(id) != 0) firstTurn.TryAdd($"f{id}", turn);
            for (byte id = 0; id < TriggerState.TimerCount; id++)
                if (state.IsStarted(id)) firstTurn.TryAdd($"t{id}", turn);
        }

        return (firstTurn.Count, firstTurn.Values.Count(t => t > 1), stalls.Count, gated.Count);
    }
}

/// <summary>One level's suitability as a two-sided experiment. See `kwd oracle`.</summary>
record OracleReach(
    string Level,
    int Flags,
    int Timers,
    int Reached,
    int Records,
    int AllFlags,
    int AllTimers,
    int CreatureRoots,
    int Moved,
    int MovedAfterFirstTurn,
    int Stalls,
    int Gates);
