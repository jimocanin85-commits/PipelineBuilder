using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Validation;
using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The input rules: problems in the settings that block generation.</summary>
public class PipelineDefinitionValidatorTests
{
    private static IReadOnlyList<string> Validate(PipelineDefinition definition) =>
        PipelineValidator.CreateDefault().ValidateInput(definition).Select(r => r.Message).ToList();

    [Fact]
    public void RejectsNullDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => Validate(null!));
    }

    [Fact]
    public void RejectsEmptyName()
    {
        var definition = new PipelineDefinition { Name = "" };
        var errors = Validate(definition);
        Assert.Contains(errors, e => e.Contains("Pipeline name is required"));
    }

    [Fact]
    public void RejectsExcessivelyLongName()
    {
        var definition = new PipelineDefinition { Name = new string('x', 300) };
        var errors = Validate(definition);
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
        var errors = Validate(definition);
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
        var errors = Validate(definition);
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
        var errors = Validate(definition);
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
        var errors = Validate(definition);
        Assert.Contains(errors, e => e.Contains("Pool name is required"));
    }

    [Fact]
    public void RejectsADockerProjectWithoutADockerImage()
    {
        var definition = new PipelineDefinition
        {
            Name = "test",
            ProjectType = ProjectType.Docker,
            Artifact = new ArtifactConfig { ArtifactType = ArtifactType.ZipPackage }
        };
        var errors = Validate(definition);
        Assert.Contains(errors, e => e.Contains("packaged as a Docker image"));
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
        var errors = Validate(definition);
        Assert.Contains(errors, e => e.Contains("HTTP health check requires"));
    }

    [Fact]
    public void ThrowsOnInvalid()
    {
        var definition = new PipelineDefinition { Name = "" };
        var generator = new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

        var ex = Assert.Throws<ArgumentException>(() => generator.Generate(definition));
        Assert.Contains("Pipeline name is required", ex.Message);
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
        var errors = Validate(definition);
        Assert.Empty(errors);
    }
}
