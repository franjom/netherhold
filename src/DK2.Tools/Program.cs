using DK2.Tools;

// DK2.Tools — command-line utilities for inspecting original game data.
//
//   dk2tools where            print the resolved DK2 installation path
//   (more subcommands arrive with the WAD/KWD readers)

if (args.Length == 0)
{
    Console.WriteLine("usage: dk2tools <command>");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  where    print the resolved Dungeon Keeper 2 installation path");
    return 1;
}

switch (args[0])
{
    case "where":
        string? dir = Dk2Install.Find();
        if (dir is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }
        Console.WriteLine(dir);
        return 0;

    default:
        Console.Error.WriteLine($"unknown command: {args[0]}");
        return 1;
}
