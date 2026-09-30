using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Core.Abstractions;

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

public interface IRollbackYamlService
{
    /// <summary>Steps run in the deploy job before deploying, to snapshot the current version.</summary>
    IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment);

    /// <summary>Steps run in the deploy job's <c>on: failure</c> hook to restore the snapshot.</summary>
    IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment);

    /// <summary>The rollback target in effect: follows the deployment kind unless that is Custom.</summary>
    RollbackTarget ResolveTarget(RollbackConfig config, DeploymentConfig deployment);
}

public interface IDeploymentStepService
{
    /// <summary>Steps that deploy the downloaded package to the target.</summary>
    IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath);
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
