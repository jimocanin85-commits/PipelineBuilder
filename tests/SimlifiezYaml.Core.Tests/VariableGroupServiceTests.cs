using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Services;
using Xunit;

namespace SimlifiezYaml.Core.Tests;

public class VariableGroupServiceTests
{
    private readonly VariableGroupService _sut = new();

    [Fact]
    public void GeneratePipelineVariables_IncludesGroupNames()
    {
        var definition = new PipelineDefinition
        {
            VariableGroups = new[] { new VariableGroupConfig { Name = "vg-test", Scope = VariableGroupScope.Pipeline } }
        };

        var yaml = _sut.GeneratePipelineVariables(definition);

        Assert.Contains("- group: 'vg-test'", yaml);
        Assert.Contains("BuildConfiguration", yaml);
    }

    [Fact]
    public void Validate_RequiredGroupMissing_ReturnsError()
    {
        var governance = new GovernancePolicyConfig { RequiredVariableGroups = new[] { "vg-prod" } };
        var results = _sut.Validate(Array.Empty<VariableGroupConfig>(), governance);
        Assert.Contains(results, r => r.Severity == ValidationSeverity.Error);
    }
}
