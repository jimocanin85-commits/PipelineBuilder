using System.Text;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Generators;

/// <summary>
/// Generates the deploy stage, written once and repeated for each environment. The stage contains:
/// <list type="bullet">
/// <item>a <c>deployment</c> job targeting the Azure DevOps environment (so approvals and checks
/// apply) that loads Key Vault secrets, backs up, deploys and health-checks. It runs on each
/// server, or on the build agent, which then sends the scripts to the servers;</item>
/// <item>an <c>on: failure</c> hook on that job which rolls back on the same server;</item>
/// <item>for environments that ask for it, a job before it that waits for a person to approve.</item>
/// </list>
/// </summary>
public sealed class DeploymentStageGenerator
{
    private readonly IArtifactYamlService _artifactService;
    private readonly IHealthCheckYamlService _healthCheckService;
    private readonly IDeploymentKinds _deploymentKinds;
    private readonly IVariableGroupService _variableGroupService;
    private readonly IKeyVaultYamlService _keyVaultService;

    public DeploymentStageGenerator(
        IArtifactYamlService artifactService,
        IHealthCheckYamlService healthCheckService,
        IDeploymentKinds deploymentKinds,
        IVariableGroupService variableGroupService,
        IKeyVaultYamlService keyVaultService)
    {
        _artifactService = artifactService;
        _healthCheckService = healthCheckService;
        _deploymentKinds = deploymentKinds;
        _variableGroupService = variableGroupService;
        _keyVaultService = keyVaultService;
    }

    /// <summary>The environment's name inside the repeated deploy stage; Azure DevOps fills it in for each environment.</summary>
    public const string EnvironmentToken = "${{ environment }}";

    /// <summary>The deploy stage's name: stage names cannot contain hyphens, environment names can.</summary>
    public const string StageName = "Deploy_${{ replace(environment, '-', '_') }}";

    private const string ForEachEnvironment = "${{ each environment in parameters.environments }}:";

    /// <summary>The variable naming the environment's servers, e.g. <c>$(SERVERS_PROD)</c>. Variable names cannot contain hyphens.</summary>
    private const string ServersOfEnvironment = "$(" + ServerScript.ServersVariablePrefix + "${{ replace(environment, '-', '_') }})";

    /// <summary>A <c>dependsOn</c> list naming the build stage and every deploy stage.</summary>
    public const string AllStagesDependsOn =
        "  dependsOn:\n" +
        "  - Build\n" +
        "  - " + ForEachEnvironment + "\n" +
        "    - " + StageName;

    /// <summary>True when deployments run on the servers registered in each environment, not on the build agent.</summary>
    /// <summary>
    /// False for a pull request build. Such a build runs code nobody has reviewed yet, so it must not
    /// reach a server or a stage that holds secrets.
    /// </summary>
    public const string NotPullRequest = "ne(variables['Build.Reason'], 'PullRequest')";

    public bool RunsOnServers(PipelineDefinition definition) => _deploymentKinds.UsesServerResources(definition);

