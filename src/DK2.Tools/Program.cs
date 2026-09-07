// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Convert;
using DK2.Convert.Audio;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;

// DK2.Tools — command-line utilities for inspecting original game data.

if (args.Length == 0)
    return Usage();

switch (args[0])
{
    case "where":
    {
        string? dir = Dk2Install.Find();
        if (dir is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }
        Console.WriteLine(dir);
        return 0;
    }

    case "convert":
    {
        int outOption = Array.IndexOf(args, "--out");
        if (outOption < 0 || outOption + 1 >= args.Length || outOption + 2 != args.Length)
        {
            Console.Error.WriteLine("usage: dk2tools convert --out converted-assets/");
            return 2;
        }

        string output = RequireConvertedOutput(args[outOption + 1]);
        string install = Dk2Install.Require();
        GameDatabase database = GameDatabaseLoader.Load(install);
        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(install, database, output);
        FileAssetProvider provider = FileAssetProvider.Converted(output);
        foreach (AssetConversionResult result in results)
        {
            if (result.Produced && !provider.TryResolve(result.Reference, out _))
                throw new InvalidOperationException(
                    $"Converter reported '{result.Reference.Key}' but FileAssetProvider cannot resolve it.");
            string status = result.Produced ? "PRODUCED" : "MISSING ";
            string destination = result.OutputPath is null
                ? result.Detail
                : $"{Path.GetRelativePath(output, result.OutputPath)} ({result.Detail})";
            Console.WriteLine($"{status} {result.Reference.Key} -> {destination}");
        }
        int produced = results.Count(result => result.Produced);
        Console.WriteLine($"-- produced {produced}, could not produce {results.Count - produced}, " +
                          $"catalogue total {results.Count}");
        return 0;
    }

    case "wad" when args.Length >= 3 && args[1] == "list":
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        foreach (WadEntry e in wad.Entries)
            Console.WriteLine($"{e.UncompressedSize,10} {(e.IsCompressed ? "C" : " ")} {e.Name}");
        Console.WriteLine($"-- {wad.Entries.Count} entries");
        return 0;
    }

    case "wad" when args.Length >= 4 && args[1] == "extract":
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        string entryName = args[3];
        string outPath = args.Length > 4 ? args[4] : Path.GetFileName(entryName.Replace('\\', '/'));
        byte[] data = wad.Extract(entryName);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"{entryName} -> {outPath} ({data.Length} bytes)");
        return 0;
    }

    case "sdt" when args.Length == 3 && args[1] == "list":
    {
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        foreach (SdtEntry entry in sdt.Entries)
        {
            string channels = entry.ChannelCount is int count ? $"{count}ch" : "?ch";
            string rate = entry.PayloadSampleRate is ushort payloadRate
                && payloadRate != entry.SampleRate
                ? $"{entry.SampleRate}Hz(stream {payloadRate}Hz)"
                : $"{entry.SampleRate}Hz";
            Console.WriteLine($"{entry.Index,4} {entry.StoredSize,10} {rate,-23} " +
                              $"{entry.BitsPerSample,2}bit {channels,-3} {entry.Codec,-11} {entry.Name}");
        }
        Console.WriteLine($"-- {sdt.Entries.Count} entries");
        return 0;
    }

    case "sdt" when args.Length is 3 or 5 && args[1] == "extract":
    {
        if (args.Length == 5 && args[3] != "--out")
        {
            Console.Error.WriteLine(
                "usage: dk2tools sdt extract <file.sdt> [--out converted-assets/audio]");
            return 2;
        }
        string output = RequireConvertedOutput(args.Length == 5
            ? args[4] : Path.Combine("converted-assets", "audio"));
        Directory.CreateDirectory(output);
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        int produced = 0;
        foreach (SdtEntry entry in sdt.Entries)
        {
            if (entry.Codec == SdtCodec.Empty)
            {
                Console.WriteLine($"SKIPPED  {entry.Index,4} {entry.Name} (empty placeholder)");
                continue;
            }
            string destination = Path.Combine(output, sdt.OutputFileName(entry));
            byte[] bytes = sdt.Extract(entry);
            File.WriteAllBytes(destination, bytes);
            string format = entry.Codec switch
            {
                SdtCodec.Pcm => "PCM in WAV",
                SdtCodec.MpegLayer1 => "raw MPEG Audio Layer I stream",
                SdtCodec.MpegLayer2 => "raw MPEG Audio Layer II stream",
                SdtCodec.MpegLayer3 => "raw MPEG Audio Layer III stream",
                _ => $"raw undecoded type {entry.Type}",
            };
            Console.WriteLine($"EXTRACTED {entry.Index,4} {entry.Name} -> " +
                              $"{Path.GetRelativePath(output, destination)} ({format}, {bytes.Length} bytes)");
            produced++;
        }
        Console.WriteLine($"-- extracted {produced}, skipped {sdt.Entries.Count - produced}");
        return 0;
    }

    case "str" when args.Length == 3 && args[1] == "list":
    {
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        foreach (Dk2StringEntry entry in table.Entries)
            Console.WriteLine($"{entry.Id,6} {EscapeForOneLine(entry.Value)}");
        Console.WriteLine($"-- {table.Entries.Count} entries, file id {table.FileId}");
        return 0;
    }

    case "str" when args.Length == 4 && args[1] == "get":
    {
        if (!uint.TryParse(args[3], out uint id))
        {
            Console.Error.WriteLine($"invalid string id: '{args[3]}'");
            return 2;
        }
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        if (!table.TryResolve(id, out string value))
        {
            Console.Error.WriteLine($"string id {id} is not in this table");
            return 2;
        }
        Console.WriteLine(value);
        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "chunks":
    {
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            // The stride is the structural check every record layout has to pass:
            // a field list that does not sum to it is wrong. Prints "varies" for
            // the two tagged types, whose records carry their own size instead.
            string stride =
                c.ItemCount <= 0 ? "n/a"
                : KwdTaggedRecords.IsTagged(c.Type) ? "varies (tagged records)"
                : c.Content.Length % c.ItemCount == 0
                    ? (c.Content.Length / c.ItemCount).ToString()
                    : $"NOT A WHOLE NUMBER ({c.Content.Length}/{c.ItemCount})";

            Console.WriteLine(
                $"{c.Type,-16} items={c.ItemCount,-6} content={c.Content.Length,-8} stride={stride}");
        }
        return 0;
    }

    // Q74: is there ANY way a dug-out tile becomes rock again? The owner says no
    // keeper can fill one in, and the terrain table plainly carries a fill-in
    // transition. The remaining candidate is the level script: DK2 has an
    // ALTER_TERRAIN_TYPE trigger action (type 22) that writes a terrain id
    // straight onto a tile. This sweeps every shipped map's TRIGGERS chunk,
    // decodes those actions, and reports which terrain they write - so the
    // question is answered from the install rather than from anybody's word.
    //
    // Layout HYPOTHESIS from OpenKeeper's KwdFile.java (facts, not code): the
    // action type is the second-to-last byte of the payload and the repeat count
    // the last, and an ALTER_TERRAIN_TYPE body is terrainId(u8), playerId(u8),
    // two nulls, posX(u16), posY(u16). Verified here by whether every decoded
    // action type lands inside the enumerated range and every terrain id names a
    // terrain the database has.
    case "kwd" when args.Length >= 2 && args[1] == "portals":
    {
        // Which shipped levels have more than one portal, and who starts with
        // them. Written for Q86: the per-room / keeper-wide tally question needs
        // a level where a keeper can hold two portals at once, and hunting for
        // one by playing is far more expensive than reading 66 grids.
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

    case "kwd" when args.Length >= 2 && args[1] == "alterterrain":
    {
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

    case "kwd" when args.Length >= 3 && args[1] == "tagged":
    {
        // Splits THINGS/TRIGGERS into their self-describing records and reports
        // what tags the file carries. See docs/research/kwd-container.md.
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            if (!KwdTaggedRecords.IsTagged(c.Type)) continue;

            var byTag = new SortedDictionary<int, (int Count, SortedSet<int> Sizes)>();
            foreach (KwdTaggedRecords.TaggedRecord r in KwdTaggedRecords.Split(c))
            {
                if (!byTag.TryGetValue(r.Tag, out (int Count, SortedSet<int> Sizes) e))
                    byTag[r.Tag] = e = (0, []);
                e.Sizes.Add(r.Payload.Length);
                byTag[r.Tag] = (e.Count + 1, e.Sizes);
            }

            Console.WriteLine($"{c.Type} — {c.ItemCount} records, {c.Content.Length} bytes, all accounted for");
            foreach ((int tag, (int count, SortedSet<int> sizes)) in byTag)
            {
                string name = c.Type == KwdRecordType.Things
                    ? KwdTaggedRecords.ThingPayloadSizes.TryGetValue(tag, out var t) ? t.Name : "?"
                    : KwdTaggedRecords.TriggerPayloadSizes.TryGetValue(tag, out var g) ? g.Name : "?";
                Console.WriteLine($"  tag {tag,-4} {name,-16} {count,5} records   payload={string.Join(",", sizes)}");
            }
        }
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "terrain":
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

    case "kwd" when args.Length >= 2 && args[1] == "terrainmana":
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        Console.WriteLine($"{"id",3} {"name",-32} {"manaGain",8} {"maxManaGain",11}");
        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} {t.ManaGain,8} {t.MaxManaGain,11}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "terrainflags":
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} 0x{(uint)t.Flags:X8}  {t.Flags}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "roomflags":
    {
        List<RoomDef> flagged = KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Rooms.kwd")));

        foreach (RoomDef r in flagged.OrderBy(r => r.RoomId))
            Console.WriteLine($"{r.RoomId,3} {r.Name,-32} 0x{(uint)r.Flags:X8}  {r.Flags}");

        Console.WriteLine($"-- {flagged.Count} room types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "rooms":
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
    case "kwd" when args.Length >= 3 && args[1] == "mapbytes":
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

    case "kwd" when args.Length >= 3 && args[1] == "map":
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

    case "kwd" when args.Length >= 3 && args[1] == "creature":
    {
        List<CreatureDef> all = KwdCreatures.Parse(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd")));
        CreatureDef? c = all.FirstOrDefault(x =>
            x.Name.Equals(args[2], StringComparison.OrdinalIgnoreCase));
        if (c is null)
        {
            Console.Error.WriteLine($"no creature named '{args[2]}'");
            return 2;
        }

        Console.WriteLine($"{c.Name} (id {c.CreatureId})  class={c.JobClass} style={c.FightStyle}");
        Console.WriteLine($"  hp={c.Hp} dmg={c.MeleeDamage} pay={c.Pay} fear={c.Fear} threat={c.Threat}");
        Console.WriteLine($"  speed={c.Speed} run={c.RunSpeed} see={c.DistanceCanSee} hear={c.DistanceCanHear}");
        Console.WriteLine($"  hungerRate={c.HungerRate} hungerFill={c.HungerFill} " +
                          $"awake={c.TimeAwake} sleep={c.TimeSleep}");
        Console.WriteLine($"  lairObject={c.LairObjectId}");
        Console.WriteLine($"  hpFromChicken={c.HpFromChicken} " +
                          $"ownLandHeal/s={c.OwnLandHealthIncreasePerSecond} " +
                          $"decompose={c.DecomposeValue}");
        Console.WriteLine($"  meleeRange={c.MeleeRange} recharge={c.MeleeRecharge} attack={c.MeleeAttackType}");
        Console.WriteLine($"  maxGoldHeld={c.MaxGoldHeld} initialGoldHeld={c.InitialGoldHeld}");
        Console.WriteLine($"  exp/lvl={c.ExpForNextLevel} exp/s={c.ExpPerSecond} train={c.ExpPerSecondTraining} " +
                          $"research={c.ResearchPerSecond} manufacture={c.ManufacturePerSecond}");
        Console.WriteLine($"  unhappyThreshold={c.UnhappyThreshold} angerNoLair={c.AngerNoLair} " +
                          $"angerNoFood={c.AngerNoFood} angerNoPay={c.AngerNoPay} angerNoWork={c.AngerNoWork}");
        Console.WriteLine($"  angerSlap={c.AngerSlap} angerInHand={c.AngerInHand}");
        Console.WriteLine($"  happy   : {string.Join(", ", c.HappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  unhappy : {string.Join(", ", c.UnhappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  angry   : {string.Join(", ", c.AngryJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  hates   : {string.Join(", ", c.HateJobs)}");
        Console.WriteLine($"  altJobs : {string.Join(", ", c.AlternativeJobs.Select(j => j.Job))}");
        Console.WriteLine($"  attracts: {string.Join(", ", c.Attractions.Where(a => a.RoomId != 0)
                              .Select(a => $"room{a.RoomId}(size>={a.RoomSize})"))}");
        Console.WriteLine($"  spells  : {string.Join(", ", c.Spells.Select(s => $"#{s.CreatureSpellId}@lvl{s.LevelAvailable}"))}");
        Console.WriteLine($"  resists : {string.Join(", ", c.Resistances.Select(x => $"{x.AttackType}={x.Value}"))}");

        // Raw fixed-point values, for exact transcription into test fixtures.
        // Fp32's denominator is 4096, so the decimal forms above are lossy.
        Console.WriteLine($"  flags   : 0x{(uint)c.Flags:X8} {c.Flags}");
        Console.WriteLine($"  raw     : speed={c.Speed.Raw} run={c.RunSpeed.Raw} shuffle={c.ShuffleSpeed.Raw} " +
                          $"height={c.Height.Raw} see={c.DistanceCanSee.Raw} hear={c.DistanceCanHear.Raw} " +
                          $"meleeRange={c.MeleeRange.Raw} recharge={c.MeleeRecharge.Raw} " +
                          $"guard={c.GuardDuration.Raw} idle={c.IdleDuration.Raw}");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "creatures":
    {
        string? carg = args.Skip(2).FirstOrDefault(a => !a.StartsWith('-'));
        string cpath = carg is not null
            ? ResolveEditor(carg)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");
        List<CreatureDef> creatures = KwdCreatures.Parse(KwdReader.ReadFile(cpath));

        // -s: the three sight/hearing ranges, with a histogram, because a
        // "they are all the same" claim is only worth anything counted.
        if (args.Contains("-s"))
        {
            Console.WriteLine($"{"id",3} {"name",-16} {"see",12} {"hear",12} {"perception",12}");
            foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
            {
                Console.WriteLine(
                    $"{c.CreatureId,3} {c.Name,-16} {c.DistanceCanSee.Raw,12} " +
                    $"{c.DistanceCanHear.Raw,12} {c.PerceptionRange.Raw,12}");
            }

            Console.WriteLine();
            foreach (var g in creatures.GroupBy(c => c.PerceptionRange.Raw).OrderBy(g => g.Key))
            {
                Console.WriteLine(
                    $"perception 0x{g.Key:X} = {g.Key / 4096.0} tiles: {g.Count()} creatures");
            }

            Console.WriteLine($"-- {creatures.Count} creatures");
            return 0;
        }

        // -a: what attracts each creature to a dungeon, which is what a portal
        // reads. Its own column set, because the room ids and minimum sizes
        // have nothing to do with the combat stats above.
        if (args.Contains("-a"))
        {
            Console.WriteLine($"{"id",3} {"name",-16} attractions (roomId size, present)");
            foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
            {
                string slots = string.Join("  ", c.Attractions.Select((a, i) =>
                    $"[{i}] room={a.RoomId} size={a.RoomSize} present={a.Present}"));
                Console.WriteLine($"{c.CreatureId,3} {c.Name,-16} {slots}");
            }
            Console.WriteLine($"-- {creatures.Count} creatures");
            return 0;
        }

        Console.WriteLine($"{"id",3} {"name",-16} {"hp",5} {"dmg",4} {"pay",4} {"fear",5} {"threat",6} " +
                          $"{"speed",6} {"exp/lvl",7} {"class",-8} {"flags"}");
        foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
        {
            string flags = string.Join(",", new[]
            {
                c.Flags.HasFlag(CreatureFlags.IsWorker) ? "worker" : null,
                c.Flags.HasFlag(CreatureFlags.IsEvil) ? "evil" : null,
                c.Flags.HasFlag(CreatureFlags.CanFly) ? "fly" : null,
                c.Flags.HasFlag(CreatureFlags.CanWalkOnWater) ? "water" : null,
                c.Flags.HasFlag(CreatureFlags.CanWalkOnLava) ? "lava" : null,
                c.Flags.HasFlag(CreatureFlags.IsFearless) ? "fearless" : null,
                c.Flags.HasFlag(CreatureFlags.IsUnique) ? "unique" : null,
                c.Flags.HasFlag(CreatureFlags.AvailableViaPortal) ? "portal" : null,
                c.Flags.HasFlag(CreatureFlags.GenerateDeadBody) ? "body" : null,
                c.Flags.HasFlag(CreatureFlags.CanBeResurrected) ? "resurrect" : null,
                c.Flags.HasFlag(CreatureFlags.NeedBodyForFightIdle) ? "needbody" : null,
            }.Where(s => s is not null));

            Console.WriteLine($"{c.CreatureId,3} {c.Name,-16} {c.Hp,5} {c.MeleeDamage,4} {c.Pay,4} " +
                              $"{c.Fear,5} {c.Threat,6} {c.Speed,6} {c.ExpForNextLevel,7} {c.JobClass,-8} {flags}");
        }
        Console.WriteLine($"-- {creatures.Count} creatures");
        return 0;
    }

    // Offset hunting in a record we only partly understand. Give it a creature
    // and a value measured in the original, and it prints every offset whose
    // u8 / u16 / s16 reading holds that value for that creature, with the whole
    // column beside it so the reading can be judged plausible across all of
    // them. This is the technique that found the Create Imp mana cost in
    // KeeperSpells.kwd; a single hit is luck, a whole column that ranks the way
    // the game plays is not. See docs/research/imps-and-mana.md.
    // Raw window onto the CREATURES record, for judging a candidate offset
    // found by creaturescan. Prints the same byte range for every creature so a
    // column can be read down.
    // Generic offset hunt in a record type we do not parse. Give it a file and
    // a number measured in the original, and it prints every offset whose
    // u8/u16/s16/s32 reading holds that number in some record, with the whole
    // column beside it. Optionally anchor on one record index. This is the
    // technique that found the Create Imp mana cost at KeeperSpells 0x11C; a
    // single hit is luck, a column that ranks the way the game plays is not.
    case "kwd" when args.Length >= 4 && args[1] == "recscan":
    {
        string rpath = ResolveEditor(args[2]);
        int target = int.Parse(args[3]);
        int anchor = args.Length > 4 ? int.Parse(args[4]) : -1;

        foreach (KwdChunk chunk in KwdReader.ReadFile(rpath))
        {
            if (chunk.ItemCount == 0 || chunk.Content.Length % chunk.ItemCount != 0) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            if (size < 8) continue;
            Console.WriteLine($"chunk {chunk.Type}: {chunk.ItemCount} records of {size} bytes");

            byte[] c = chunk.Content;
            string Nm(int r)
            {
                int e = Array.IndexOf(c, (byte)0, r * size, Math.Min(32, size));
                string s = System.Text.Encoding.ASCII.GetString(c, r * size, e < 0 ? 0 : e - r * size);
                return s.Length > 1 && s.All(ch => ch >= 32 && ch < 127) ? s : $"[{r}]";
            }
            int hits = 0;
            for (int off = 0; off < size; off++)
            {
                foreach ((string w, int n) in new[] { ("u8", 1), ("u16", 2), ("s16", 2), ("s32", 4) })
                {
                    if (off + n > size) continue;
                    int Read(int rec)
                    {
                        int b = rec * size + off;
                        return w switch
                        {
                            "u8" => c[b],
                            "u16" => c[b] | (c[b + 1] << 8),
                            "s16" => (short)(c[b] | (c[b + 1] << 8)),
                            _ => c[b] | (c[b + 1] << 8) | (c[b + 2] << 16) | (c[b + 3] << 24),
                        };
                    }

                    bool match = anchor >= 0
                        ? Read(anchor) == target
                        : Enumerable.Range(0, chunk.ItemCount).Any(r => Read(r) == target);
                    if (!match) continue;

                    hits++;
                    Console.WriteLine($"0x{off:X4} {w,-3} : " + string.Join(" ",
                        Enumerable.Range(0, chunk.ItemCount).Select(r => $"{Nm(r)}={Read(r)}")));
                }
            }
            Console.WriteLine($"-- {hits} candidate offsets");
        }
        return 0;
    }

    case "kwd" when args.Length >= 4 && args[1] == "creaturehex":
    {
        int start = Convert.ToInt32(args[2], 16);
        int count = int.Parse(args[3]);
        string hpath = args.Length > 4
            ? ResolveEditor(args[4])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        foreach (KwdChunk chunk in KwdReader.ReadFile(hpath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            Console.Write($"{"creature",-20}");
            for (int o = start; o < start + count; o++) Console.Write($" {o:X4}");
            Console.WriteLine();
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end);
                Console.Write($"{name,-20}");
                for (int o = start; o < start + count; o++) Console.Write($" {rec[o],4}");
                Console.WriteLine();
            }
            break;
        }
        return 0;
    }

    // The object-record twin of creaturehex. Added 2026-09-07 while hunting the
    // obstacle radius the binary reads at objDef+0xf8 (Q88): the loader's frame
    // slot named a file offset, and the only way to check a frame-to-record
    // delta is to look at the column and see whether the numbers behave.
    case "kwd" when args.Length >= 4 && args[1] == "objecthex":
    {
        int start = Convert.ToInt32(args[2], 16);
        int count = int.Parse(args[3]);
        int width = args.Length > 4 && args[4] == "-w" ? 2 : 1;
        string opath = Path.Combine(
            Dk2Install.Require(), "Data", "editor", "Objects.kwd");

        foreach (KwdChunk chunk in KwdReader.ReadFile(opath))
        {
            if (chunk.Type != KwdRecordType.Objects) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            Console.Write($"{"object",-28}");
            for (int o = start; o < start + count; o += width) Console.Write($" {o:X4}");
            Console.WriteLine();
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end);
                Console.Write($"{name,-28}");
                for (int o = start; o < start + count; o += width)
                {
                    int v = width == 2
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                            rec.AsSpan(o))
                        : rec[o];
                    Console.Write($" {v,4}");
                }
                Console.WriteLine();
            }
            break;
        }
        return 0;
    }

    case "kwd" when args.Length >= 4 && args[1] == "creaturescan":
    {
        string wanted = args[2];
        if (!int.TryParse(args[3], out int target))
        {
            Console.Error.WriteLine($"'{args[3]}' is not a number");
            return 2;
        }

        string spath = args.Length > 4
            ? ResolveEditor(args[4])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        var records = new List<(string Name, byte[] Bytes)>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(spath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(
                    rec, 0, end < 0 ? 32 : end);
                records.Add((name, rec));
            }
            break;
        }

        int anchor = records.FindIndex(r =>
            r.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (anchor < 0)
        {
            Console.Error.WriteLine($"no creature named '{wanted}' in {records.Count} records");
            return 2;
        }

        int len = records[anchor].Bytes.Length;
        Console.WriteLine($"scanning {records.Count} records of {len} bytes for " +
                          $"{records[anchor].Name} == {target}");

        int hits = 0;
        for (int off = 0; off < len; off++)
        {
            foreach (string width in new[] { "u8", "u16", "s16" })
            {
                if (width != "u8" && off + 1 >= len) continue;
                int Read(byte[] r) => width switch
                {
                    "u8" => r[off],
                    "u16" => r[off] | (r[off + 1] << 8),
                    _ => (short)(r[off] | (r[off + 1] << 8)),
                };

                if (Read(records[anchor].Bytes) != target) continue;
                hits++;
                Console.WriteLine($"0x{off:X4} {width,-3} : " + string.Join(" ",
                    records.Select(r => $"{r.Name}={Read(r.Bytes)}")));
            }
        }
        Console.WriteLine($"-- {hits} candidate offsets");
        return 0;
    }

    // The OBJECTS record (M6): 145 objects of 894 bytes. Only the gameplay
    // tail is parsed; the first 812 bytes are art. -t groups by type flag,
    // which is what identifies a bed, a chicken or a gold pile.
    case "kwd" when args.Length >= 2 && args[1] == "objects":
    {
        string? oarg = args.Skip(2).FirstOrDefault(a => !a.StartsWith('-'));
        string opath = oarg is not null
            ? ResolveEditor(oarg)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Objects.kwd");
        List<ObjectDef> objects = KwdObjects.Parse(KwdReader.ReadFile(opath));

        if (args.Contains("-t"))
        {
            foreach ((string label, Func<ObjectDef, bool> pick) in new (string, Func<ObjectDef, bool>)[]
            {
                ("lair (beds)", o => o.IsLair),
                ("food", o => o.IsFood),
                ("gold", o => o.IsGold),
                ("spell book", o => o.IsSpellBook),
                ("special", o => o.IsSpecial),
                ("crate", o => o.Flags.HasFlag(ObjectFlags.TypeCrate)),
                ("level gem", o => o.Flags.HasFlag(ObjectFlags.TypeLevelGem)),
                ("decays", o => o.Decays),

                // The Hand of Evil's two: what it may lift, and where it may
                // put it down (Q196).
                ("pick up", o => o.Flags.HasFlag(ObjectFlags.CanBePickedUp)),
                ("any land", o => o.Flags.HasFlag(ObjectFlags.CanBeDroppedOnAnyLand)),
                ("slappable", o => o.Flags.HasFlag(ObjectFlags.CanBeSlapped)),
            })
            {
                var hits = objects.Where(pick).OrderBy(o => o.ObjectId).ToList();
                Console.WriteLine($"{label,-12} {hits.Count,3}: " + string.Join(", ",
                    hits.Select(o => $"{o.ObjectId}:{o.Name}")));
                Console.WriteLine();
            }

            // Every flag bit the shipped data actually uses, so a bit nobody
            // has seen set cannot be quietly named.
            uint union = objects.Aggregate(0u, (a, o) => a | (uint)o.Flags);
            Console.WriteLine($"union of all flags: 0x{union:X8}  ({(ObjectFlags)union})");
            return 0;
        }

        // Every parsed field of one record, which is what transcribing a
        // fixture needs: the table below prints the seven columns worth
        // scanning, and a fixture needs all fourteen.
        if (args.Contains("-full"))
        {
            foreach (ObjectDef o in objects.OrderBy(o => o.ObjectId))
            {
                if (args.FirstOrDefault(a => a.StartsWith("-id=")) is { } want
                    && o.ObjectId != int.Parse(want[4..]))
                {
                    continue;
                }

                Console.WriteLine($"{o.ObjectId} {o.Name}");
                Console.WriteLine($"  Flags = 0x{(uint)o.Flags:X8}  ({o.Flags})");
                Console.WriteLine($"  Hp = {o.Hp}, ManaValue = {o.ManaValue}, " +
                                  $"RoomCapacity = {o.RoomCapacity}, StartState = {o.StartState}, " +
                                  $"PickUpPriority = {o.PickUpPriority}");
                Console.WriteLine($"  Width = {o.Width.Raw}, Height = {o.Height.Raw}, " +
                                  $"Mass = {o.Mass.Raw}, Speed = {o.Speed.Raw}");
                Console.WriteLine($"  NameStringId = {o.NameStringId}, " +
                                  $"TooltipStringId = {o.TooltipStringId}");
            }
            return 0;
        }

        Console.WriteLine($"{"id",3} {"name",-26} {"hp",5} {"mana",5} {"cap",4} " +
                          $"{"state",5} {"prio",4} flags");
        foreach (ObjectDef o in objects.OrderBy(o => o.ObjectId))
        {
            Console.WriteLine($"{o.ObjectId,3} {o.Name,-26} {o.Hp,5} {o.ManaValue,5} " +
                              $"{o.RoomCapacity,4} {o.StartState,5} {o.PickUpPriority,4} {o.Flags}");
        }
        Console.WriteLine($"-- {objects.Count} objects, " +
                          $"{objects.Select(o => o.ObjectId).Distinct().Count()} distinct ids");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "vars":
    {
        string? named = args.Skip(2).FirstOrDefault(a => !a.StartsWith("-"));
        string path = named is not null
            ? ResolveEditor(named)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "GlobalVariables.kwd");
        GlobalVariables vars = KwdVariables.Parse(KwdReader.ReadFile(path));

        Console.WriteLine($"# Misc variables ({vars.Misc.Count})");
        foreach (MiscVariable v in vars.Misc.Values.OrderBy(v => (int)v.Id))
        {
            string name = Enum.IsDefined(v.Id) ? v.Id.ToString() : $"Unknown_{(int)v.Id}";
            string extra = v.Unk1 != 0 || v.Unk2 != 0 ? $"   unk1={v.Unk1} unk2={v.Unk2}" : "";
            Console.WriteLine($"{(int)v.Id,5}  {name,-64} {v.Value,10}{extra}");
        }
        Console.WriteLine();
        Console.WriteLine($"# CreaturePool entries: {vars.CreaturePool.Count}");
        Console.WriteLine($"# Availability entries: {vars.Availabilities.Count}");
        Console.WriteLine($"# Sacrifice recipes:    {vars.Sacrifices.Count}");
        Console.WriteLine($"# Creature stat rows:   {vars.CreatureStats.Count}");

        // The per-level stat table (Q119). One row per (stat, level), and the
        // question it answers is what a level actually does to a creature —
        // the owner reports imps get faster as they level, and nothing in the
        // creature record varies by level.
        if (args.Contains("-s"))
        {
            Console.WriteLine();
            foreach (IGrouping<int, CreatureStatEntry> g in vars.CreatureStats
                         .Where(e => !e.FirstPerson)
                         .GroupBy(e => e.StatId)
                         .OrderBy(g => g.Key))
            {
                Console.WriteLine($"  stat {g.Key,3}: " + string.Join(" ",
                    g.OrderBy(e => e.Level).Select(e => $"L{e.Level}={e.Value}")));
            }

            Console.WriteLine();
            Console.WriteLine("first-person rows:");
            foreach (IGrouping<int, CreatureStatEntry> g in vars.CreatureStats
                         .Where(e => e.FirstPerson)
                         .GroupBy(e => e.StatId)
                         .OrderBy(g => g.Key))
            {
                Console.WriteLine($"  stat {g.Key,3}: " + string.Join(" ",
                    g.OrderBy(e => e.Level).Select(e => $"L{e.Level}={e.Value}")));
            }
        }

        // The two lists a level actually carries. Printed on request because the
        // question they answer — does a level's list replace the global one or
        // add to it? — needs them side by side (Q82).
        if (args.Contains("-a"))
        {
            Console.WriteLine();
            Console.WriteLine("CreaturePool: " + string.Join(", ",
                vars.CreaturePool.Select(e => $"creature{e.CreatureId}=+{e.Value} p{e.PlayerId}")));

            Console.WriteLine();
            Console.WriteLine("Availability, by kind:");
            foreach (IGrouping<ushort, AvailabilityEntry> g in
                     vars.Availabilities.GroupBy(a => a.Kind).OrderBy(g => g.Key))
            {
                Console.WriteLine($"  kind {g.Key}: " + string.Join(", ",
                    g.OrderBy(a => a.TypeId).Select(a => $"{a.TypeId}={a.Value}(p{a.PlayerId})")));
            }
        }

        // -s: the temple's sacrifice recipes, resolved to creature names where
        // the ingredient is a creature. Added to check the DK2 wiki's claim
        // that two Black Knights make a Vampire against the shipped table.
        if (args.Contains("-s"))
        {
            List<CreatureDef> sacCreatures = KwdCreatures.Parse(KwdReader.ReadFile(
                Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd")));
            string Name(byte type, byte id) => type == 0 && id == 0
                ? "-"
                : $"t{type}:" + (sacCreatures.FirstOrDefault(c => c.CreatureId == id)?.Name ?? $"#{id}");

            Console.WriteLine();
            Console.WriteLine($"Sacrifice recipes ({vars.Sacrifices.Count}):");
            foreach (SacrificeEntry s in vars.Sacrifices)
            {
                Console.WriteLine(
                    $"  {Name(s.Type1, s.Id1),-22} + {Name(s.Type2, s.Id2),-22} " +
                    $"+ {Name(s.Type3, s.Id3),-22} => rewardType={s.RewardType} " +
                    $"value={s.RewardValue} speech={s.SpeechId}");
            }
        }

        return 0;
    }

    default:
        return Usage();
}

static string ResolveData(string arg) =>
    File.Exists(arg) ? arg : Path.Combine(Dk2Install.Require(), "Data", arg);

static string ResolveText(string arg) =>
    File.Exists(arg)
        ? arg
        : Path.Combine(Dk2Install.Require(), "Data", "Text", "Default", arg);

static string EscapeForOneLine(string value) =>
    value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

static string ResolveEditor(string arg)
{
    if (File.Exists(arg)) return arg;
    string root = Dk2Install.Require();
    string editor = Path.Combine(root, "Data", "editor", arg);
    if (File.Exists(editor)) return editor;
    return Path.Combine(root, "Data", "editor", "maps", arg);
}

static string RequireConvertedOutput(string argument)
{
    string path = Path.GetFullPath(argument);
    string allowed = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "converted-assets"));
    string allowedPrefix = allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        return path;
    throw new ArgumentException(
        $"Conversion output must be converted-assets/ or one of its children, not '{argument}'.");
}

