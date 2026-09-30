using System.Text;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Generators;

/// <summary>
/// Generates one <c>Deploy_{env}</c> stage per environment. Each stage contains:
/// <list type="bullet">
/// <item>an optional <c>Infrastructure</c> job (IaC for that environment), which runs first;</item>
/// <item>a <c>deployment</c> job targeting the Azure DevOps environment (so approvals and checks
/// apply) that loads Key Vault secrets, backs up, deploys and health-checks;</item>
/// <item>an <c>on: failure</c> hook on that job which rolls back on the same server.</item>
/// </list>
/// </summary>
public sealed class DeploymentStageGenerator
{
    private readonly IArtifactYamlService _artifactService;
    private readonly IHealthCheckYamlService _healthCheckService;
    private readonly IRollbackYamlService _rollbackService;
    private readonly IDeploymentStrategyService _strategyService;
    private readonly IVariableGroupService _variableGroupService;
    private readonly IDeploymentStepService _deploymentStepService;
    private readonly IKeyVaultYamlService _keyVaultService;
    private readonly IIacYamlService _iacService;

    public DeploymentStageGenerator(
        IArtifactYamlService artifactService,
        IHealthCheckYamlService healthCheckService,
        IRollbackYamlService rollbackService,
        IDeploymentStrategyService strategyService,
        IVariableGroupService variableGroupService,
        IDeploymentStepService deploymentStepService,
        IKeyVaultYamlService keyVaultService,
        IIacYamlService iacService)
    {
        _artifactService = artifactService;
        _healthCheckService = healthCheckService;
        _rollbackService = rollbackService;
        _strategyService = strategyService;
        _variableGroupService = variableGroupService;
        _deploymentStepService = deploymentStepService;
        _keyVaultService = keyVaultService;
        _iacService = iacService;
    }

    /// <summary>
    /// True when deployments run on servers registered in the Azure DevOps environment
    /// (VM resources) rather than on a pipeline agent pool.
    /// </summary>
    public static bool UsesServerResources(PipelineDefinition definition) =>
        definition.Deployment.Kind != DeploymentKind.Kubernetes // deploys to the cluster, from a pipeline agent
        && (definition.DeploymentTarget == DeploymentTarget.OnPrem
            || (definition.DeploymentTarget == DeploymentTarget.Hybrid && definition.Deployment.IsServerDeployment));

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
            var hasInfrastructure = definition.IaC != null;
            if (hasInfrastructure)
                sb.Append(InfrastructureJob(definition, env)).Append('\n');
            sb.Append(DeploymentJob(definition, env, envId, hasInfrastructure)).Append('\n');

            previousStage = stageName;
        }
        return sb.ToString().TrimEnd();
    }

    private string InfrastructureJob(PipelineDefinition definition, string env)
    {
        var iac = definition.IaC!;
        var steps = string.Join("\n", _iacService.GenerateIacSteps(iac, env));
        var pool = PoolConfigurationHelper.GeneratePoolConfiguration(definition.BuildAgent, definition.PoolName);

        if (!iac.ApplyOnApproval)
        {
            return $"""
  - job: Infrastructure
    displayName: {YamlBuilder.YamlString($"Infrastructure ({env})")}
    pool:
      {pool}
    steps:
{YamlBuilder.Indent(steps, 4)}
""";
        }

        // A deployment job targeting the environment, so its approvals gate the infrastructure change.
        return $"""
  - deployment: Infrastructure
    displayName: {YamlBuilder.YamlString($"Infrastructure ({env})")}
    pool:
      {pool}
    environment: {env}
    strategy:
      runOnce:
        deploy:
          steps:
          - checkout: self
{YamlBuilder.Indent(steps, 6)}
""";
    }

    private string DeploymentJob(PipelineDefinition definition, string env, string envId, bool dependsOnInfrastructure)
    {
        var serverResources = UsesServerResources(definition);
        var packagePath = _artifactService.GetDeployPackagePath(definition.Artifact);

        var deploySteps = new List<string> { "    - download: none" }; // we download explicitly below
        if (definition.Deployment.Kind == DeploymentKind.Kubernetes)
            deploySteps.Add("    - checkout: self"); // the manifests live in the repository
        deploySteps.AddRange(_artifactService.GenerateDownloadSteps(definition.Artifact, env));
        if (definition.KeyVault != null && !string.IsNullOrWhiteSpace(definition.KeyVault.KeyVaultName))
            deploySteps.Add(_keyVaultService.GeneratePreJobSteps(definition.KeyVault));
        deploySteps.AddRange(_rollbackService.GenerateBackupSteps(definition.Rollback, definition.Deployment, env));

        var deployTask = string.Join("\n", _deploymentStepService.GenerateDeploySteps(definition, env, packagePath));
        deploySteps.AddRange(_strategyService.GenerateStrategySteps(
            definition.DeploymentStrategy, env, deployTask, definition.Deployment.WebAppNameOrDefault, definition.AzureServiceConnection));

        foreach (var hc in definition.HealthChecks.Where(h => h.Enabled))
            deploySteps.AddRange(_healthCheckService.GenerateHealthCheckSteps(ForEnvironment(hc, env)));

        var rollbackSteps = definition.DeploymentStrategy.RollbackOnFailure
            ? _rollbackService.GenerateRollbackSteps(definition.Rollback, definition.Deployment, env)
            : Array.Empty<string>();

        var sb = new StringBuilder();
        sb.Append($"  - deployment: DeployTo{envId}\n");
        sb.Append($"    displayName: {YamlBuilder.YamlString($"Deploy to {env}")}\n");
        if (dependsOnInfrastructure)
            sb.Append("    dependsOn: Infrastructure\n");

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