    /// <summary>
    /// One deploy stage, repeated for each environment in the <c>environments</c> parameter. The
    /// stages run in that order, because a stage without <c>dependsOn</c> follows the one before it.
    /// </summary>
    public string Generate(PipelineDefinition definition)
    {
        var stage = new StringBuilder();
        stage.Append("- stage: ").Append(StageName).Append('\n');
        stage.Append("  displayName: ").Append(YamlBuilder.YamlString($"Deploy {EnvironmentToken}")).Append('\n');

        // A pull request build runs code nobody has reviewed yet, so it never reaches a server.
        // Production also only deploys from the release branch.
        var production = definition.Environments.Where(EnvironmentNames.IsProduction).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        stage.Append("  # A pull request is built and tested, but never deployed.\n");
        if (production.Count > 0)
        {
            var names = string.Join(", ", production.Select(YamlBuilder.YamlString));
            stage.Append("  ${{ if notIn(environment, ").Append(names).Append(") }}:\n");
            stage.Append($"    condition: and(succeeded(), {NotPullRequest})\n");
            stage.Append($"  # Only the {definition.ReleaseBranch} branch deploys to {string.Join(" and ", production)}.\n");
            stage.Append("  ${{ if in(environment, ").Append(names).Append(") }}:\n");
            stage.Append($"    condition: and(succeeded(), {NotPullRequest}, eq(variables['Build.SourceBranch'], 'refs/heads/{definition.ReleaseBranch}'))\n");
        }
        else
        {
            stage.Append($"  condition: and(succeeded(), {NotPullRequest})\n");
        }

        // Variable groups that only one environment may read.
        var groupsByEnvironment = definition.VariableGroups
            .Where(g => g.Scope == VariableGroupScope.Environment && !string.IsNullOrWhiteSpace(g.EnvironmentName))
            .GroupBy(g => g.EnvironmentName!, StringComparer.OrdinalIgnoreCase)
            .Where(g => definition.Environments.Contains(g.Key, StringComparer.OrdinalIgnoreCase));
        foreach (var groups in groupsByEnvironment)
        {
            stage.Append("  ${{ if eq(environment, ").Append(YamlBuilder.YamlString(groups.Key)).Append(") }}:\n");
            stage.Append("    variables:\n");
            stage.Append(YamlBuilder.Indent(_variableGroupService.GenerateStageVariables(groups.ToList()), 2)).Append('\n');
        }

        stage.Append("  jobs:\n");
        stage.Append(ApprovalGate.Job(definition, EnvironmentToken));
        stage.Append(DeploymentJob(definition)).Append('\n');

        return "# Deploy: repeated for each environment, one after the other. Nothing is built again.\n" +
               "- " + ForEachEnvironment + "\n" + YamlBuilder.Indent(stage.ToString().TrimEnd(), 2);
    }

