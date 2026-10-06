// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using static DK2.Tools.Commands.CommandHelpers;

namespace DK2.Tools.Commands;

internal static partial class KwdCommands
{
    internal static int RunPortals(string[] args)
    {
        // Which shipped levels have more than one portal, and who starts with
        // them: Q86 needs a level where one keeper can hold two portals at once.
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");
        GameDatabase db = GameDatabaseLoader.Load(root);

        // Portal terrains from the ROOMS table rather than hardcoded, so a
        // renumbered id cannot make this quietly print nothing.
        var portalTerrain = new Dictionary<byte, string>();
        foreach (RoomDef room in db.Rooms.Values)
        {
            if (room.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase))
                portalTerrain[room.TerrainId] = room.Name;
        }
        if (portalTerrain.Count == 0)
        {
            Console.Error.WriteLine("no room named *Portal* in the ROOMS table.");
            return 1;
        }
        Console.WriteLine("portal terrains: " + string.Join(", ",
            portalTerrain.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        Console.WriteLine();

        int baseGlobal = db[VariableTypeId.EntranceGenerationSpeedSeconds];
        var interesting = new List<string>();

        foreach (string file in Directory.GetFiles(mapsDir, "*Map.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Map.kld".Length];
            LevelMap? grid = MapGrid.Parse(KwdReader.ReadFile(file));
            if (grid is null) continue;

            // Flood fill each portal terrain into 4-connected regions; one
            // region is one portal, whatever its shape.
            var seen = new bool[grid.Width * grid.Height];
            var regions = new List<(string Room, byte Owner, int Tiles, int X, int Y)>();
            var stack = new Stack<int>();

            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start]) continue;
                MapTile t0 = grid.Tiles[start];
                if (!portalTerrain.TryGetValue(t0.TerrainId, out string? name)) continue;

                seen[start] = true;
                stack.Push(start);
                int tiles = 0, sx = 0, sy = 0;
                byte owner = t0.PlayerId;

                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    tiles++;
                    int x = i % grid.Width, y = i / grid.Width;
                    sx += x; sy += y;
                    // A region takes the highest player id it touches, so a
                    // portal half-owned in the file still reports an owner.
                    if (grid.Tiles[i].PlayerId > owner) owner = grid.Tiles[i].PlayerId;

                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (!grid.Contains(nx, ny)) continue;
                        int n = ny * grid.Width + nx;
                        if (seen[n]) continue;
                        if (grid.Tiles[n].TerrainId != t0.TerrainId) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                regions.Add((name, owner, tiles, sx / tiles, sy / tiles));
            }

            if (regions.Count == 0) continue;

            // The level's own base, because level 1 is not the only one allowed
            // to override it even if it is the only one that does.
            // Read the level's own VARIABLES rather than asking the campaign
            // loader, which accepts only campaign level names and would
            // silently hand back the global 25 for the rest — printing "base
            // 25" for levels that plainly override it. Five levels do.
            int levelBase = baseGlobal;
            string varsFile = file[..^"Map.kld".Length] + "Variables.kld";
            if (File.Exists(varsFile))
            {
                GlobalVariables? own = KwdVariables.Parse(KwdReader.ReadFile(varsFile));
                if (own is not null &&
                    own.TryGet(VariableTypeId.EntranceGenerationSpeedSeconds, out int v))
                {
                    levelBase = v;
                }
            }

