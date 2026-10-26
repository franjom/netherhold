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
    internal static int RunCreature(string[] args)
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

    // The Library's rates, and what the loader's divide by variable 19 does to
    // each of them. Written for the research accrual (ADR-0090): the tabled
    // figure is not what the engine adds, because ResearchPerSecond is one of
    // the ten record fields divided at load time.
    internal static int RunResearch(string[] args)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2Install.Require());

        // This tool has no dependency on DK2.Simulation, deliberately - it is a
        // data dump, not a second implementation. So the loader's divide is
        // inlined here: integer divide by variable 19's four, floored at one
        // for any non-zero file value. It mirrors
        // SimulationTiming.LoaderRatePerTick, which is the authority.
        const int TicksPerGameSecond = 4;
        static int PerTick(int tabled) =>
            tabled == 0 ? 0 : Math.Max(tabled / TicksPerGameSecond, 1);

        Console.WriteLine(" id name                 tabled  perTick  perSecond  lost  lvl10");
        int researchers = 0;
        foreach (CreatureDef c in db.Creatures.Values.OrderBy(c => c.CreatureId))
        {
            if (c.ResearchPerSecond == 0) continue;
            researchers++;
            int perTick = PerTick(c.ResearchPerSecond);
            int perSecond = perTick * TicksPerGameSecond;
            int atTen = perTick
                * db.StatPercent(CreatureStatId.ResearchPerSecond, 10) / 100
                * TicksPerGameSecond;
            Console.WriteLine(
                $"{c.CreatureId,3} {c.Name,-20} {c.ResearchPerSecond,6} {perTick,8} "
                + $"{perSecond,10} {c.ResearchPerSecond - perSecond,5} {atTen,6}");
        }

        Console.WriteLine();
        Console.WriteLine($"{researchers} creatures can research.");
        Console.WriteLine("Level curve for the Research row (stat 17):");
        for (int level = 1; level <= 10; level++)
        {
            Console.Write(
                $"   L{level}={db.StatPercent(CreatureStatId.ResearchPerSecond, level)}%");
        }
        Console.WriteLine();
        return 0;
    }

    internal static int RunCreatures(string[] args)
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

    // Generic offset hunt in a record type we do not parse: give it a file and a
    // number measured in the original and it prints every offset whose
    // u8/u16/s16/s32 reading holds that number in some record, with the whole
    // column beside it; optionally anchor on one record index. Judge a candidate
    // on the column, not on one hit — that is how the Create Imp mana cost fell
    // out at KeeperSpells 0x11C (docs/research/imps-and-mana.md).
    internal static int RunRecScan(string[] args)
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

    // Raw window onto the CREATURES record, for judging a candidate offset found
    // by creaturescan. Prints the same byte range for every creature so a column
    // can be read down.
    internal static int RunCreatureHex(string[] args)
    {
        int start = System.Convert.ToInt32(args[2], 16);
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

    // The object-record twin of creaturehex. Written for the obstacle radius the
    // binary reads at objDef+0xf8 (Q88): the loader's frame slot names a file
    // offset, and a frame-to-record delta can only be checked by reading the
    // column and seeing whether the numbers behave.
    internal static int RunObjectHex(string[] args)
    {
        int start = System.Convert.ToInt32(args[2], 16);
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

    // Offset hunt inside the CREATURES record: give it a creature and a value
    // measured in the original and it prints every offset whose u8/u16/s16
    // reading holds that value for that creature, with the whole column beside
    // it. Judge the column, not one hit (docs/research/imps-and-mana.md).
    internal static int RunCreatureScan(string[] args)
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

    // Which fields of the record are the same in every record? A constant field
    // cannot discriminate between records, so a rule this project keys on one is
    // either dead code or a rule we invented (Q213). The balance-table equivalent
    // of this sweep found ten live mechanisms shipped switched off (ADR-0085); the
    // creature and object records had never been swept at all.
    internal static int RunFieldSweep(string[] args)
    {
        string which = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : "creatures";
        string root = Path.Combine(Dk2Install.Require(), "Data", "editor");

        var rows = new List<(string Name, object? Value)>();
        var records = new List<(string Label, object Record)>();

        switch (which.ToLowerInvariant())
        {
            case "creatures":
                foreach (CreatureDef c in KwdCreatures.Parse(
                             KwdReader.ReadFile(Path.Combine(root, "Creatures.kwd"))))
                {
                    records.Add((c.Name, c));
                }

                break;

            case "objects":
                foreach (ObjectDef o in KwdObjects.Parse(
                             KwdReader.ReadFile(Path.Combine(root, "Objects.kwd"))))
                {
                    records.Add((o.Name, o));
                }

                break;

            default:
                Console.Error.WriteLine("fieldsweep: expected 'creatures' or 'objects'");
                return 2;
        }

        if (records.Count == 0)
        {
            Console.Error.WriteLine("fieldsweep: no records parsed");
            return 2;
        }

        Console.WriteLine($"{which}: {records.Count} records");
        Console.WriteLine();

        System.Reflection.PropertyInfo[] props = records[0].Record.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public
                           | System.Reflection.BindingFlags.Instance);

        var alwaysZero = new List<string>();
        var constant = new List<(string Field, string Value)>();
        var varying = new List<(string Field, int Distinct)>();

        foreach (System.Reflection.PropertyInfo p in props)
        {
            // Collections say nothing useful to a distinct-value count; the
            // per-slot tables have their own sweeps.
            if (p.PropertyType != typeof(string)
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType))
            {
                continue;
            }

            var seen = new List<string>();
            foreach ((string _, object rec) in records)
            {
                string v = p.GetValue(rec)?.ToString() ?? "<null>";
                if (!seen.Contains(v)) seen.Add(v);
            }

            if (seen.Count == 1)
            {
                string only = seen[0];
                bool zeroish = only is "0" or "0.00" or "False" or "None" or "";
                if (zeroish) alwaysZero.Add(p.Name);
                else constant.Add((p.Name, only));
            }
            else
            {
                varying.Add((p.Name, seen.Count));
            }
        }

        Console.WriteLine($"ZERO FOR EVERY RECORD ({alwaysZero.Count}) "
                          + "- carries no information in v1.70:");
        foreach (string f in alwaysZero) Console.WriteLine($"   {f}");
        Console.WriteLine();

        Console.WriteLine($"THE SAME NON-ZERO VALUE FOR EVERY RECORD ({constant.Count}) "
                          + "- cannot discriminate:");
        foreach ((string f, string v) in constant) Console.WriteLine($"   {f,-38} = {v}");
        Console.WriteLine();

        Console.WriteLine($"VARIES ({varying.Count}):");
        foreach ((string f, int n) in varying.OrderBy(x => x.Distinct))
        {
            Console.WriteLine($"   {f,-38} {n} distinct");
        }

        return 0;
    }

    // Which (offset, bit) in the CREATURES record selects exactly this set of
    // creatures? The flag twin of creaturescan. The loader does not preserve file
    // offsets into the runtime record, so a runtime offset (the hero portal flag
    // is read as `byte ptr [def + 0x68a] & 8`, DKII.EXE) says nothing about where
    // the bit lives in the file — search for the roster the flag must select, not
    // for the offset. Reasoning from the layout gave a wrong answer once.
    internal static int RunCreatureBits(string[] args)
    {
        string[] names = args[2].Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string bpath = args.Length > 3
            ? ResolveEditor(args[3])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        var recs = new List<(string Name, byte[] Bytes)>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(bpath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                recs.Add((
                    System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end),
                    rec));
            }
            break;
        }

        var target = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string n in names)
        {
            if (!recs.Any(r => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"no creature named '{n}' in {recs.Count} records");
                return 2;
            }
            target.Add(n);
        }

        int blen = recs[0].Bytes.Length;
        Console.WriteLine($"{recs.Count} records of {blen} bytes; " +
                          $"target set has {target.Count} creatures");

        int pairs = 0;
        for (int off = 0; off < blen; off++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                List<string> set = recs
                    .Where(r => (r.Bytes[off] & (1 << bit)) != 0)
                    .Select(r => r.Name)
                    .ToList();

                if (set.Count == 0) continue;

                int inTarget = set.Count(target.Contains);
                int wrong = (target.Count - inTarget) + (set.Count - inTarget);

                // Within two of the target, so a near miss is visible rather
                // than silently filtered — a flag that is the right idea with
                // one creature added or dropped is the interesting case.
                if (wrong > 2) continue;

                pairs++;
                Console.WriteLine(
                    $"0x{off:X4} bit{bit} {(wrong == 0 ? "EXACT" : $"off-by-{wrong}"),-9} " +
                    $"n={set.Count} : {string.Join(" ", set)}");
            }
        }

        Console.WriteLine($"-- {pairs} offset/bit pairs within 2 of the target");
        return 0;
    }

    // The OBJECTS record (M6): 145 objects of 894 bytes. Only the gameplay
    // tail is parsed; the first 812 bytes are art. -t groups by type flag,
    // which is what identifies a bed, a chicken or a gold pile.
    internal static int RunObjects(string[] args)
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

    // Q84/Q85: sweep every VARIABLES record in the corpus, not just the level
    // that happens to be open. Reports the (unk1, unk2) pair distribution, and
    // for each id how many distinct values it takes across the 66 level files -
    // which is the "a sweep of keys is not a sweep of values" check.
    internal static int RunVarSweep(string[] args)
    {
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");

        var perId = new Dictionary<int, List<(string Level, int Value, int U1, int U2)>>();
        var pairs = new Dictionary<(int, int), int>();
        int files = 0;

        foreach (string file in Directory.GetFiles(mapsDir, "*Variables.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Variables.kld".Length];
            GlobalVariables v;
            try { v = KwdVariables.Parse(KwdReader.ReadFile(file)); }
            catch (Exception e) { Console.Error.WriteLine($"{level}: {e.Message}"); continue; }
            files++;
            foreach (MiscVariable m in v.Misc.Values)
            {
                int id = (int)m.Id;
                if (!perId.TryGetValue(id, out List<(string, int, int, int)>? list))
                {
                    list = [];
                    perId[id] = list;
                }
                list.Add((level, m.Value, m.Unk1, m.Unk2));
                var key = (m.Unk1, m.Unk2);
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
            }
        }

        Console.WriteLine($"{files} level VARIABLES files, {perId.Count} distinct ids");
        Console.WriteLine();
        Console.WriteLine("(unk1, unk2) pairs, by how many records carry them:");
        foreach (((int u1, int u2), int n) in pairs.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"   unk1={u1,-6} unk2={u2,-6} {n} records");
        Console.WriteLine();

        Console.WriteLine("ids whose VALUE differs between levels (the key/value trap):");
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            var vals = list.Select(e => e.Value).Distinct().OrderBy(x => x).ToList();
            if (vals.Count <= 1) continue;
            string name = Enum.IsDefined(typeof(VariableTypeId), id)
                ? ((VariableTypeId)id).ToString() : $"Unknown_{id}";
            Console.WriteLine($"   {id,4} {name,-52} {vals.Count} values: "
                              + string.Join(", ", vals.Take(8))
                              + (vals.Count > 8 ? " ..." : "")
                              + $"   in {list.Count} levels");
        }
        Console.WriteLine();

        Console.WriteLine("ids present in EVERY level file with one value (constants):");
        int constants = 0;
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            if (list.Count == files && list.Select(e => e.Value).Distinct().Count() == 1)
                constants++;
        }
        Console.WriteLine($"   {constants}");
        Console.WriteLine();

        // Q576: id 66 is an alliance record (0x0051DC90), several a level. Group the
        // levels by their pair list so the exceptions stand out.
        Console.WriteLine("id 66 alliance pairs, levels grouped by identical lists:");
        var byPairs = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(mapsDir, "*Variables.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Variables.kld".Length];
            GlobalVariables v;
            try { v = KwdVariables.Parse(KwdReader.ReadFile(file)); }
            catch { continue; }
            if (v.Alliances.Count == 0) continue;
            string key = string.Join(" ", v.Alliances.Select(a => $"({a.PlayerId},{a.OtherPlayerId})"));
            if (!byPairs.TryGetValue(key, out List<string>? levels)) byPairs[key] = levels = [];
            levels.Add(level);
        }
        foreach ((string pairList, List<string> levels) in byPairs)
            Console.WriteLine($"   {levels.Count,2} levels: {pairList}   e.g. {string.Join(" ", levels.Take(4))}");
        Console.WriteLine();

        Console.WriteLine("ids present in only SOME level files:");
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            if (list.Count == files) continue;
            string name = Enum.IsDefined(typeof(VariableTypeId), id)
                ? ((VariableTypeId)id).ToString() : $"Unknown_{id}";
            var shown = list.Take(6).Select(e => $"{e.Level}={e.Value}");
            Console.WriteLine($"   {id,4} {name,-46} in {list.Count,2}/{files}: "
                              + string.Join(" ", shown) + (list.Count > 6 ? " ..." : ""));
            Console.WriteLine($"        unk1/unk2: " + string.Join(" ",
                list.Select(e => $"({e.U1},{e.U2})").Distinct()));
        }

        return 0;
    }

    internal static int RunVars(string[] args)
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
}
