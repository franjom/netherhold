// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text;
using DK2.Data.Model;
using DK2.Harness;

namespace DK2.Behavior.Tests;

/// <summary>The build receipt a run claims acceptance on (campaign player plan, task 7): the running
/// harness and the source tree must be the receipt's, and the scenario a shipped level's own start.</summary>
public class BuildReceiptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk2-receipt-{Guid.NewGuid():N}");
    private string Repo => Path.Combine(_root, "repo");
    private string Harness => Path.Combine(Repo, "src", "DK2.Harness", "bin");
    private string Receipt => Path.Combine(_root, "receipt");

    public BuildReceiptTests()
    {
        Write(Path.Combine(Repo, "DK2.slnx"), "<Solution />");
        Write(Path.Combine(Repo, "src", "DK2.Harness", "Program.cs"), "class P {}");
        Write(Path.Combine(Repo, "tests", "T.cs"), "class T {}");
        Write(Path.Combine(Harness, "DK2.Harness.dll"), "harness");
        Write(Path.Combine(Harness, "DK2.Simulation.dll"), "simulation");
        Write(Path.Combine(Harness, "YamlDotNet.dll"), "yaml");
        Directory.CreateDirectory(Receipt);
        File.WriteAllText(Path.Combine(Receipt, BuildReceipt.HeadFile), "abc123\n");
        File.WriteAllText(Path.Combine(Receipt, BuildReceipt.OutputFile),
            Line(Harness, "DK2.Harness.dll", binary: true) + Line(Harness, "DK2.Simulation.dll") + Line(Harness, "YamlDotNet.dll"));
        File.WriteAllText(Path.Combine(Receipt, BuildReceipt.SourceFile),
            Line(Repo, "DK2.slnx") + Line(Repo, "src/DK2.Harness/Program.cs") + Line(Repo, "tests/T.cs"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Line(string root, string name, bool binary = false) =>
        $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name))))} {(binary ? '*' : ' ')}{name}\n";

    private BuildReceipt.Verification Verify() => BuildReceipt.Verify(Receipt, Harness, Repo);

    [Fact]
    public void TheReceiptsOwnBuildAndTreeMatch()
    {
        BuildReceipt.Verification verification = Verify();
        Assert.True(verification.Matches, string.Join("; ", verification.Problems));
        Assert.Equal("abc123", verification.Head);
        Assert.Equal(Repo, BuildReceipt.RepositoryRootAbove(Harness));
    }

    [Fact]
    public void AChangedOrUnlistedAssemblyIsNotTheReceiptsBuild()
    {
        File.WriteAllText(Path.Combine(Harness, "DK2.Simulation.dll"), "rebuilt");
        File.WriteAllText(Path.Combine(Harness, "DK2.Extra.dll"), "extra");
        IReadOnlyList<string> problems = Verify().Problems;
        Assert.Contains("output DK2.Simulation.dll differs from the receipt", problems);
        Assert.Contains("output DK2.Extra.dll is beside the harness but not in the receipt", problems);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void AnUnlistedProbingFileIsNotTheReceiptsBuildButSymbolsAre()
    {
        File.WriteAllText(Path.Combine(Harness, "DK2.Harness.deps.json"), "{}");
        File.WriteAllText(Path.Combine(Harness, "DK2.Harness.pdb"), "symbols");
        Assert.Equal(["output DK2.Harness.deps.json is beside the harness but not in the receipt"], Verify().Problems);
    }

    [Fact]
    public void AChangedSourceOrAnUnlistedBuildInputIsNotTheReceiptsTree()
    {
        File.WriteAllText(Path.Combine(Repo, "tests", "T.cs"), "class T { int x; }");
        Write(Path.Combine(Repo, "src", "DK2.Harness", "New.cs"), "class N {}");
        Write(Path.Combine(Repo, "src", "DK2.Harness", "obj", "Generated.cs"), "class G {}");   // the project's build output
        Write(Path.Combine(Repo, "src", "DK2.Harness", "Deep", "obj", "Compiled.cs"), "class C {}");   // still compiled
        Write(Path.Combine(Repo, "src", "DK2.Harness", "README.md"), "notes");                  // not a build input
        IReadOnlyList<string> problems = Verify().Problems;
        Assert.Contains("source tests/T.cs differs from the receipt", problems);
        Assert.Contains("source src/DK2.Harness/New.cs is a build input the receipt does not list", problems);
        Assert.Contains("source src/DK2.Harness/Deep/obj/Compiled.cs is a build input the receipt does not list", problems);
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void AReceiptOfAnUncommittedTreeSaysSo()
    {
        Assert.False(Verify().Dirty);
        File.WriteAllText(Path.Combine(Receipt, BuildReceipt.StatusFile), " M src/DK2.Harness/Program.cs\n");
        File.WriteAllText(Path.Combine(Receipt, BuildReceipt.PatchHashFile), new string('b', 64) + "  tracked.patch\n");
        BuildReceipt.Verification verification = Verify();
        Assert.True(verification.Matches);
        Assert.True(verification.Dirty);
        Assert.Equal(new string('b', 64), verification.PatchSha256);
    }

    [Fact]
    public void AMissingOrMalformedReceiptMatchesNothing()
    {
        File.Delete(Path.Combine(Receipt, BuildReceipt.OutputFile));
        File.AppendAllText(Path.Combine(Receipt, BuildReceipt.SourceFile),
            "not a hash line\n" + new string('a', 64) + "  ../outside.cs\n" + Line(Repo, "DK2.slnx"));
        IReadOnlyList<string> problems = Verify().Problems;
        Assert.Contains("output.sha256 is missing from the receipt", problems);
        Assert.Contains("source.sha256:4 is not a sha256sum line", problems);
        Assert.Contains("source.sha256:5 names a path outside the tree: ../outside.cs", problems);
        Assert.Contains("source.sha256:6 lists DK2.slnx twice", problems);
        Assert.Contains("no repository (DK2.slnx) above the harness to check the source against",
            BuildReceipt.Verify(Receipt, Harness, repositoryRoot: null).Problems);
    }

    private static Scenario ShippedStart() => new()
    {
        Id = "start", Map = "campaign:level1", Seed = 1, Random = "dk2", FogOfWar = true, ViewingPlayer = 3, DurationTicks = 10,
        Setup = new ScenarioSetup { PlacedThings = true },
    };

    [Fact]
    public void OnlyAShippedLevelsOwnStartIsUnmodified()
    {
        Assert.Empty(PlayerRunReport.ModifiedStart(ShippedStart()));
        Scenario changed = ShippedStart();
        changed.Map = "flat_20x20";
        changed.Random = "modern";
        changed.FogOfWar = false;
        changed.Setup = new ScenarioSetup
        {
            PlacedThings = false,
            Players = [new ScenarioPlayer { Id = 3, Gold = 50000 }],
            Rooms = [new ScenarioRoom()],
            Creatures = [new ScenarioCreature()],
            Objects = [new ScenarioObject()],
        };
        changed.Commands = [new ScenarioCommand()];
        changed.Work = [new ScenarioWork()];
        changed.KeeperPersonalities = new Dictionary<byte, string> { [2] = "Rogue" };
        Assert.Equal(11, PlayerRunReport.ModifiedStart(changed).Count);
        // The shipped level's own computer keepers may be driven: the real game runs them.
        Scenario keepers = ShippedStart();
        keepers.ComputerKeepers = [2];
        Assert.Empty(PlayerRunReport.ModifiedStart(keepers));
    }

    [Fact]
    public void ARunOnAHarnessThatIsNotTheReceiptsIsRefusedBeforeItsFolder()
    {
        File.WriteAllText(Path.Combine(Harness, "DK2.Simulation.dll"), "rebuilt");
        string scenarioPath = Path.Combine(_root, "probe.yaml");
        File.WriteAllText(scenarioPath, """
            id: receipt-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 4
            """);
        RunTemplate template = HarnessCommandLine.RunOptions(["run", "x", "--campaign-player", "3", "--player-receipt", Receipt])!;
        var error = new StringWriter();
        var database = ScenarioTests.SyntheticDatabase();
        int exit = PlayerRunCommand.Run(new ScenarioRunner(database, null), database, null, Scenario.Load(scenarioPath), template,
            Path.Combine(_root, "out"), "run", TextWriter.Null, error, tune: null, harnessDirectory: Harness);
        Assert.Equal(2, exit);
        Assert.False(Directory.Exists(Path.Combine(_root, "out")));
        Assert.Contains("output DK2.Simulation.dll differs from the receipt", error.ToString(), StringComparison.Ordinal);
    }
}
