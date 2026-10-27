// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;
using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Behavior.Tests;

/// <summary>
/// The fuzzer's allowlist (ADR-0552 §4) is explicit and kept true: every command type is classified,
/// and every command the human input files build is one a human may issue. The Unity files are read
/// as text because nothing in <c>src/</c> can reference them.
/// </summary>
public class HumanCommandPolicyTests
{
    [Fact]
    public void EveryCommandType_IsClassified()
    {
        Type[] missing = typeof(PlayerCommand).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(PlayerCommand)) && !t.IsAbstract)
            .Where(t => !HumanCommandPolicy.Classes.ContainsKey(t))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"classify in HumanCommandPolicy: {string.Join(", ", missing.Select(t => t.Name))}");
    }

    [Fact]
    public void EveryCommandTheHumanInputFilesBuild_IsOneAHumanIssues()
    {
        var byName = HumanCommandPolicy.Classes.Keys.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var built = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in HumanCommandPolicy.SourceRoots)
        {
            string dir = Path.Combine(ScenarioCorpus.RepositoryRoot(), root);
            Assert.True(Directory.Exists(dir), $"{root} is listed as a human input root but does not exist");
            foreach (string path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                // Any mention outside a comment line: commands are built as `new X(`, `X.Rect(`, and
                // target-typed `new(` behind a method that returns X.
                string code = string.Join('\n', File.ReadLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                foreach (Match m in Regex.Matches(code, @"\b([A-Z][A-Za-z]+Command)\b"))
                    if (byName.ContainsKey(m.Groups[1].Value)) built.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(built);
        foreach (string name in built)
            Assert.True(HumanCommandPolicy.Classes[byName[name]] is HumanCommandClass.Play or HumanCommandClass.OutsidePlay,
                $"{name} is named by a human input file but classified {HumanCommandPolicy.Classes[byName[name]]}");
        foreach ((Type type, HumanCommandClass c) in HumanCommandPolicy.Classes)
            if (c == HumanCommandClass.Play)
                Assert.True(built.Contains(type.Name), $"{type.Name} is Play but no human input file names it");
    }

    [Fact]
    public void ARoomPlacedTileByTile_IsNotAHumanCommand()
    {
        var player = new PlayerId(3);

        Assert.False(HumanCommandPolicy.AllowsInPlay(BuildRoomCommand.At(player, RoomTypeId.Treasury, new TilePos(1, 1))));
        Assert.True(HumanCommandPolicy.AllowsInPlay(BuildRoomCommand.Rect(player, RoomTypeId.Treasury, 1, 1, 2, 2)));
        Assert.False(HumanCommandPolicy.AllowsInPlay(new RevealMapCommand(player)));
        Assert.False(HumanCommandPolicy.AllowsInPlay(new GrantCreatureLevelCommand(player, 1)));
    }
}
