using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Deployment;

/// <summary>Defaults for a deployment kind: no backup, no rollback, no extra checks.</summary>
public abstract class DeploymentKindHandler : IDeploymentKindHandler
{
    public abstract DeploymentKind Kind { get; }
    public abstract RollbackTarget? RollbackTarget { get; }
    public virtual bool RunsOnServers => true;
    public virtual bool NeedsRepositoryCheckout => false;
    public virtual bool SupportsRollback => true;

    public virtual ScriptShell Shell(DeploymentConfig deployment) => ScriptShell.PowerShell;

    public abstract IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath);

    public virtual IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) =>
        Array.Empty<string>();

    public virtual IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) =>
        Array.Empty<string>();

    public virtual IEnumerable<ValidationResult> Validate(PipelineDefinition definition) =>
        Array.Empty<ValidationResult>();

    protected static ValidationResult Finding(ValidationSeverity severity, string message, string field, string fix) => new()
    {
        Severity = severity,
        Message = message,
        AffectedField = field,
        SuggestedFix = fix
    };
}
