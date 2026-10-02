using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Abstractions;
using Xunit;

namespace PipelineBuilder.Tests;

public class PipelineGeneratorTests
{
    private readonly IPipelineGeneratorService _generator;

    public PipelineGeneratorTests()
    {
        var services = new ServiceCollection();
        services.AddPipelineBuilderCore();
        _generator = services.BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();
    }

    [Fact]
    public void Generate_IncludesEnterpriseStages()
    {
        var definition = WizardState_CreateDefinition();
        var result = _generator.Generate(definition);

        Assert.Contains("stage: Build", result.Yaml);
        Assert.DoesNotContain("stage: Test", result.Yaml);
        Assert.Contains("- stage: Deploy_test\n  displayName: 'Deploy test'\n  dependsOn: Build", result.Yaml.ReplaceLineEndings("\n"));
        Assert.Contains("      name: test", result.Yaml);
        Assert.Contains("- group: 'vg-test'", result.Yaml);
        Assert.NotEmpty(result.Explanations);
    }

    [Fact]
    public void Generate_DetectsPlaintextSecrets()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            Environments = new[] { "test" },
            Deployment = new DeploymentConfig { CustomScript = "$password = 'SuperSecret123!'" }
        };

        var result = _generator.Generate(definition);

        Assert.Contains(result.ValidationResults, v =>
            v.Severity == ValidationSeverity.Error && v.Message.Contains("plaintext password"));
    }

    private static PipelineDefinition WizardState_CreateDefinition() => new()
    {
        Name = "enterprise-pipeline",
        ProjectType = ProjectType.DotNet,
        Environments = new[] { "test", "preprod", "prod" },
        VariableGroups = new[] { new VariableGroupConfig { Name = "vg-test", Scope = VariableGroupScope.Pipeline } },
        Artifact = new ArtifactConfig { ArtifactName = "drop" },
        Rollback = new RollbackConfig { Enabled = true, Target = RollbackTarget.Iis },
        HealthChecks = new[] { new HealthCheckConfig { Enabled = true, Url = "https://localhost/health" } }
    };
}
