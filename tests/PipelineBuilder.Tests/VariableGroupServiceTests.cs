using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

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
    public void Validate_GroupWithoutAName_ReturnsError()
    {
        var results = _sut.Validate(new[] { new VariableGroupConfig { Name = " " } });
        Assert.Contains(results, r => r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void AGroupWithSecretsSharedWithTheWholePipelineIsFlagged()
    {
        var groups = new[]
        {
            new VariableGroupConfig { Name = "vg-shared-secrets", Scope = VariableGroupScope.Pipeline, ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-prod-secrets", Scope = VariableGroupScope.Environment, EnvironmentName = "prod", ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-common", Scope = VariableGroupScope.Pipeline }
        };

        var warning = Assert.Single(_sut.Validate(groups));

        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Contains("vg-shared-secrets", warning.Message);
    }
}
