// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

// Test levels for original-game experiments: see tools/levelgen/README.md.
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.LevelGen;

string install = Environment.GetEnvironmentVariable("DK2_DIR") ?? @"E:\GogGames\Dungeon Keeper 2";
string maps = LevelBuilder.MapsDir(install);

switch (args.FirstOrDefault())
{
    case "list":
        foreach (Recipe r in Recipes.All) Console.WriteLine($"{r.Name,-22} from {r.Base}: {r.Purpose}");
        return 0;

    case "survey":
        foreach (string f in Directory.GetFiles(maps, "*.kwd").OrderBy(x => x))
        {
            string name = Path.GetFileNameWithoutExtension(f);
            try
            {
                var b = new LevelBuilder(install, name);
                var things = b.Things;
                Console.WriteLine($"{name,-22} {b.Map.Width}x{b.Map.Height} things={things.Count} traps={things.OfType<TrapThing>().Count()} "
                    + $"heroes={things.OfType<GoodCreatureThing>().Count()} overrides={b.Variables.OfType<MiscVariable>().Count()}");
            }
            catch (Exception e) { Console.WriteLine($"{name,-22} not loadable: {e.Message}"); }
        }
        return 0;

    case "show" when args.Length == 2:
    {
        var b = new LevelBuilder(install, args[1]);
        Console.Write(LevelBuilder.Render(b.Map, b.Things));
        foreach (object t in b.Things) Console.WriteLine(t.ToString()!.Replace("System.ReadOnlyMemory<Byte>", "bytes"));
        foreach (object v in b.Variables) Console.WriteLine(v);
        return 0;
    }

    case "build" when args.Length == 3:
    {
        Recipe recipe = Recipes.All.SingleOrDefault(r => r.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"no recipe {args[1]}; see 'list'");
        string stage = Path.GetFullPath(args[2]);
        var b = new LevelBuilder(install, recipe.Base);
        recipe.Build(b);
        string written = b.SaveAs(recipe.Name, stage);
        LevelBuilder.Verify(install, stage, recipe.Name);
        Console.WriteLine($"written to {written}; install by copying its files into {maps} (new names only)");
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: levelgen list | survey | show <level> | build <recipe> <stage-dir>");
        return 2;
}
