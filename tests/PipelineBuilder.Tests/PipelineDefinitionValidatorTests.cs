using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

public class PipelineDefinitionValidatorTests
{
    [Fact]
    public void RejectsNullDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => PipelineDefinitionValidator.Validate(null!));
    }

    [Fact]
    public void RejectsEmptyName()
    {
        var definition = new PipelineDefinition { Name = "" };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("Pipeline name is required"));
    }

    [Fact]
    public void RejectsExcessivelyLongName()
    {
        var definition = new PipelineDefinition { Name = new string('x', 300) };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("255 characters"));
    }

    [Fact]
    public void RejectsNoEnvironments()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            Environments = Array.Empty<string>()
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("at least one environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsEmptyEnvironmentName()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            Environments = new[] { "", "test" }
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("Environment names cannot be empty"));
    }

    [Fact]
    public void RejectsInvalidEnvironmentNames()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            Environments = new[] { "test@invalid", "Test_Name" }  // Invalid chars
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("invalid characters"));
    }

    [Fact]
    public void RequiresSelfHostedPoolName()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            BuildAgent = BuildAgentType.SelfHosted,
            PoolName = ""
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("Pool name is required"));
    }

    [Fact]
    public void RejectsInvalidCanaryPercentage()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            DeploymentStrategy = new DeploymentStrategyConfig
            {
                StrategyType = DeploymentStrategyType.Canary,
                CanaryPercentage = 150
            }
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("between 0 and 100"));
    }

    [Fact]
    public void RejectsInvalidRegex()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            Governance = new GovernancePolicyConfig { NamingConvention = "[invalid(regex" }
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("valid regular expression"));
    }

    [Fact]
    public void RejectsHttpHealthCheckWithoutEndpoint()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            HealthChecks = new[]
            {
                new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = null }
            }
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Contains(errors, e => e.Contains("HTTP health check requires"));
    }

    [Fact]
    public void ThrowsOnInvalid()
    {
        var definition = new PipelineDefinition { Name = "" };
        Assert.Throws<ArgumentException>(() => PipelineDefinitionValidator.ValidateOrThrow(definition));
    }

    [Fact]
    public void PassesValidDefinition()
    {
        var definition = new PipelineDefinition
        {
            Name = "my-pipeline",
            Environments = new[] { "test", "prod" },
            BuildAgent = BuildAgentType.MicrosoftHosted
        };
        var errors = PipelineDefinitionValidator.Validate(definition);
        Assert.Empty(errors);
    }
}
