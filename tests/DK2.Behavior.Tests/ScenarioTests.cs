using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;

namespace DK2.Behavior.Tests;

/// <summary>
/// Covers the scenario harness itself: that every scenario in the repository
/// loads, that a run is reproducible, and - most importantly - that the
/// harness reports what it could not do rather than quietly skipping it.
///
/// These run without a Dungeon Keeper 2 installation, so they are part of the
/// CI gate. Scenarios that need shipped maps are exercised separately and skip
/// when the install is absent.
/// </summary>
public class ScenarioTests
{
    [Fact]
    public void EveryScenarioInTheRepository_Loads()
    {
        List<string> files = ScenarioFiles();
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            Scenario scenario = Scenario.Load(file);
            Assert.False(string.IsNullOrWhiteSpace(scenario.Id), $"{file}: no id");
            Assert.False(string.IsNullOrWhiteSpace(scenario.Map), $"{file}: no map");
            Assert.True(scenario.DurationTicks > 0, $"{file}: duration_ticks must be positive");
            Assert.True(scenario.UnknownKeys.Count == 0,
                $"{file}: unrecognised keys {string.Join(", ", scenario.UnknownKeys)}");
        }
    }

    [Fact]
    public void ScenarioIds_AreUnique()
    {
        // Ids name the output directory, so a duplicate silently overwrites
        // another scenario's evidence.
        List<string> ids = ScenarioFiles().Select(f => Scenario.Load(f).Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void RunningTheSameScenarioTwice_ProducesTheSameHashSeries()
    {
        Scenario scenario = LoadFlatScenario();
        var runner = new ScenarioRunner(SyntheticDatabase(), installDir: null);

        ScenarioResult first = runner.Run(scenario);
        ScenarioResult second = runner.Run(scenario);

        Assert.Equal(first.HashPerTick, second.HashPerTick);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
    }

    [Fact]
    public void Run_ReportsThatNoSystemsAreRegisteredYet()
    {
        // The honesty check. Until real mechanics land, every run must say so;
        // a report that looks complete would corrupt the regression corpus.
        ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(LoadFlatScenario());

        Assert.Contains(result.Unsupported, u => u.Contains("no simulation systems", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_ReportsUnknownKeysRatherThanIgnoringThem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: unknown-key-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 5
            wibble: 42
            """);

        try
        {
            Scenario scenario = Scenario.Load(path);
            Assert.Contains("wibble", scenario.UnknownKeys);

            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);
            Assert.Contains(result.Unsupported, u => u.Contains("wibble", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_ReportsSetupItWillNotApply()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: unapplied-setup-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 5
            setup:
              players:
                - { id: 1, gold: 500, mana: 100 }
              rooms:
                - { type: Lair, at: [2, 2], size: [3, 3] }
              creatures:
                - { type: Goblin, at: [4, 4] }
            observe:
              - first_destination
            """);

        try
        {
            ScenarioResult result = new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path));

            Assert.Contains(result.Unsupported, u => u.StartsWith("setup.rooms", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.StartsWith("setup.creatures", StringComparison.Ordinal));
            Assert.Contains(result.Unsupported, u => u.StartsWith("observe.first_destination", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Setup_AppliesStartingGoldAndMana()
    {
        // Gold and mana are the two things M2 can honour, so they must actually
        // reach the world - a scenario whose setup is a no-op is worse than no
        // scenario at all.
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: starting-resources-probe
            map: flat_8x8
            seed: 1
            duration_ticks: 1
            setup:
              players:
                - { id: 1, gold: 1234, mana: 5678 }
            """);

        try
        {
            Scenario scenario = Scenario.Load(path);
            var withResources = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);

            scenario.Setup.Players[0].Gold = 0;
            scenario.Setup.Players[0].Mana = 0;
            ScenarioResult without = new ScenarioRunner(SyntheticDatabase(), null).Run(scenario);

            Assert.NotEqual(withResources.FinalStateHash, without.FinalStateHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnparseableMapSpec_IsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dk2-scenario-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            id: bad-map-probe
            map: somewhere_nice
            seed: 1
            duration_ticks: 5
            """);

        try
        {
            Assert.Throws<InvalidDataException>(
                () => new ScenarioRunner(SyntheticDatabase(), null).Run(Scenario.Load(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Scenario LoadFlatScenario() =>
        Scenario.Load(Path.Combine(RepositoryRoot(), "scenarios", "smoke", "determinism_flat_map.yaml"));

    private static List<string> ScenarioFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "scenarios"), "*.yaml", SearchOption.AllDirectories)
             .OrderBy(f => f, StringComparer.Ordinal)];

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the repository root (no DK2.slnx above the test binaries).");
    }

    /// <summary>
    /// A minimal stand-in database so harness tests run without the original
    /// game. The numbers are invented and are not claims about DK2; only the
    /// two terrain roles the flat-map builder looks for matter here.
    /// </summary>
    private static GameDatabase SyntheticDatabase() => new(
        [
            Terrain(1, "Impenetrable Rock", TerrainFlags.Solid | TerrainFlags.Impenetrable, 0),
            Terrain(3, "Dirt Path", TerrainFlags.Ownable, 100),
        ],
        rooms: [],
        creatures: [],
        variables: new GlobalVariables());

    private static TerrainDef Terrain(byte id, string name, TerrainFlags flags, ushort health) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = health,
        MaxHealth = health,
        GoldValue = 0,
        ManaGain = 0,
        MaxManaGain = 0,
        Damage = 0,
        MaxHealthTypeTerrainId = 0,
        DestroyedTypeTerrainId = 0,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };
}