            // Player 2 is the unowned marker, not 0: level 1's portal — which
            // the tutorial has you claim — is owner 2, and level 1's keeper is
            // player 3. Player 7 is the Heroes.
            const byte Unowned = 2;
            int claimable = regions.Count(r => r.Owner == Unowned && r.Room == "Portal");
            string mark = claimable >= 2 ? " <== TWO CLAIMABLE" : "";
            Console.WriteLine($"{level,-22} base={levelBase,3}s  portals={regions.Count}{mark}");
            foreach ((string room, byte owner, int tiles, int x, int y) in
                     regions.OrderBy(r => r.X).ThenBy(r => r.Y))
            {
                Console.WriteLine(
                    $"    {room,-12} owner={owner} tiles={tiles,2} at ({x},{y})");
            }
            if (claimable >= 2) interesting.Add($"{level} (base {levelBase}s, {claimable} claimable)");
        }

        Console.WriteLine();
        Console.WriteLine("levels with two or more unowned Portals — candidates for the Q86 timing:");
        if (interesting.Count == 0) Console.WriteLine("    none");
        foreach (string s in interesting) Console.WriteLine("    " + s);
        return 0;
    }

    internal static int RunThings(string[] args)
    {
        // The placed-entity dump: unlike `kwd records`, which prints only a
        // record's type and length, this prints the fields the projection into
        // WorldState reads. With a file argument it dumps that level; with none it
        // sweeps every <name>Things.kld in the install and prints the corpus
        // distribution.
        if (args.Length == 2)
        {
            string maps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int files = 0, maxX = 0, maxY = 0, maxW = 0, maxH = 0, outOfRange = 0;
            int rooms = 0, roomsWrongTerrain = 0, roomsWrongOwner = 0;
            foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").Order(StringComparer.OrdinalIgnoreCase))
            {
                files++;

                // The level's own grid, so "in range" is checked against the map
                // it belongs to rather than against the largest map in the game.
                string level = Path.GetFileName(file);
                level = level[..^"Things.kld".Length];
                LevelMap? grid = GameDatabaseLoader.LoadCampaignMap(Dk2Install.Require(), level);
                if (grid is not null) { maxW = Math.Max(maxW, grid.Width); maxH = Math.Max(maxH, grid.Height); }

                foreach (ThingRecord record in KwdThings.Parse(KwdReader.ReadFile(file)))
                {
                    string key = $"{record.Tag} {record.GetType().Name}";
                    totals[key] = totals.TryGetValue(key, out int n) ? n + 1 : 1;

                    foreach ((int x, int y) in ThingTiles(record))
                    {
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        if (grid is not null && (x < 0 || y < 0 || x >= grid.Width || y >= grid.Height))
                            outOfRange++;
                    }

                    // Whether a ROOM record places a room or annotates one the
                    // MAP already holds. If every one of them agrees with the
                    // grid underneath it, projecting it as a placement would
                    // build the room twice.
                    if (record is RoomThing room && grid is not null && grid.Contains(room.X, room.Y))
                    {
                        rooms++;
                        MapTile tile = grid[room.X, room.Y];
                        if (tile.TerrainId != room.RoomType) roomsWrongTerrain++;
                        if (tile.PlayerId != room.PlayerId) roomsWrongOwner++;
                    }
                }
            }
            Console.WriteLine($"{files} THINGS files, {totals.Values.Sum()} top-level records");
            foreach ((string key, int count) in totals.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {key,-32} {count,6}");
            Console.WriteLine(
                $"coordinate range x 0..{maxX}, y 0..{maxY}; largest map {maxW}x{maxH}; "
                + $"{outOfRange} placed things outside their own level's grid");
            Console.WriteLine(
                $"{rooms} ROOM records checked against the grid under them; "
                + $"{roomsWrongTerrain} disagree on terrain id, {roomsWrongOwner} on owner");
            return outOfRange == 0 ? 0 : 1;
        }

        string thingsPath = ResolveEditor(args[2]);
        if (!File.Exists(thingsPath) && !Path.HasExtension(args[2]))
            thingsPath = ResolveEditor(args[2] + "Things.kld");
        if (!File.Exists(thingsPath))
        {
            Console.Error.WriteLine($"THINGS file not found: {thingsPath}");
            return 2;
        }

        // The level's own grid, when the file is named the shipped way, so each
        // line can say what terrain is already under the thing. That is the
        // question a projection has to answer first: whether a placed record
        // duplicates the map or annotates it.
        LevelMap? under = null;
        string stem = Path.GetFileNameWithoutExtension(thingsPath);
        if (stem.EndsWith("Things", StringComparison.OrdinalIgnoreCase))
            under = GameDatabaseLoader.LoadCampaignMap(Dk2Install.Require(), stem[..^"Things".Length]);

        foreach (ThingRecord record in KwdThings.Parse(KwdReader.ReadFile(thingsPath)))
        {
            string line = DescribeThing(record);
            if (under is not null && ThingTiles(record).FirstOrDefault() is var (tx, ty)
                && under.Contains(tx, ty))
                line += $"  [map terrain {under[tx, ty].TerrainId}, owner {under[tx, ty].PlayerId}]";
            Console.WriteLine(line);
        }
        return 0;
    }

    internal static int RunTerrain(string[] args)
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        Console.WriteLine($"{"id",3} {"name",-24} {"health",7} {"maxHp",7} {"gold",5} {"mana",5} " +
                          $"{"dmg",4} {"->maxHp",7} {"->dead",7}  flags");
        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
        {
            string flags = string.Join(",", new[]
            {
                t.IsSolid ? "solid" : null,
                t.IsImpenetrable ? "impenetrable" : null,
                t.IsOwnable ? "ownable" : null,
                t.IsTaggable ? "taggable" : null,
                t.IsRoom ? "room" : null,
                t.IsWater ? "water" : null,
                t.IsLava ? "lava" : null,
            }.Where(s => s is not null));
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-24} {t.StartingHealth,7} {t.MaxHealth,7} " +
                              $"{t.GoldValue,5} {t.ManaGain,5} {t.Damage,4} " +
                              $"{t.MaxHealthTypeTerrainId,7} {t.DestroyedTypeTerrainId,7}  {flags}");
        }
        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    internal static int RunTerrainMana(string[] args)
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        Console.WriteLine($"{"id",3} {"name",-32} {"manaGain",8} {"maxManaGain",11}");
        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} {t.ManaGain,8} {t.MaxManaGain,11}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    internal static int RunTerrainFlags(string[] args)
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} 0x{(uint)t.Flags:X8}  {t.Flags}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    internal static int RunRoomFlags(string[] args)
    {
        List<RoomDef> flagged = KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Rooms.kwd")));

        foreach (RoomDef r in flagged.OrderBy(r => r.RoomId))
            Console.WriteLine($"{r.RoomId,3} {r.Name,-32} 0x{(uint)r.Flags:X8}  {r.Flags}");

        Console.WriteLine($"-- {flagged.Count} room types");
        return 0;
    }

    internal static int RunRooms(string[] args)
    {
        List<RoomDef> rooms = KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Rooms.kwd")));

        Console.WriteLine($"{"id",3} {"name",-24} {"cost",5} {"sell%",5} {"terr",4} {"spawn",5} " +
                          $"{"size",5} {"construction",-20} flags");
        foreach (RoomDef r in rooms.OrderBy(r => r.RoomId))
        {
            string flags = string.Join(",", new[]
            {
                r.IsBuildable ? "buildable" : null,
                r.IsGood ? "good" : null,
                r.HasWalls ? "walls" : null,
            }.Where(s => s is not null));
            Console.WriteLine($"{r.RoomId,3} {r.Name,-24} {r.Cost,5} {r.ReturnPercentage,5} " +
                              $"{r.TerrainId,4} {r.CreatedCreatureId,5} " +
                              $"{r.RecommendedSizeX + "x" + r.RecommendedSizeY,5} " +
                              $"{r.Construction,-20} {flags}");
        }
        Console.WriteLine();
        Console.WriteLine("object ids per room (Q120: which room owns which object):");
        foreach (RoomDef r in rooms.OrderBy(r => r.RoomId))
        {
            string objs = string.Join(",", r.ObjectIds.Where(o => o != 0));
            if (objs.Length > 0) Console.WriteLine($"  {r.RoomId,3} {r.Name,-24} {objs}");
        }
        Console.WriteLine($"-- {rooms.Count} room types");
        return 0;
    }

    // Q64/Q16: the MAP record's four bytes per tile are terrainId, playerId and
    // two we do not use. A sold bridge reverts to the water or lava it spanned
    // (CONFIRMED, owner), so the underlying terrain has to be recorded per tile
    // somewhere, and one of these two bytes is the obvious candidate. This
    // cross-tabulates them against terrain so the question can be settled by
    // looking rather than by guessing.
    internal static int RunMapBytes(string[] args)
    {
        LevelMap? bytesGrid = MapGrid.Parse(KwdReader.ReadFile(ResolveEditor(args[2])));
        if (bytesGrid is null)
        {
            Console.Error.WriteLine("no MAP chunk in that file");
            return 2;
        }

        byte? only = args.Length >= 4 && byte.TryParse(args[3], out byte want) ? want : null;

        // "neighbours" mode: for every tile of the wanted terrain, what is
        // orthogonally next to it, grouped by the two unparsed bytes. This is
        // the test for whether byte 2 records the hazard a bridge spans, since
        // a fully bridged pond leaves no visible water to count.
        if (args.Length >= 5 && args[4] == "neighbours" && byte.TryParse(args[3], out byte wantN))
        {
            var tally = new Dictionary<string, int>();
            for (int y = 0; y < bytesGrid.Height; y++)
            {
                for (int x = 0; x < bytesGrid.Width; x++)
                {
                    MapTile t = bytesGrid[x, y];
                    if (t.TerrainId != wantN) continue;

                    int[] dx = [0, -1, 1, 0];
                    int[] dy = [-1, 0, 0, 1];
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + dx[i], ny = y + dy[i];
                        if (nx < 0 || ny < 0 || nx >= bytesGrid.Width || ny >= bytesGrid.Height) continue;
                        string key = $"flags={t.Flags} neighbour={bytesGrid[nx, ny].TerrainId}";
                        tally[key] = tally.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
            }

            foreach (var kv in tally.OrderBy(k => k.Key))
                Console.WriteLine($"  {kv.Key,-28} {kv.Value,5}");
            return 0;
        }

        Console.WriteLine($"{"terr",4} {"flags",5} {"unk",4} {"count",6}");
        var groups = bytesGrid.Tiles
            .Where(t => only is null || t.TerrainId == only)
            .GroupBy(t => (t.TerrainId, t.Flags, t.Unknown))
            .OrderBy(g => g.Key.TerrainId).ThenBy(g => g.Key.Flags).ThenBy(g => g.Key.Unknown);

        foreach (var g in groups)
        {
            Console.WriteLine(
                $"{g.Key.TerrainId,4} {g.Key.Flags,5} {g.Key.Unknown,4} {g.Count(),6}");
        }
        return 0;
    }

    internal static int RunMap(string[] args)
    {
        List<KwdChunk> mapChunks = KwdReader.ReadFile(ResolveEditor(args[2]));
        LevelMap? grid = MapGrid.Parse(mapChunks);
        if (grid is null)
        {
            Console.Error.WriteLine("no MAP chunk in that file");
            return 2;
        }

        Console.WriteLine($"{grid.Width} x {grid.Height} tiles");
        var byTerrain = grid.Tiles.GroupBy(t => t.TerrainId)
            .OrderByDescending(g => g.Count()).ToList();
        Console.WriteLine("terrain histogram:");
        foreach (var g in byTerrain.Take(12))
            Console.WriteLine($"  terrain {g.Key,3}: {g.Count(),6} tiles");
        var owners = grid.Tiles.GroupBy(t => t.PlayerId).OrderBy(g => g.Key);
        Console.WriteLine("owners: " + string.Join(", ", owners.Select(g => $"p{g.Key}={g.Count()}")));

        // Optional: where a given terrain type actually is, so scenarios can
        // name real coordinates instead of guessing at them.
        if (args.Length >= 4 && byte.TryParse(args[3], out byte wanted))
        {
            var found = new List<string>();
            for (int y = 0; y < grid.Height && found.Count < 24; y++)
                for (int x = 0; x < grid.Width && found.Count < 24; x++)
                    if (grid[x, y].TerrainId == wanted)
                        found.Add($"({x},{y})p{grid[x, y].PlayerId}");

            Console.WriteLine($"terrain {wanted} at: {string.Join(" ", found)}"
                              + (found.Count == 24 ? " ..." : ""));
        }

        return 0;
    }

    /// <summary>
    /// One line per placed THING, showing the fields the world projection reads.
    ///
    /// <para>Coordinates print as bare tile indices because that is what they are.
    /// This was worth checking rather than assuming: the simulation carries
    /// positions in fixed-point subtile units (4096 to the tile), and a THING's X
    /// and Y are 32-bit ints, so the natural guess is that they are the same units.
    /// They are not. Every coordinate in the shipped corpus is below the level's
    /// width and height, and `kwd things` with no argument is what re-checks
    /// it.</para>
    /// </summary>
    static string DescribeThing(ThingRecord record)
    {
        static string Pos(int x, int y) => $"({x,3},{y,3})";

        return record switch
        {
            ObjectThing t =>
                $"OBJECT       {Pos(t.X, t.Y)} objectId={t.ObjectId,3} player={t.PlayerId,2} "
                + $"gold={t.MoneyAmount,6} spell={t.KeeperSpellId,3} trigger={t.TriggerId}",
            TrapThing t =>
                $"TRAP         {Pos(t.X, t.Y)} trapId={t.TrapId,3} player={t.PlayerId,2} shots={t.NumberOfShots}",
            DoorThing t =>
                $"DOOR         {Pos(t.X, t.Y)} doorId={t.DoorId,3} player={t.PlayerId,2} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
            ActionPointThing t =>
                $"ACTION_POINT {Pos(t.StartX, t.StartY)}..{Pos(t.EndX, t.EndY)} id={t.ActionPointId,3} "
                + $"next={t.NextWaypointId,3} wait={t.WaitDelay,5} flags=0x{t.Flags:X4} trigger={t.TriggerId,4} name={t.Name}",
            KeeperCreatureThing t =>
                $"KEEPER_CREAT {Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} player={t.PlayerId,2} "
                + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
            NeutralCreatureThing t =>
                $"NEUTRAL_CREAT{Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} "
                + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
            GoodCreatureThing t =>
                $"GOOD_CREAT   {Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} "
                + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} objective={t.Objective,3} "
                + $"targetAp={t.ObjectiveTargetActionPointId,3} targetPlayer={t.ObjectiveTargetPlayerId,2} trigger={t.TriggerId}",
            HeroPartyThing t =>
                $"HERO_PARTY   partyId={t.PartyId,3} trigger={t.TriggerId,4} name={t.Name}"
                + string.Concat(t.Members
                    .Where(m => m.CreatureId != 0)
                    .Select(m => Environment.NewLine + "    member   " + DescribeThing(m))),
            RoomThing t =>
                $"ROOM         {Pos(t.X, t.Y)} roomType={t.RoomType,3} player={t.PlayerId,2} "
                + $"direction={t.Direction,3} hp={t.InitialHealth,6}",
            EffectGeneratorThing t =>
                $"EFFECT_GEN   {Pos(t.X, t.Y)} generatorId={t.GeneratorId,3} frequency={t.Frequency,3} "
                + $"effects=[{string.Join(",", t.EffectIds)}]",
            DeadBodyThing t =>
                $"DEAD_BODY    {Pos(t.X, t.Y)} creatureId={t.CreatureId,3} player={t.PlayerId,2} gold={t.GoldHeld}",
            CameraThing t =>
                $"CAMERA       {Pos(t.X, t.Y)} z={t.Z,7} cameraId={t.CameraId,3}",
            _ => $"THING tag={record.Tag} {record.GetType().Name}",
        };
    }

    /// <summary>
    /// The tile a placed THING occupies, or nothing for the records that carry no
    /// position. Deliberately independent of the world projection in
    /// <c>DK2.Simulation</c>: this is the auditor, and an auditor that shares the
    /// thing it audits proves nothing.
    ///
    /// <para>HERO_PARTY members are excluded because their X and Y are zero in
    /// every shipped record — a party is placed by the trigger that spawns it at an
    /// action point, not by coordinates in the file.</para>
    /// </summary>
    static IEnumerable<(int X, int Y)> ThingTiles(ThingRecord record)
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
            case CameraThing: break;
            case HeroPartyThing: break;
        }
    }
}
