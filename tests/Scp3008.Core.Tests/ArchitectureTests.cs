using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void The_simulation_core_does_not_depend_on_godot()
    {
        var references = typeof(Simulation).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }
}