    private string DeploymentJob(PipelineDefinition definition)
    {
        const string env = EnvironmentToken;
        var kind = _deploymentKinds.For(definition.Deployment.Kind);
        var serverResources = _deploymentKinds.UsesServerResources(definition);
        var fromAgent = _deploymentKinds.DeploysFromAgentToServers(definition);
        var shell = _deploymentKinds.ShellFor(definition);
        var packagePath = _artifactService.GetDeployPackagePath(definition.Artifact);

        var deploySteps = new List<string> { "    - download: none" }; // we download explicitly below
        if (kind.NeedsRepositoryCheckout)
            deploySteps.Add("    - checkout: self"); // e.g. Kubernetes manifests live in the repository
        deploySteps.AddRange(_artifactService.GenerateDownloadSteps(definition.Artifact));
        if (definition.KeyVault != null && !string.IsNullOrWhiteSpace(definition.KeyVault.KeyVaultName))
            deploySteps.Add(_keyVaultService.GeneratePreJobSteps(definition.KeyVault));
        if (!string.IsNullOrWhiteSpace(definition.Deployment.SettingsFiles) && definition.Artifact.ArtifactType != ArtifactType.DockerImage)
            deploySteps.Add(SettingsForEnvironment(definition.Deployment.SettingsFiles.Trim(), packagePath));
        if (fromAgent && definition.Artifact.ArtifactType != ArtifactType.DockerImage)
        {
            // The package was downloaded to the agent; the scripts run on the servers.
            deploySteps.Add(ServerScript.CopyPackageStep(definition.Deployment, shell, packagePath));
            packagePath = ServerScript.RemotePackagePath(shell, packagePath);
        }
        deploySteps.AddRange(_deploymentKinds.GenerateBackupSteps(definition, env));

        deploySteps.AddRange(kind.GenerateDeploySteps(definition, env, packagePath));

        foreach (var hc in definition.HealthChecks.Where(h => h.Enabled))
            deploySteps.AddRange(_healthCheckService.GenerateHealthCheckSteps(ForEnvironment(hc, env), shell, fromAgent ? definition.Deployment : null));

        var rollbackSteps = definition.DeploymentStrategy.RollbackOnFailure
            ? _deploymentKinds.GenerateRollbackSteps(definition, env)
            : Array.Empty<string>();

        var sb = new StringBuilder();
        sb.Append("  - deployment: Deploy\n");
        sb.Append($"    displayName: {YamlBuilder.YamlString($"Deploy to {env}")}\n");
        sb.Append(ApprovalGate.DependsOn(definition));

        if (serverResources)
        {
            sb.Append("    # Runs on each server registered in this environment.\n");
            sb.Append("    environment:\n");
            sb.Append($"      name: {env}\n");
            sb.Append("      resourceType: VirtualMachine\n");
        }
        else
        {
            if (fromAgent)
            {
                sb.Append("    # Runs on the build agent, which connects to this environment's servers.\n");
                sb.Append("    # They are named in a pipeline variable per environment, e.g. SERVERS_PROD = web01, web02.\n");
                sb.Append("    variables:\n");
                sb.Append($"      {ServerScript.ServersVariable}: {ServersOfEnvironment}\n");
            }
            if (!string.IsNullOrWhiteSpace(definition.Deployment.AgentPool))
            {
                sb.Append("    # This job needs another agent than the build.\n");
                sb.Append("    pool:\n");
                sb.Append($"      name: {YamlBuilder.YamlString(definition.Deployment.AgentPool.Trim())}\n");
            }
            sb.Append("    # Approvals and checks are set on this environment in Azure DevOps.\n");
            sb.Append($"    environment: {env}\n");
        }

        var useRolling = serverResources && definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.Rolling;
        sb.Append("    strategy:\n");
        if (useRolling)
        {
            sb.Append("      rolling:\n");
            sb.Append($"        maxParallel: {Math.Max(1, definition.DeploymentStrategy.BatchSize)}\n");
        }
        else
        {
            sb.Append("      runOnce:\n");
        }
        sb.Append("        deploy:\n");
        sb.Append("          steps:\n");
        sb.Append(YamlBuilder.Indent(string.Join("\n", deploySteps), 8)).Append('\n');

        if (rollbackSteps.Count > 0)
        {
            sb.Append("        # If a step above fails, the previous version is put back.\n");
            sb.Append("        on:\n");
            sb.Append("          failure:\n");
            sb.Append("            steps:\n");
            sb.Append(YamlBuilder.Indent(string.Join("\n", rollbackSteps), 10)).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Replaces an <c>{environment}</c> token in the health check URL, so one check can target
    /// e.g. <c>https://myapp-{environment}.contoso.com/health</c> in every environment.
    /// </summary>
    /// <summary>
    /// Build once, configure per environment: the same package gets this environment's values before
    /// it is deployed. Runs after Key Vault, so its secrets can be used too.
    /// </summary>
    private static string SettingsForEnvironment(string files, string packagePath) =>
        "    # The settings get this environment's values: a variable named like a setting, e.g. ConnectionStrings.Default, replaces it.\n" +
        YamlBuilder.Task("FileTransform@1", new Dictionary<string, string>
        {
            ["folderPath"] = packagePath,
            ["fileType"] = "json",
            ["targetFiles"] = files
        }, $"Fill in the settings for {EnvironmentToken}");

    private static HealthCheckConfig ForEnvironment(HealthCheckConfig hc, string env) => new()
    {
        Enabled = hc.Enabled,
        HealthCheckType = hc.HealthCheckType,
        Url = hc.Url?.Replace("{environment}", env, StringComparison.OrdinalIgnoreCase),
        ExpectedStatusCode = hc.ExpectedStatusCode,
        TimeoutSeconds = hc.TimeoutSeconds,
        RetryCount = hc.RetryCount,
        ServiceName = hc.ServiceName,
        AppPoolName = hc.AppPoolName,
        Port = hc.Port,
        CustomScript = hc.CustomScript
    };
}