static int Usage()
{
    Console.WriteLine("usage: dk2tools <command>");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  where                                  print resolved DK2 install path");
    Console.WriteLine("  convert --out converted-assets/        convert catalogue assets to GLB and PNG");
    Console.WriteLine("  wad list <file.WAD>                    list archive entries");
    Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");
    Console.WriteLine("  sdt list <file.sdt>                    list sound-bank entries and formats");
    Console.WriteLine("  sdt extract <file.sdt> [--out converted-assets/audio]");
    Console.WriteLine("                                         extract PCM as WAV, compressed audio raw");
    Console.WriteLine("  str list <file.str>                    list decoded string ids and values");
    Console.WriteLine("  str get <file.str> <id>                resolve one table-local string id");
    Console.WriteLine("  kwd chunks <file.kwd|kld>              list chunks of a KWD/KLD file");
    Console.WriteLine("  kwd vars [file.kwd]                    dump the global balance table");
    Console.WriteLine("  kwd alterterrain [-v]                  every ALTER_TERRAIN_TYPE trigger in every shipped");
    Console.WriteLine("                                         map, with what each target tile starts as (Q74)");
    Console.WriteLine("  kwd portals                            every portal in every shipped map, its owner and");
    Console.WriteLine("                                         size, flagging levels with two claimable ones (Q86)");
    Console.WriteLine();
    Console.WriteLine("offset hunting in records we do not parse:");
    Console.WriteLine("  kwd recscan <file.kwd> <value> [rec]   offsets holding a measured value, with the column");
    Console.WriteLine("  kwd creaturescan <name> <value>        the same, anchored on one creature");
    Console.WriteLine("  kwd creaturehex <offsetHex> <count>    raw byte window across every creature");
    Console.WriteLine("  kwd objecthex <offsetHex> <count> [-w]  the same across every object; -w reads u16s");
    return 1;
}
