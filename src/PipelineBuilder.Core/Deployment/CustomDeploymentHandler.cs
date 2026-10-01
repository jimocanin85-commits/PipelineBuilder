using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Runs the user's own deploy script (or a placeholder). The user chooses the rollback target, and
/// backup and rollback come from that target's handler.
/// </summary>
public sealed class CustomDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.Custom;
    public override RollbackTarget? RollbackTarget => null;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) => new[]
    {
        YamlBuilder.PowerShellStep(
            definition.Deployment.CustomScript
                ?? $"Write-Warning 'No deployment kind selected: add your deploy commands here. The package is at {packagePath}'",
            $"Deploy to {environment}")
    };

    public override IEnumerable<ValidationResult> Validate(PipelineDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Deployment.CustomScript))
        {
            yield return Finding(ValidationSeverity.Warning,
                "No deployment kind is selected, so the deploy step is only a placeholder.",
                nameof(PipelineDefinition.Deployment),
                "Choose IIS, Windows service, file share, App Service or Docker, or provide a custom deploy script.");
        }
    }
}
