using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Abstractions;

public interface IVariableGroupService
{
    string GeneratePipelineVariables(PipelineDefinition definition);
    string GenerateStageVariables(IReadOnlyList<VariableGroupConfig> groups);
    IReadOnlyList<ValidationResult> Validate(IReadOnlyList<VariableGroupConfig> groups, GovernancePolicyConfig? governance);
}

public interface IKeyVaultYamlService
{
    string GeneratePreJobSteps(KeyVaultConfig config);
    IReadOnlyList<ValidationResult> Validate(KeyVaultConfig? config);
}

public interface IArtifactYamlService
{
    /// <summary>Steps that produce the deployable output (e.g. <c>dotnet publish</c>) before it is packaged.</summary>
    IReadOnlyList<string> GenerateBuildOutputSteps(PipelineDefinition definition);
    IReadOnlyList<string> GeneratePublishSteps(ArtifactConfig config);
    IReadOnlyList<string> GenerateDownloadSteps(ArtifactConfig config, string? environment = null);

    /// <summary>Path of the downloaded package (folder or zip file) inside a deployment job.</summary>
    string GetDeployPackagePath(ArtifactConfig config);
}

/// <summary>
/// Everything about one deployment kind in one place: its deploy, backup and rollback steps, what it
/// needs from the pipeline, and its own checks. Adding a deployment kind means adding one handler.
/// </summary>
public interface IDeploymentKindHandler
{
    DeploymentKind Kind { get; }

    /// <summary>The rollback target this kind backs up and restores; null when the user chooses one (Custom).</summary>
    RollbackTarget? RollbackTarget { get; }

    /// <summary>Deploys to servers registered in an Azure DevOps environment when the target is on-premises or hybrid.</summary>
    bool RunsOnServers { get; }

    /// <summary>Always deploys from a pipeline agent, even on-premises (e.g. to a Kubernetes cluster).</summary>
    bool DeploysFromAgentOnly { get; }

    /// <summary>The deploy job checks out the repository (e.g. for Kubernetes manifests).</summary>
    bool NeedsRepositoryCheckout { get; }

    /// <summary>Works with the slot-swap strategy.</summary>
    bool SupportsSlotSwap { get; }

    /// <summary>Steps that deploy the downloaded package to the target.</summary>
    IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath);

    /// <summary>Steps run before deploying to snapshot the current version (rollback enabled).</summary>
    IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment);

    /// <summary>Steps run in the deploy job's <c>on: failure</c> hook to restore the snapshot.</summary>
    IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment);

    /// <summary>Checks that only apply to this deployment kind.</summary>
    IEnumerable<ValidationResult> Validate(PipelineDefinition definition);
}

/// <summary>The registered deployment kinds, and the decisions that depend on the selected one.</summary>
public interface IDeploymentKinds
{
    IReadOnlyList<IDeploymentKindHandler> All { get; }

    /// <summary>The handler for a kind; the Custom handler for a kind without one.</summary>
    IDeploymentKindHandler For(DeploymentKind kind);

    /// <summary>True when deployments run on servers registered in the environment rather than on an agent pool.</summary>
    bool UsesServerResources(PipelineDefinition definition);

    /// <summary>Backup steps for the deploy job, from the kind's own handler or, for Custom, the chosen rollback target's.</summary>
    IReadOnlyList<string> GenerateBackupSteps(PipelineDefinition definition, string environment);

    /// <summary>Rollback steps for the <c>on: failure</c> hook: the custom rollback script, or the handler's steps.</summary>
    IReadOnlyList<string> GenerateRollbackSteps(PipelineDefinition definition, string environment);
}

public interface IHealthCheckYamlService
{
    IReadOnlyList<string> GenerateHealthCheckSteps(HealthCheckConfig config);
}

public interface INotificationYamlService
{
    IReadOnlyList<string> GenerateNotificationSteps(NotificationConfig config, bool succeeded);
}

public interface IIacYamlService
{
    IReadOnlyList<string> GenerateIacSteps(InfrastructureAsCodeConfig config, string environment);
}

public interface IDeploymentStrategyService
{
    IReadOnlyList<string> GenerateStrategySteps(DeploymentStrategyConfig config, string environment, string deploymentTaskYaml, string webAppName = "$(WEBAPP_NAME)", string azureServiceConnection = "$(AZURE_SERVICE_CONNECTION)");
    string GetStrategyNote(DeploymentStrategyConfig config);
}

public interface IGovernanceValidationService
{
    IReadOnlyList<ValidationResult> Validate(PipelineDefinition definition, string yaml);
}

public interface IRepoScannerService
{
    RepoScanResult ScanFileList(IReadOnlyList<string> relativePaths);
}

public interface IYamlExplanationService
{
    IReadOnlyList<YamlBlockExplanation> ExplainYaml(string yaml);
}

public interface IAgentDiagnosticsService
{
    string GenerateDiagnosticScript(AgentDiagnosticConfig config);
}

public interface ISecretsGovernanceService
{
    IReadOnlyList<SecretGovernanceResult> ScanYaml(string yaml);
    IReadOnlyList<ValidationResult> ToValidationResults(IReadOnlyList<SecretGovernanceResult> results);
}

public interface IEnvironmentYamlService
{
    string GetApprovalUiNote();
}

public interface ITemplateMarketplaceService
{
    IReadOnlyList<PipelineTemplate> GetAllTemplates();
    PipelineTemplate? GetById(string id);

    /// <summary>
    /// Applies a template's settings (project type, target, artifact, deployment kind, stages)
    /// to <paramref name="definition"/>. Returns false if the template id is unknown.
    /// </summary>
    bool ApplyTo(string templateId, PipelineDefinition definition);
}

public interface IPipelineGeneratorService
{
    GeneratedPipeline Generate(PipelineDefinition definition);
}
