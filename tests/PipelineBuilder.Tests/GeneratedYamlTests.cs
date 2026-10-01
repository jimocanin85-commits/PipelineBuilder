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
        foreach (var target in Enum.GetValues<DeploymentTarget>())
            yield return new object[] { strategy, artifact, kind, target };
    }

    public static IEnumerable<object[]> AllRollbackTargets() =>
        Enum.GetValues<RollbackTarget>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllOptionCombinations))]
    public void GeneratedYaml_IsValidForEveryOptionCombination(
        DeploymentStrategyType strategy, ArtifactType artifact, DeploymentKind kind, DeploymentTarget target)
    {
        var definition = FullDefinition();
        definition.DeploymentStrategy = new DeploymentStrategyConfig { StrategyType = strategy };
        definition.Artifact = new ArtifactConfig { ArtifactType = artifact, ArtifactName = "drop" };
        definition.Deployment = new DeploymentConfig { Kind = kind };
        definition.DeploymentTarget = target;

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
        Assert.All(names, n => Assert.Matches(StageIdentifier, n));

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
        Assert.Contains(scripts, s => s.Contains("#$(Build.BuildNumber)"));
    }

    [Fact]
    public void GeneratedYaml_HyphenatedEnvironmentGetsValidStageName()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var stages = StagesOf(Parse(yaml));

        var preProd = Assert.Single(stages, s => (string)s["stage"] == "Deploy_pre_prod");
        var job = DeployJob(preProd);
        Assert.Equal("pre-prod", (string)job["environment"]);
        Assert.Equal("DeployTopre_prod", (string)job["deployment"]);
    }

    [Fact]
    public void GeneratedYaml_NotificationsAreSplitBySuccessAndFailure()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var stages = StagesOf(Parse(yaml));

        var success = Assert.Single(stages, s => (string)s["stage"] == "Notify_Success");
        Assert.Equal("succeeded()", (string)success["condition"]);
        Assert.Contains("succeeded", string.Join("\n", PowerShellScripts(success)));
        Assert.DoesNotContain(" failed", string.Join("\n", PowerShellScripts(success)));

        var failure = Assert.Single(stages, s => (string)s["stage"] == "Notify_Failure");
        Assert.Equal("failed()", (string)failure["condition"]);
        var failureDependencies = DependenciesOf(failure).ToList();
        Assert.Contains("Build", failureDependencies);
        Assert.Contains("Deploy_prod", failureDependencies);
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
    public void GeneratedYaml_HostedBuildsRunOnLinuxAndDeploysOnWindows()
    {
        var root = Parse(_generator.Generate(FullDefinition()).Yaml);

        var buildPool = Assert.IsType<Dictionary<object, object>>(Assert.Single(JobsOf(StageNamed(root, "Build")))["pool"]);
        Assert.Equal("ubuntu-latest", (string)buildPool["vmImage"]);
        Assert.Equal("windows-latest", (string)Assert.IsType<Dictionary<object, object>>(root["pool"])["vmImage"]);
    }

    [Fact]
    public void GeneratedYaml_SelfHostedBuildsUseThePool()
    {
        var definition = FullDefinition();
        definition.BuildAgent = BuildAgentType.SelfHosted;
        definition.PoolName = "OnPremAgents";
        var root = Parse(_generator.Generate(definition).Yaml);

        var buildPool = Assert.IsType<Dictionary<object, object>>(Assert.Single(JobsOf(StageNamed(root, "Build")))["pool"]);
        Assert.Equal("OnPremAgents", (string)buildPool["name"]);
    }

    [Fact]
    public void GeneratedYaml_KeyVaultRunsInsideEachDeployJob()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var root = Parse(yaml);

        Assert.DoesNotContain(StagesOf(root), s => (string)s["stage"] == "KeyVaultPreJob");
        foreach (var env in new[] { "test", "pre_prod", "prod" })
        {
            var job = DeployJob(StageNamed(root, $"Deploy_{env}"));
            Assert.Contains(DeploySteps(job), s => s.GetValueOrDefault("task") as string == "AzureKeyVault@2");
        }
    }

    [Fact]
    public void GeneratedYaml_InfrastructureRunsPerEnvironmentBeforeDeploy()
    {
        var yaml = _generator.Generate(FullDefinition()).Yaml;
        var root = Parse(yaml);

        Assert.DoesNotContain(StagesOf(root), s => (string)s["stage"] == "Infrastructure");
        foreach (var env in new[] { "test", "pre_prod", "prod" })
        {
            var stage = StageNamed(root, $"Deploy_{env}");
            var infra = Assert.Single(JobsOf(stage), j => JobName(j) == "Infrastructure");
            Assert.Equal("Infrastructure", Assert.Single(DependenciesOf(DeployJob(stage))));

            var steps = DeploySteps(infra);
            Assert.Equal("self", (string)steps[0]["checkout"]);
            Assert.Contains(steps, s => s.GetValueOrDefault("task") as string == "TerraformTaskV4@4"
                                        && (string)Inputs(s)["command"] == "apply");
        }
    }

    [Fact]
    public void GeneratedYaml_OnPremIisDeploysToServersAndRollsBackOnFailure()
    {
        var definition = FullDefinition();
        definition.DeploymentTarget = DeploymentTarget.OnPrem;
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis, WebsiteName = "MyApp" };

        var root = Parse(_generator.Generate(definition).Yaml);
        var job = DeployJob(StageNamed(root, "Deploy_prod"));

        var environment = Assert.IsType<Dictionary<object, object>>(job["environment"]);
        Assert.Equal("prod", (string)environment["name"]);
        Assert.Equal("VirtualMachine", (string)environment["resourceType"]);

        var steps = DeploySteps(job);
        var deploy = Assert.Single(steps, s => s.GetValueOrDefault("task") as string == "IISWebAppDeploymentOnMachineGroup@0");
        Assert.Equal("MyApp", (string)Inputs(deploy)["WebSiteName"]);
        Assert.Equal("$(Pipeline.Workspace)/drop", (string)Inputs(deploy)["Package"]);

        var backupIndex = steps.FindIndex(s => (s.GetValueOrDefault("displayName") as string ?? "").StartsWith("Back up"));
        Assert.InRange(backupIndex, 0, steps.IndexOf(deploy) - 1);
        Assert.Contains(@"Join-Path 'D:\backups' 'prod'", (string)steps[backupIndex]["powershell"]);

        var rollback = FailureSteps(job);
        Assert.Contains(rollback, s => (s.GetValueOrDefault("powershell") as string ?? "").Contains("Sync-Folder -Source $backup -Destination $target -Mirror"));
    }

    [Fact]
    public void GeneratedYaml_RollingStrategyUsesNativeRollingOnServers()
    {
        var definition = FullDefinition();
        definition.DeploymentTarget = DeploymentTarget.OnPrem;
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.WindowsService };
        definition.DeploymentStrategy = new DeploymentStrategyConfig { StrategyType = DeploymentStrategyType.Rolling, BatchSize = 2 };

        var job = DeployJob(StageNamed(Parse(_generator.Generate(definition).Yaml), "Deploy_prod"));
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

        foreach (var (stage, env) in new[] { ("Deploy_test", "test"), ("Deploy_prod", "prod") })
        {
            var scripts = PowerShellScripts(DeployJob(StageNamed(root, stage)));
            Assert.Contains(scripts, s => s.Contains($"$uri = 'https://myapp-{env}.contoso.com/health'"));
        }
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
        Assert.Equal("windows-latest", (string)pool["vmImage"]);
    }

    [Fact]
    public void GeneratedYaml_WizardDefaultsProduceAValidPipelineWithoutErrors()
    {
        var result = _generator.Generate(PipelineBuilder.Web.State.WizardState.CreateDefault());

        AssertValidPipeline(result.Yaml);
        Assert.DoesNotContain(result.ValidationResults, v => v.Severity == ValidationSeverity.Error);
    }

    private static Dictionary<object, object> StageNamed(Dictionary<object, object> root, string name) =>
        Assert.Single(StagesOf(root), s => (string)s["stage"] == name);

    private static string JobName(Dictionary<object, object> job) =>
        (job.GetValueOrDefault("job") ?? job.GetValueOrDefault("deployment")) as string ?? "";

    private static Dictionary<object, object> DeployJob(Dictionary<object, object> stage) =>
        Assert.Single(JobsOf(stage), j => JobName(j).StartsWith("DeployTo", StringComparison.Ordinal));

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
        Name = "enterprise-pipeline",
        ProjectType = ProjectType.DotNet,
        BuildAgent = BuildAgentType.MicrosoftHosted,
        DeploymentTarget = DeploymentTarget.Hybrid,
        Environments = new[] { "test", "pre-prod", "prod" },
        VariableGroups = new[]
        {
            new VariableGroupConfig { Name = "vg-common", Scope = VariableGroupScope.Pipeline },
            new VariableGroupConfig { Name = "vg-prod", Scope = VariableGroupScope.Environment, EnvironmentName = "prod" }
        },
        KeyVault = new KeyVaultConfig { KeyVaultName = "kv-test" },
        IaC = new InfrastructureAsCodeConfig { Tool = IaCTool.Terraform, WorkingDirectory = "infra" },
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

    private static Dictionary<object, object> Parse(string yaml)
    {
        var parsed = new DeserializerBuilder().Build().Deserialize<object>(yaml);
        return Assert.IsType<Dictionary<object, object>>(parsed);
    }

    private static List<Dictionary<object, object>> StagesOf(Dictionary<object, object> root) =>
        Assert.IsType<List<object>>(root["stages"]).Cast<Dictionary<object, object>>().ToList();

    private static List<Dictionary<object, object>> JobsOf(Dictionary<object, object> stage) =>
        Assert.IsType<List<object>>(stage["jobs"]).Cast<Dictionary<object, object>>().ToList();

    private static IEnumerable<string> DependenciesOf(Dictionary<object, object> stage) =>
        stage.TryGetValue("dependsOn", out var value) switch
        {
            false => Array.Empty<string>(),
            true when value is string single => new[] { single },
            true when value is List<object> list => list.Cast<string>(),
            _ => Array.Empty<string>()
        };

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
