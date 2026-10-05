using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using Xunit;
using YamlDotNet.Serialization;

namespace PipelineBuilder.Tests;

/// <summary>
/// End-to-end checks on the generated pipeline: the YAML must parse, stage names and
/// dependencies must be valid, and scripts must reach the agent unmangled.
/// </summary>
public class GeneratedYamlTests
{
    private static readonly Regex StageIdentifier = new("^[A-Za-z0-9_]+$");
    private readonly IPipelineGeneratorService _generator;

    public GeneratedYamlTests()
    {
        var services = new ServiceCollection();
        services.AddPipelineBuilderCore();
        _generator = services.BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();
    }

    public static IEnumerable<object[]> AllOptionCombinations()
    {
        foreach (var strategy in Enum.GetValues<DeploymentStrategyType>())
        foreach (var artifact in Enum.GetValues<ArtifactType>())
        foreach (var kind in Enum.GetValues<DeploymentKind>())
            yield return new object[] { strategy, artifact, kind };
    }

    public static IEnumerable<object[]> AllRollbackTargets() =>
        Enum.GetValues<RollbackTarget>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllOptionCombinations))]
    public void GeneratedYaml_IsValidForEveryOptionCombination(
        DeploymentStrategyType strategy, ArtifactType artifact, DeploymentKind kind)
    {
        var definition = FullDefinition();
        definition.DeploymentStrategy = new DeploymentStrategyConfig { StrategyType = strategy };
        definition.Artifact = new ArtifactConfig { ArtifactType = artifact, ArtifactName = "drop" };
        definition.Deployment = new DeploymentConfig { Kind = kind };

        AssertValidPipeline(_generator.Generate(definition).Yaml);
    }

    [Theory]
    [MemberData(nameof(AllRollbackTargets))]
    public void GeneratedYaml_IsValidForEveryCustomRollbackTarget(RollbackTarget rollback)
    {
        var definition = FullDefinition();
        definition.Rollback = new RollbackConfig { Enabled = true, Target = rollback, BackupPath = @"D:\backups" };

        AssertValidPipeline(_generator.Generate(definition).Yaml);
    }

    private static void AssertValidPipeline(string yaml)
    {
        var root = Parse(yaml);
        Assert.True(root.ContainsKey("trigger"), "Missing trigger");
        var stages = StagesOf(root);
        Assert.NotEmpty(stages);

        var names = stages.Select(s => (string)s["stage"]).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names.Where(n => n != PipelineYaml.DeployStageName), n => Assert.Matches(StageIdentifier, n));
        Assert.Contains(PipelineYaml.DeployStageName, names);
        Assert.NotEmpty(PipelineYaml.Environments(root));

        foreach (var stage in stages)
        {
            foreach (var dependency in DependenciesOf(stage))
                Assert.Contains(dependency, names);

            var jobs = JobsOf(stage);
            var jobNames = jobs.Select(JobName).ToList();
            Assert.Equal(jobNames.Count, jobNames.Distinct().Count());
            Assert.All(jobNames, n => Assert.Matches(StageIdentifier, n));
            foreach (var job in jobs)
            foreach (var dependency in DependenciesOf(job))
                Assert.Contains(dependency, jobNames);
        }

        foreach (var script in PowerShellScripts(root))
        {
            Assert.DoesNotContain("$([", script);          // Azure macros must not be mangled
            Assert.DoesNotContain("''", script);             // no doubled quotes from over-escaping
            Assert.DoesNotContain(@"\\inetpub", script);     // no doubled backslashes in local paths
        }
    }

    [Fact]
    public void GeneratedYaml_HealthCheckScriptKeepsUrlIntact()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var scripts = PowerShellScripts(Parse(yaml)).ToList();

        Assert.Contains(scripts, s => s.Contains("$uri = 'https://myapp.contoso.com/health'"));
        Assert.Contains(scripts, s => s.Contains("#$env:BUILD_BUILDNUMBER"));
    }

    [Fact]
    public void GeneratedYaml_TheDeployStageIsWrittenOnceAndRepeatedPerEnvironment()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var root = Parse(yaml);

        Assert.Equal(new[] { "test", "pre-prod", "prod" }, PipelineYaml.Environments(root));
        Assert.Contains("- ${{ each environment in parameters.environments }}:", yaml);

        // Stage names cannot contain hyphens, so the name replaces them; the environment keeps its own name.
        var job = DeployJob(root);
        Assert.Equal(PipelineYaml.Environment, (string)Assert.IsType<Dictionary<object, object>>(job["environment"])["name"]);
        Assert.Equal("Deploy", (string)job["deployment"]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(yaml, "- stage: Deploy_"));
    }

    [Fact]
    public void GeneratedYaml_OnlyProductionIsLimitedToTheReleaseBranch()
    {
        var stage = PipelineYaml.DeployStage(Parse(_generator.Generate(FullDefinition()).Yaml));

        var condition = Assert.IsType<Dictionary<object, object>>(stage["${{ if in(environment, 'prod') }}"]);
        Assert.Equal("and(succeeded(), ne(variables['Build.Reason'], 'PullRequest'), eq(variables['Build.SourceBranch'], 'refs/heads/main'))", (string)condition["condition"]);
        Assert.False(stage.ContainsKey("condition"));
        Assert.False(stage.ContainsKey("dependsOn"));
    }

    [Fact]
    public void GeneratedYaml_EnvironmentVariableGroupsAreOnlyReadByThatEnvironment()
    {
        var stage = PipelineYaml.DeployStage(Parse(_generator.Generate(FullDefinition()).Yaml));

        var onlyProd = Assert.IsType<Dictionary<object, object>>(stage["${{ if eq(environment, 'prod') }}"]);
        var group = Assert.IsType<Dictionary<object, object>>(Assert.Single(Assert.IsType<List<object>>(onlyProd["variables"])));
        Assert.Equal("vg-prod", (string)group["group"]);
    }

    [Fact]
    public void GeneratedYaml_NotificationsAreSplitBySuccessAndFailure()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var stages = StagesOf(Parse(yaml));

        var success = Assert.Single(stages, s => (string)s["stage"] == "Notify_Success");
        Assert.Equal("and(succeeded(), ne(variables['Build.Reason'], 'PullRequest'))", (string)success["condition"]);
        Assert.Contains("succeeded", string.Join("\n", PowerShellScripts(success)));
        Assert.DoesNotContain(" failed", string.Join("\n", PowerShellScripts(success)));

        var failure = Assert.Single(stages, s => (string)s["stage"] == "Notify_Failure");
        Assert.Equal("and(failed(), ne(variables['Build.Reason'], 'PullRequest'))", (string)failure["condition"]);
        var failureDependencies = DependenciesOf(failure).ToList();
        Assert.Equal(new[] { "Build", PipelineYaml.DeployStageName }, failureDependencies);
        Assert.False(success.ContainsKey("dependsOn")); // follows the last deploy stage
        Assert.DoesNotContain("# condition:", yaml);
    }

    [Fact]
    public void GeneratedYaml_BuildJobCompilesOnceThenTestsAndPackages()
    {
        var root = Parse(_generator.Generate(FullDefinition()).Yaml);

        Assert.Equal(new[] { "Build" }, StagesOf(root).Select(s => (string)s["stage"]).Take(1));
        Assert.DoesNotContain(StagesOf(root), s => (string)s["stage"] is "Test" or "Artifact");

        var job = Assert.Single(JobsOf(StageNamed(root, "Build")));
        var steps = StepsOf(job);
        var commands = steps.Select(s => s.GetValueOrDefault("task") as string == "DotNetCoreCLI@2" ? (string)Inputs(s)["command"] : s.GetValueOrDefault("task") as string).ToList();
        Assert.Equal(new[] { "restore", "build", "test", "publish", "PublishPipelineArtifact@1" }, commands);

        // Only the build step compiles; test and publish reuse its output.
        Assert.Contains("--no-build", (string)Inputs(steps[2])["arguments"]);
        Assert.Contains("--no-build", (string)Inputs(steps[3])["arguments"]);
        Assert.Equal("$(Build.ArtifactStagingDirectory)/app", (string)Inputs(steps[4])["targetPath"]);
    }

    [Fact]
    public void GeneratedYaml_HostedJobsRunOnLinux()
    {
        var root = Parse(_generator.Generate(FullDefinition()).Yaml);

        Assert.Equal("ubuntu-latest", (string)Assert.IsType<Dictionary<object, object>>(root["pool"])["vmImage"]);
        Assert.False(Assert.Single(JobsOf(StageNamed(root, "Build"))).ContainsKey("pool"));
    }

    [Fact]
    public void GeneratedYaml_SelfHostedBuildsUseThePool()
    {
        var definition = FullDefinition();
        definition.BuildAgent = BuildAgentType.SelfHosted;
        definition.PoolName = "OnPremAgents";
        var root = Parse(_generator.Generate(definition).Yaml);

        Assert.Equal("OnPremAgents", (string)Assert.IsType<Dictionary<object, object>>(root["pool"])["name"]);
    }

    [Fact]
    public void GeneratedYaml_KeyVaultRunsInsideEachDeployJob()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var root = Parse(yaml);

        Assert.DoesNotContain(StagesOf(root), s => (string)s["stage"] == "KeyVaultPreJob");
        Assert.Contains(DeploySteps(DeployJob(root)), s => s.GetValueOrDefault("task") as string == "AzureKeyVault@2");
    }

    [Fact]
    public void GeneratedYaml_IisDeploysToServersAndRollsBackOnFailure()
    {
        var definition = FullDefinition();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis, WebsiteName = "MyApp" };

        var root = Parse(_generator.Generate(definition).Yaml);
        var job = DeployJob(root);

        var environment = Assert.IsType<Dictionary<object, object>>(job["environment"]);
        Assert.Equal(PipelineYaml.Environment, (string)environment["name"]);
        Assert.Equal("VirtualMachine", (string)environment["resourceType"]);

        var steps = DeploySteps(job);
        var deploy = Assert.Single(steps, s => s.GetValueOrDefault("task") as string == "IISWebAppDeploymentOnMachineGroup@0");
        Assert.Equal("MyApp", (string)Inputs(deploy)["WebSiteName"]);
        Assert.Equal("$(Pipeline.Workspace)/drop", (string)Inputs(deploy)["Package"]);

        var backupIndex = steps.FindIndex(s => (s.GetValueOrDefault("displayName") as string ?? "").StartsWith("Back up"));
        Assert.InRange(backupIndex, 0, steps.IndexOf(deploy) - 1);
        Assert.Contains(@"Join-Path 'D:\backups' '${{ environment }}'", (string)steps[backupIndex]["powershell"]);

        var rollback = FailureSteps(job);
        Assert.Contains(rollback, s => (s.GetValueOrDefault("powershell") as string ?? "").Contains("Sync-Folder -Source $backup -Destination $target -Mirror"));
    }

    [Fact]
    public void GeneratedYaml_RollingStrategyUsesNativeRollingOnServers()
    {
        var definition = FullDefinition();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.WindowsService };
        definition.DeploymentStrategy = new DeploymentStrategyConfig { StrategyType = DeploymentStrategyType.Rolling, BatchSize = 2 };

        var job = DeployJob(Parse(_generator.Generate(definition).Yaml));
        var rolling = Assert.IsType<Dictionary<object, object>>(Strategy(job)["rolling"]);
        Assert.Equal("2", (string)rolling["maxParallel"]);
    }

    [Fact]
    public void GeneratedYaml_HealthCheckUrlIsPerEnvironment()
    {
        var definition = FullDefinition();
        definition.HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, Url = "https://myapp-{environment}.contoso.com/health" }
        };
        var root = Parse(_generator.Generate(definition).Yaml);

        var scripts = PowerShellScripts(DeployJob(root));
        Assert.Contains(scripts, s => s.Contains("$uri = 'https://myapp-${{ environment }}.contoso.com/health'"));
    }

    [Fact]
    public void GeneratedYaml_TriggerIncludesExcludesAndPaths()
    {
        var definition = FullDefinition();
        definition.Trigger = new TriggerConfig
        {
            IncludeBranches = new[] { "main", "release/*" },
            ExcludeBranches = new[] { "release/old" },
            PathFilters = new[] { "src/*", "!docs/*" }
        };
        var trigger = Assert.IsType<Dictionary<object, object>>(Parse(_generator.Generate(definition).Yaml)["trigger"]);
        var branches = Assert.IsType<Dictionary<object, object>>(trigger["branches"]);
        var paths = Assert.IsType<Dictionary<object, object>>(trigger["paths"]);

        Assert.Equal(new object[] { "main", "release/*" }, (List<object>)branches["include"]);
        Assert.Equal(new object[] { "release/old" }, (List<object>)branches["exclude"]);
        Assert.Equal(new object[] { "src/*" }, (List<object>)paths["include"]);
        Assert.Equal(new object[] { "docs/*" }, (List<object>)paths["exclude"]);
    }

    [Fact]
    public void GeneratedYaml_HasDefaultPool()
    {
        var root = Parse(_generator.Generate(FullDefinition()).Yaml);
        var pool = Assert.IsType<Dictionary<object, object>>(root["pool"]);
        Assert.Equal("ubuntu-latest", (string)pool["vmImage"]);
    }

    [Fact]
    public void GeneratedYaml_WizardDefaultsProduceAValidPipelineWithoutErrors()
    {
        var result = _generator.Generate(PipelineBuilder.Web.State.WizardState.CreateDefault());

        AssertValidPipeline(result.Yaml);
        Assert.DoesNotContain(result.ValidationResults, v => v.Severity == ValidationSeverity.Error);
    }

    private static Dictionary<object, object> StageNamed(Dictionary<object, object> root, string name) => PipelineYaml.Stage(root, name);

    private static string JobName(Dictionary<object, object> job) =>
        (job.GetValueOrDefault("job") ?? job.GetValueOrDefault("deployment")) as string ?? "";

    private static Dictionary<object, object> DeployJob(Dictionary<object, object> root) => PipelineYaml.DeployJob(root);

    private static Dictionary<object, object> Strategy(Dictionary<object, object> job) =>
        Assert.IsType<Dictionary<object, object>>(job["strategy"]);

    private static Dictionary<object, object> Hooks(Dictionary<object, object> job)
    {
        var strategy = Strategy(job);
        return Assert.IsType<Dictionary<object, object>>(strategy.GetValueOrDefault("runOnce") ?? strategy["rolling"]);
    }

    private static List<Dictionary<object, object>> DeploySteps(Dictionary<object, object> job)
    {
        var deploy = Assert.IsType<Dictionary<object, object>>(Hooks(job)["deploy"]);
        return StepsOf(deploy);
    }

    private static List<Dictionary<object, object>> FailureSteps(Dictionary<object, object> job)
    {
        var on = Assert.IsType<Dictionary<object, object>>(Hooks(job)["on"]);
        return StepsOf(Assert.IsType<Dictionary<object, object>>(on["failure"]));
    }

    private static List<Dictionary<object, object>> StepsOf(Dictionary<object, object> node) =>
        Assert.IsType<List<object>>(node["steps"]).Cast<Dictionary<object, object>>().ToList();

    private static Dictionary<object, object> Inputs(Dictionary<object, object> step) =>
        Assert.IsType<Dictionary<object, object>>(step["inputs"]);

    private static PipelineDefinition FullDefinition() => new()
    {
        Name = "orders",
        ProjectType = ProjectType.DotNet,
        BuildAgent = BuildAgentType.MicrosoftHosted,
        Environments = new[] { "test", "pre-prod", "prod" },
        VariableGroups = new[]
        {
            new VariableGroupConfig { Name = "vg-common", Scope = VariableGroupScope.Pipeline },
            new VariableGroupConfig { Name = "vg-prod", Scope = VariableGroupScope.Environment, EnvironmentName = "prod" }
        },
        KeyVault = new KeyVaultConfig { KeyVaultName = "kv-test" },
        Rollback = new RollbackConfig { Enabled = true, BackupPath = @"D:\backups", Target = RollbackTarget.Iis },
        HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = "https://myapp.contoso.com/health" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.IisAppPool, AppPoolName = "MyApp" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = "MyService" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.PortCheck, Port = 443 }
        },
        Notifications = new[]
        {
            new NotificationConfig { NotificationType = NotificationType.TeamsWebhook, NotifyOnSuccess = true, NotifyOnFailure = true },
            new NotificationConfig { NotificationType = NotificationType.CustomWebhook, NotifyOnFailure = true }
        }
    };

    private static Dictionary<object, object> Parse(string yaml) => PipelineYaml.Parse(yaml);

    private static List<Dictionary<object, object>> StagesOf(Dictionary<object, object> root) => PipelineYaml.Stages(root);

    private static List<Dictionary<object, object>> JobsOf(Dictionary<object, object> stage) => PipelineYaml.Jobs(stage);

    private static IEnumerable<string> DependenciesOf(Dictionary<object, object> node) => PipelineYaml.DependsOn(node);

    /// <summary>Finds every <c>powershell:</c> script anywhere in the document.</summary>
    private static IEnumerable<string> PowerShellScripts(object node)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach (var (key, value) in map)
                {
                    if (key is "powershell" && value is string script)
                        yield return script;
                    else
                        foreach (var nested in PowerShellScripts(value))
                            yield return nested;
                }
                break;
            case List<object> list:
                foreach (var item in list)
                foreach (var nested in PowerShellScripts(item))
                    yield return nested;
                break;
        }
    }
}
