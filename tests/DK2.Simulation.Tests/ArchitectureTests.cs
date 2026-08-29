using System.Reflection;

namespace DK2.Simulation.Tests;

/// <summary>
/// Guards the project's central architectural rule: the simulation must never
/// depend on Unity. (docs/DECISIONS.md ADR-0002)
/// </summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("DK2.Core")]
    [InlineData("DK2.Data.Model")]
    [InlineData("DK2.Simulation")]
    public void SimulationAssemblies_DoNotReferenceUnity(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        r.Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
