using Microsoft.Extensions.DependencyInjection;
using SimlifiezYaml.Core.DependencyInjection;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Abstractions;
using Xunit;

namespace SimlifiezYaml.Core.Tests;

public class PipelineGeneratorTests
{
    private readonly IPipelineGeneratorService _generator;

    public PipelineGeneratorTests()
    {
        var services = new ServiceCollection();
        services.AddSimlifiezYamlCore();
        _generator = services.BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();
    }

    [Fact]
    public void Generate_IncludesEnterpriseStages()
    {
        var definition = WizardState_CreateDefinition();
        var result = _generator.Generate(definition);

        Assert.Contains("stage: Build", result.Yaml);
        Assert.Contains("stage: Test", result.Yaml);
        Assert.Contains("stage: Artifact", result.Yaml);
        Assert.Contains("environment: test", result.Yaml);
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
        HealthChecks = new[] { new HealthCheckConfig { Enabled = true, Url = "https://localhost/health" } },
        Governance = new GovernancePolicyConfig { RequiredVariableGroups = new[] { "vg-test" } }
    };
}
