using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Wad;
using DK2.Tools;

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

    case "kwd" when args.Length >= 3 && args[1] == "chunks":
    {
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
            Console.WriteLine($"{c.Type,-16} items={c.ItemCount,-6} content={c.Content.Length} bytes");
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
        Console.WriteLine($"  meleeRange={c.MeleeRange} recharge={c.MeleeRecharge} attack={c.MeleeAttackType}");
        Console.WriteLine($"  exp/lvl={c.ExpForNextLevel} exp/s={c.ExpPerSecond} train={c.ExpPerSecondTraining} " +
                          $"research={c.ResearchPerSecond} manufacture={c.ManufacturePerSecond}");
        Console.WriteLine($"  unhappyThreshold={c.UnhappyThreshold} angerNoLair={c.AngerNoLair} " +
                          $"angerNoFood={c.AngerNoFood} angerNoPay={c.AngerNoPay} angerNoWork={c.AngerNoWork}");
        Console.WriteLine($"  happy   : {string.Join(", ", c.HappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  unhappy : {string.Join(", ", c.UnhappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  angry   : {string.Join(", ", c.AngryJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  hates   : {string.Join(", ", c.HateJobs)}");
        Console.WriteLine($"  altJobs : {string.Join(", ", c.AlternativeJobs.Select(j => j.Job))}");
        Console.WriteLine($"  attracts: {string.Join(", ", c.Attractions.Where(a => a.RoomId != 0)
                              .Select(a => $"room{a.RoomId}(size>={a.RoomSize})"))}");
        Console.WriteLine($"  spells  : {string.Join(", ", c.Spells.Select(s => $"#{s.CreatureSpellId}@lvl{s.LevelAvailable}"))}");
        Console.WriteLine($"  resists : {string.Join(", ", c.Resistances.Select(x => $"{x.AttackType}={x.Value}"))}");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "creatures":
    {
        string cpath = args.Length > 2
            ? ResolveEditor(args[2])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");
        List<CreatureDef> creatures = KwdCreatures.Parse(KwdReader.ReadFile(cpath));

        Console.WriteLine($"{"id",3} {"name",-16} {"hp",5} {"dmg",4} {"pay",4} {"fear",5} {"threat",6} " +
                          $"{"speed",6} {"exp/lvl",7} {"class",-8} {"flags"}");
        foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
        {
            string flags = string.Join(",", new[]
            {
                c.Flags.HasFlag(CreatureFlags.IsWorker) ? "worker" : null,
                c.Flags.HasFlag(CreatureFlags.IsEvil) ? "evil" : null,
                c.Flags.HasFlag(CreatureFlags.CanFly) ? "fly" : null,
                c.Flags.HasFlag(CreatureFlags.IsFearless) ? "fearless" : null,
                c.Flags.HasFlag(CreatureFlags.IsUnique) ? "unique" : null,
            }.Where(s => s is not null));

            Console.WriteLine($"{c.CreatureId,3} {c.Name,-16} {c.Hp,5} {c.MeleeDamage,4} {c.Pay,4} " +
                              $"{c.Fear,5} {c.Threat,6} {c.Speed,6} {c.ExpForNextLevel,7} {c.JobClass,-8} {flags}");
        }
        Console.WriteLine($"-- {creatures.Count} creatures");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "vars":
    {
        string path = args.Length > 2
            ? ResolveEditor(args[2])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "GlobalVariables.kwd");
        KwdVariables vars = KwdVariables.Parse(KwdReader.ReadFile(path));

        Console.WriteLine($"# Misc variables ({vars.Misc.Count})");
        foreach (MiscVariable v in vars.Misc.Values.OrderBy(v => (int)v.Id))
        {
            string name = Enum.IsDefined(v.Id) ? v.Id.ToString() : $"Unknown_{(int)v.Id}";
            Console.WriteLine($"{(int)v.Id,5}  {name,-64} {v.Value,10}");
        }
        Console.WriteLine();
        Console.WriteLine($"# CreaturePool entries: {vars.CreaturePool.Count}");
        Console.WriteLine($"# Availability entries: {vars.Availabilities.Count}");
        Console.WriteLine($"# Sacrifice recipes:    {vars.Sacrifices.Count}");
        Console.WriteLine($"# Creature stat rows:   {vars.CreatureStats.Count}");
        return 0;
    }

    default:
        return Usage();
}

static string ResolveData(string arg) =>
    File.Exists(arg) ? arg : Path.Combine(Dk2Install.Require(), "Data", arg);

static string ResolveEditor(string arg)
{
    if (File.Exists(arg)) return arg;
    string root = Dk2Install.Require();
    string editor = Path.Combine(root, "Data", "editor", arg);
    if (File.Exists(editor)) return editor;
    return Path.Combine(root, "Data", "editor", "maps", arg);
}

static int Usage()
{
    Console.WriteLine("usage: dk2tools <command>");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  where                                  print resolved DK2 install path");
    Console.WriteLine("  wad list <file.WAD>                    list archive entries");
    Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");
    Console.WriteLine("  kwd chunks <file.kwd|kld>              list chunks of a KWD/KLD file");
    Console.WriteLine("  kwd vars [file.kwd]                    dump the global balance table");
    return 1;
}
