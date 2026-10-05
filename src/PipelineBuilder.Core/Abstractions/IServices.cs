using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Abstractions;

public interface IVariableGroupService
{
    string GeneratePipelineVariables(PipelineDefinition definition);
    string GenerateStageVariables(IReadOnlyList<VariableGroupConfig> groups);
    IReadOnlyList<ValidationResult> Validate(IReadOnlyList<VariableGroupConfig> groups);
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

    /// <summary>
    /// Deploys to your own servers: its scripts run on each server. False means the deploy job only
    /// talks to something else from a pipeline agent, e.g. a Kubernetes cluster.
    /// </summary>
    bool RunsOnServers { get; }

    /// <summary>The deploy job checks out the repository (e.g. for Kubernetes manifests).</summary>
    bool NeedsRepositoryCheckout { get; }

    /// <summary>False when the kind leaves rollback to something else, e.g. an Ansible playbook.</summary>
    bool SupportsRollback { get; }

    /// <summary>The shell scripts run in where this kind deploys: PowerShell on Windows, bash on Linux servers.</summary>
    ScriptShell Shell(DeploymentConfig deployment);

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

    /// <summary>True when an agent on each server deploys: the servers are registered in the environment (Virtual machine resources).</summary>
    bool UsesServerResources(PipelineDefinition definition);

    /// <summary>True when the build agent deploys to your servers over the network (WinRM or SSH).</summary>
    bool DeploysFromAgentToServers(PipelineDefinition definition);

    /// <summary>The shell for script steps in the deploy job (health checks, custom scripts).</summary>
    ScriptShell ShellFor(PipelineDefinition definition);

    /// <summary>Backup steps for the deploy job, from the kind's own handler or, for Custom, the chosen rollback target's.</summary>
    IReadOnlyList<string> GenerateBackupSteps(PipelineDefinition definition, string environment);

    /// <summary>Rollback steps for the <c>on: failure</c> hook: the custom rollback script, or the handler's steps.</summary>
    IReadOnlyList<string> GenerateRollbackSteps(PipelineDefinition definition, string environment);
}

public interface IHealthCheckYamlService
{
    /// <summary>
    /// The check as steps. Pass <paramref name="deployment"/> when the check must run on the servers
    /// a build agent deploys to; without it the check runs where the job runs.
    /// </summary>
    IReadOnlyList<string> GenerateHealthCheckSteps(HealthCheckConfig config, ScriptShell shell = ScriptShell.PowerShell, DeploymentConfig? deployment = null);
}

public interface INotificationYamlService
{
    IReadOnlyList<string> GenerateNotificationSteps(NotificationConfig config, bool succeeded);
}

/// <summary>The one entry point for validation findings: a chain of rules, each with an id.</summary>
public interface IPipelineValidator
{
    /// <summary>Problems in the settings that block generation. Needs no generated YAML, so it is always current.</summary>
    IReadOnlyList<ValidationResult> ValidateInput(PipelineDefinition definition);

    /// <summary>Findings about the generated pipeline: deployment advice, variable groups, secrets, Key Vault.</summary>
    IReadOnlyList<ValidationResult> ValidateGenerated(PipelineDefinition definition, string yaml);
}

public interface ISecretsGovernanceService
{
    IReadOnlyList<SecretGovernanceResult> ScanYaml(string yaml);
    IReadOnlyList<ValidationResult> ToValidationResults(IReadOnlyList<SecretGovernanceResult> results);
}

public interface ITemplateCatalogue
{
    IReadOnlyList<PipelineTemplate> GetAllTemplates();
    PipelineTemplate? GetById(string id);

    /// <summary>
    /// Applies a template's settings (project type, artifact, deployment kind, environments)
    /// to <paramref name="definition"/>. Returns false if the template id is unknown.
    /// </summary>
    bool ApplyTo(string templateId, PipelineDefinition definition);
}

public interface IPipelineGeneratorService
{
    GeneratedPipeline Generate(PipelineDefinition definition);
}
