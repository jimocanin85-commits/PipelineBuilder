using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using Xunit;
using YamlDotNet.Serialization;

namespace PipelineBuilder.Tests;

/// <summary>One test (or more) per bug found in the September 2026 code review.</summary>
public class RegressionTests
{
    private readonly IServiceProvider _services;
    private readonly IPipelineGeneratorService _generator;

    public RegressionTests()
    {
        _services = new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider();
        _generator = _services.GetRequiredService<IPipelineGeneratorService>();
    }

    private static PipelineDefinition Minimal() => new()
    {
        Name = "app",
        ProjectType = ProjectType.DotNet,
        Environments = new[] { "test", "prod" }
    };

    // 1. BuildConfiguration was only defined when variable groups or Key Vault were configured.
    [Fact]
    public void BuildConfigurationIsDefinedWithoutVariableGroups()
    {
        var yaml = _generator.Generate(Minimal()).Yaml;

        var root = Parse(yaml);
        var variables = Assert.IsType<List<object>>(root["variables"]).Cast<Dictionary<object, object>>();
        Assert.Contains(variables, v => (string?)v.GetValueOrDefault("name") == "BuildConfiguration");
        Assert.Contains("$(BuildConfiguration)", yaml);
    }

    // 2. Docker containers were started on a throwaway hosted agent instead of the servers.
    [Fact]
    public void DockerDeploysToRegisteredServers()
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer };

        var yaml = _generator.Generate(definition).Yaml;

        Assert.Contains("resourceType: VirtualMachine", yaml);
    }

    // 3. Variable group names were written unquoted, and the pipeline name could contain line breaks.
    [Theory]
    [InlineData("vg: prod")]
    [InlineData("vg #1")]
    [InlineData("it's")]
    public void VariableGroupNamesCannotBreakTheYaml(string groupName)
    {
        var definition = Minimal();
        definition.VariableGroups = new[]
        {
            new VariableGroupConfig { Name = groupName, Scope = VariableGroupScope.Pipeline },
            new VariableGroupConfig { Name = groupName, Scope = VariableGroupScope.Environment, EnvironmentName = "prod" }
        };

        var root = Parse(_generator.Generate(definition).Yaml);

        var variables = Assert.IsType<List<object>>(root["variables"]).Cast<Dictionary<object, object>>();
        Assert.Contains(variables, v => (string?)v.GetValueOrDefault("group") == groupName);
    }

    [Theory]
    [InlineData("app\ntrigger: none")]
    [InlineData("app\r")]
    public void PipelineNameWithLineBreaksIsRejected(string name)
    {
        var definition = Minimal();
        definition.Name = name;

        var ex = Assert.Throws<ArgumentException>(() => _generator.Generate(definition));
        Assert.Contains("line breaks", ex.Message);
    }

    // 6. "production" wasn't treated as production everywhere, and "main" was hard-coded.
    [Fact]
    public void ProductionUsesTheConfiguredReleaseBranch()
    {
        var definition = Minimal();
        definition.Environments = new[] { "test", "production" };
        definition.ReleaseBranch = "master";

        var yaml = _generator.Generate(definition).Yaml;

        Assert.Contains("eq(variables['Build.SourceBranch'], 'refs/heads/master')", yaml);
        Assert.DoesNotContain("refs/heads/main", yaml);
    }

    [Theory]
    [InlineData("main'; exit")]
    [InlineData("")]
    public void InvalidReleaseBranchIsRejected(string branch)
    {
        var definition = Minimal();
        definition.ReleaseBranch = branch;
        Assert.Throws<ArgumentException>(() => _generator.Generate(definition));
    }

    private static Dictionary<object, object> Parse(string yaml) =>
        Assert.IsType<Dictionary<object, object>>(new DeserializerBuilder().Build().Deserialize<object>(yaml));
}
