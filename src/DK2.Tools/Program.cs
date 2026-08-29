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

    case "wad" when args.Length >= 2 && args[1] == "list":
    {
        if (args.Length < 3) return Usage();
        using var wad = WadArchive.Open(ResolveWad(args[2]));
        foreach (WadEntry e in wad.Entries)
            Console.WriteLine($"{e.UncompressedSize,10} {(e.IsCompressed ? "C" : " ")} {e.Name}");
        Console.WriteLine($"-- {wad.Entries.Count} entries");
        return 0;
    }

    case "wad" when args.Length >= 2 && args[1] == "extract":
    {
        if (args.Length < 4) return Usage();
        using var wad = WadArchive.Open(ResolveWad(args[2]));
        string entryName = args[3];
        string outPath = args.Length > 4 ? args[4] : Path.GetFileName(entryName.Replace('\\', '/'));
        byte[] data = wad.Extract(entryName);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"{entryName} -> {outPath} ({data.Length} bytes)");
        return 0;
    }

    default:
        return Usage();
}

// Accepts either a full path or a name like "Meshes.WAD" resolved inside the install's Data dir.
static string ResolveWad(string arg) =>
    File.Exists(arg) ? arg : Path.Combine(Dk2Install.Require(), "Data", arg);

static int Usage()
{
    Console.WriteLine("usage: dk2tools <command>");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  where                              print resolved DK2 install path");
    Console.WriteLine("  wad list <file.WAD>                list archive entries");
    Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");
    return 1;
}
