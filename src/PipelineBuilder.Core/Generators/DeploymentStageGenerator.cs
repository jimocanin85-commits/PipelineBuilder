using System.Text;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Generators;

/// <summary>
/// Generates one <c>Deploy_{env}</c> stage per environment. Each stage contains:
/// <list type="bullet">
/// <item>a <c>deployment</c> job targeting the Azure DevOps environment (so approvals and checks
/// apply) that loads Key Vault secrets, backs up, deploys and health-checks;</item>
/// <item>an <c>on: failure</c> hook on that job which rolls back on the same server.</item>
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

    public string Generate(PipelineDefinition definition)
    {
        var sb = new StringBuilder();
        var previousStage = "Build";
        foreach (var env in definition.Environments)
        {
            var envId = YamlBuilder.ToIdentifier(env);
            var stageName = $"Deploy_{envId}";
            // Production only deploys from the release branch.
            var condition = EnvironmentNames.IsProduction(env)
                ? $"and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/{definition.ReleaseBranch}'))"
                : "succeeded()";

            sb.Append($"- stage: {stageName}\n");
            sb.Append($"  displayName: {YamlBuilder.YamlString($"Deploy {env}")}\n");
            sb.Append($"  dependsOn: {previousStage}\n");
            sb.Append($"  condition: {condition}\n");

            var envGroups = definition.VariableGroups
                .Where(g => g.Scope == VariableGroupScope.Environment
                            && string.Equals(g.EnvironmentName, env, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (envGroups.Count > 0)
                sb.Append("  variables:\n").Append(_variableGroupService.GenerateStageVariables(envGroups)).Append('\n');

            sb.Append("  jobs:\n");
            sb.Append(DeploymentJob(definition, env, envId)).Append('\n');

            previousStage = stageName;
        }
        return sb.ToString().TrimEnd();
    }

    private string DeploymentJob(PipelineDefinition definition, string env, string envId)
    {
        var kind = _deploymentKinds.For(definition.Deployment.Kind);
        var serverResources = _deploymentKinds.UsesServerResources(definition);
        var packagePath = _artifactService.GetDeployPackagePath(definition.Artifact);

        var deploySteps = new List<string> { "    - download: none" }; // we download explicitly below
        if (kind.NeedsRepositoryCheckout)
            deploySteps.Add("    - checkout: self"); // e.g. Kubernetes manifests live in the repository
        deploySteps.AddRange(_artifactService.GenerateDownloadSteps(definition.Artifact, env));
        if (definition.KeyVault != null && !string.IsNullOrWhiteSpace(definition.KeyVault.KeyVaultName))
            deploySteps.Add(_keyVaultService.GeneratePreJobSteps(definition.KeyVault));
        deploySteps.AddRange(_deploymentKinds.GenerateBackupSteps(definition, env));

        deploySteps.AddRange(kind.GenerateDeploySteps(definition, env, packagePath));

        foreach (var hc in definition.HealthChecks.Where(h => h.Enabled))
            deploySteps.AddRange(_healthCheckService.GenerateHealthCheckSteps(ForEnvironment(hc, env), _deploymentKinds.ShellFor(definition)));

        var rollbackSteps = definition.DeploymentStrategy.RollbackOnFailure
            ? _deploymentKinds.GenerateRollbackSteps(definition, env)
            : Array.Empty<string>();

        var sb = new StringBuilder();
        sb.Append($"  - deployment: DeployTo{envId}\n");
        sb.Append($"    displayName: {YamlBuilder.YamlString($"Deploy to {env}")}\n");

        if (serverResources)
        {
            // Runs on the servers registered in the Azure DevOps environment.
            sb.Append("    environment:\n");
            sb.Append($"      name: {env}\n");
            sb.Append("      resourceType: VirtualMachine\n");
        }
        else
        {
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
        sb.Append(YamlBuilder.Indent(string.Join("\n", deploySteps), 6)).Append('\n');

        if (rollbackSteps.Count > 0)
        {
            sb.Append("        on:\n");
            sb.Append("          failure:\n");
            sb.Append("            steps:\n");
            sb.Append(YamlBuilder.Indent(string.Join("\n", rollbackSteps), 8)).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Replaces an <c>{environment}</c> token in the health check URL, so one check can target
    /// e.g. <c>https://myapp-{environment}.contoso.com/health</c> in every environment.
    /// </summary>
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
