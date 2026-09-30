using Microsoft.Extensions.DependencyInjection;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.DependencyInjection;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Services;
using Xunit;
using YamlDotNet.Serialization;

namespace SimlifiezYaml.Core.Tests;

/// <summary>One test (or more) per bug found in the September 2026 code review.</summary>
public class RegressionTests
{
    private readonly IServiceProvider _services;
    private readonly IPipelineGeneratorService _generator;

    public RegressionTests()
    {
        _services = new ServiceCollection().AddSimlifiezYamlCore().BuildServiceProvider();
        _generator = _services.GetRequiredService<IPipelineGeneratorService>();
    }

    private static PipelineDefinition Minimal() => new()
    {
        Name = "app",
        ProjectType = ProjectType.DotNet,
        Environments = new[] { "test", "prod" },
        Governance = new GovernancePolicyConfig { RequireHealthCheck = false }
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

    // 2. Docker containers were started on a throwaway hosted agent for Cloud/Hybrid targets.
    [Theory]
    [InlineData(DeploymentTarget.Hybrid)]
    [InlineData(DeploymentTarget.OnPrem)]
    public void DockerDeploysToRegisteredServers(DeploymentTarget target)
    {
        var definition = Minimal();
        definition.DeploymentTarget = target;
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer };

        var yaml = _generator.Generate(definition).Yaml;

        Assert.Contains("resourceType: VirtualMachine", yaml);
    }

    [Fact]
    public void DockerWithCloudTargetIsFlagged()
    {
        var definition = Minimal();
        definition.DeploymentTarget = DeploymentTarget.Cloud;
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer };

        var result = _generator.Generate(definition);

        Assert.Contains(result.ValidationResults, v => v.Severity == ValidationSeverity.Warning && v.Message.Contains("DockerContainer"));
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

    // 4. The repository scan suggested template ids that don't exist.
    [Fact]
    public void RepositoryScanOnlySuggestsExistingTemplates()
    {
        var scanner = _services.GetRequiredService<IRepoScannerService>();
        var marketplace = _services.GetRequiredService<ITemplateMarketplaceService>();
        var everything = new[]
        {
            "App.sln", "src/App/App.csproj", "tests/App.Tests/App.Tests.csproj", "src/App/web.config", "Dockerfile",
            "docker-compose.yml", "package.json", "infra/main.tf", "infra/main.bicep", "k8s/deployment.yaml",
            "azure-pipelines.yml", "config/settings.yaml"
        };

        var result = scanner.ScanFileList(everything);

        Assert.NotEmpty(result.SuggestedTemplates);
        Assert.All(result.SuggestedTemplates, id => Assert.NotNull(marketplace.GetById(id)));
    }

    [Theory]
    [InlineData("k8s/deployment.yaml", true)]
    [InlineData("deploy/manifests/service.yml", true)]
    [InlineData("config/settings.yaml", false)]
    [InlineData("azure-pipelines.yml", false)]
    public void KubernetesTemplateIsOnlySuggestedForManifestFolders(string path, bool expected)
    {
        var result = _services.GetRequiredService<IRepoScannerService>().ScanFileList(new[] { path });
        Assert.Equal(expected, result.SuggestedTemplates.Contains("aks-deploy"));
    }

    // 5. "Plan only" was ignored for Bicep, ARM and PowerShell.
    [Fact]
    public void PlanOnlyNeverAppliesInfrastructure()
    {
        var iac = new IacYamlService();

        string Steps(IaCTool tool, bool planOnly) =>
            string.Join("\n", iac.GenerateIacSteps(new InfrastructureAsCodeConfig { Tool = tool, WorkingDirectory = "infra", PlanOnly = planOnly }, "prod"));

        Assert.Contains("az deployment group what-if", Steps(IaCTool.Bicep, true));
        Assert.Contains("az deployment group create", Steps(IaCTool.Bicep, false));
        Assert.Contains("deploymentMode: 'Validation'", Steps(IaCTool.ArmTemplate, true));
        Assert.Contains("deploymentMode: 'Incremental'", Steps(IaCTool.ArmTemplate, false));
        Assert.Contains("-WhatIf", Steps(IaCTool.PowerShell, true));
        Assert.DoesNotContain("-WhatIf", Steps(IaCTool.PowerShell, false));
        Assert.DoesNotContain("command: 'apply'", Steps(IaCTool.Terraform, true));
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

    [Fact]
    public void HealthCheckRequirementAppliesToProductionToo()
    {
        var definition = Minimal();
        definition.Environments = new[] { "test", "production" };
        definition.Governance = new GovernancePolicyConfig { RequireHealthCheck = true };

        var result = _generator.Generate(definition);

        Assert.Contains(result.ValidationResults, v => v.Severity == ValidationSeverity.Error && v.Message.Contains("health check"));
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

    // 7. Terraform applied a fresh plan instead of the one it had just shown.
    [Fact]
    public void TerraformAppliesTheSavedPlan()
    {
        var steps = new IacYamlService().GenerateIacSteps(
            new InfrastructureAsCodeConfig { Tool = IaCTool.Terraform, WorkingDirectory = "infra" }, "prod");

        Assert.Contains(steps, s => s.Contains("command: 'plan'") && s.Contains("commandOptions: '-input=false -out=tfplan'"));
        Assert.Contains(steps, s => s.Contains("command: 'apply'") && s.Contains("commandOptions: '-input=false tfplan'"));
    }

    private static Dictionary<object, object> Parse(string yaml) =>
        Assert.IsType<Dictionary<object, object>>(new DeserializerBuilder().Build().Deserialize<object>(yaml));
}
