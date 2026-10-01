using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

public class SettingsAndValidationTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    private static PipelineDefinition Rich()
    {
        var definition = WizardState.CreateDefault();
        definition.Name = "orders-api";
        definition.ReleaseBranch = "release";
        definition.KeyVault = new KeyVaultConfig { KeyVaultName = "kv-orders" };
        definition.IaC = new InfrastructureAsCodeConfig { Tool = IaCTool.Bicep, WorkingDirectory = "infra", PlanOnly = true };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.WindowsService, ServiceName = "Orders", TargetPath = @"D:\apps\orders" };
        definition.Trigger = new TriggerConfig { IncludeBranches = new[] { "main" }, PathFilters = new[] { "src/*" } };
        return definition;
    }

    [Fact]
    public void SavedSettingsReproduceTheSamePipeline()
    {
        var original = Rich();
        var json = PipelineDefinitionSerializer.ToJson(original);
        var loaded = PipelineDefinitionSerializer.FromJson(json);

        Assert.Equal(_generator.Generate(original).Yaml, _generator.Generate(loaded).Yaml);
    }

    [Fact]
    public void SettingsFileIsReadableAndHasNoComputedValues()
    {
        var json = PipelineDefinitionSerializer.ToJson(Rich());

        Assert.Contains($"\"format\": \"{PipelineDefinitionSerializer.Format}\"", json);
        Assert.Contains("\"kind\": \"WindowsService\"", json);   // enums as names, not numbers
        Assert.DoesNotContain("OrDefault", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isServerDeployment", json);
    }

    [Fact]
    public void SettingsSavedBeforeTheRenameStillOpen()
    {
        var json = PipelineDefinitionSerializer.ToJson(Rich())
            .Replace(PipelineDefinitionSerializer.Format, PipelineDefinitionSerializer.LegacyFormat);

        Assert.Equal("orders-api", PipelineDefinitionSerializer.FromJson(json).Name);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"definition\":{}}")]
    [InlineData("{}")]
    public void InvalidSettingsFilesAreRejected(string json)
    {
        Assert.Throws<FormatException>(() => PipelineDefinitionSerializer.FromJson(json));
    }

    [Theory]
    [InlineData("Environments", WizardStep.EnvironmentSelection)]
    [InlineData("ReleaseBranch", WizardStep.EnvironmentSelection)]
    [InlineData("PoolName", WizardStep.BuildAgent)]
    [InlineData("HealthChecks", WizardStep.HealthChecks)]
    [InlineData("Deployment", WizardStep.DeploymentTarget)]
    [InlineData("KeyVault", WizardStep.VariableGroupsAndKeyVault)]
    [InlineData("NamingConvention", WizardStep.GovernancePolicies)]
    public void ValidationFieldsMapToWizardSteps(string field, WizardStep step)
    {
        Assert.Equal(step, StepMap.ForField(field));
    }

    [Fact]
    public void EveryBlockingErrorBelongsToAStep()
    {
        var definition = new PipelineDefinition
        {
            Name = "",
            Environments = new[] { "Bad Name", "test", "test" },
            ReleaseBranch = "",
            BuildAgent = BuildAgentType.SelfHosted,
            Artifact = new ArtifactConfig { ArtifactName = "" },
            DeploymentStrategy = new DeploymentStrategyConfig { StrategyType = DeploymentStrategyType.Canary, CanaryPercentage = 200 },
            Governance = new GovernancePolicyConfig { NamingConvention = "[" },
            HealthChecks = new[] { new HealthCheckConfig { Enabled = true, Url = "not a url" } },
            IaC = new InfrastructureAsCodeConfig { Tool = IaCTool.Terraform, WorkingDirectory = "" }
        };

        var errors = PipelineDefinitionValidator.ValidateDetailed(definition);

        Assert.True(errors.Count >= 10, $"Expected every rule to fire, got {errors.Count}");
        Assert.All(errors, e => Assert.NotNull(StepMap.ForField(e.AffectedField)));
    }

    [Fact]
    public void GovernanceFindingsBelongToSteps()
    {
        var definition = WizardState.CreateDefault();
        definition.HealthChecks = Array.Empty<HealthCheckConfig>();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom };
        definition.VariableGroups = new[] { new VariableGroupConfig { Name = "" } };

        var findings = _generator.Generate(definition).ValidationResults
            .Where(v => v.Severity != ValidationSeverity.Info && !v.AffectedField!.StartsWith("Line "));

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.NotNull(StepMap.ForField(f.AffectedField)));
    }
}
