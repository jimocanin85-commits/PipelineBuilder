using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Runs the user's own deploy script (or a reminder to write one) on the registered servers. The user chooses
/// the rollback target, and backup and rollback come from that target's handler.
/// </summary>
public sealed class CustomDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.Custom;
    public override RollbackTarget? RollbackTarget => null;

    public override ScriptShell Shell(DeploymentConfig deployment) =>
        deployment.ServerOs == ServerOs.Linux ? ScriptShell.Bash : ScriptShell.PowerShell;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var shell = Shell(definition.Deployment);
        var placeholder = shell == ScriptShell.Bash
            ? $"echo '##vso[task.logissue type=warning]No deploy script yet: add your deploy commands here. The package is at {packagePath}'"
            : $"Write-Warning 'No deploy script yet: add your deploy commands here. The package is at {packagePath}'";

        return new[] { ServerScript.Step(definition.Deployment, shell, definition.Deployment.CustomScript ?? placeholder, $"Deploy to {environment}") };
    }

    public override IEnumerable<ValidationResult> Validate(PipelineDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Deployment.CustomScript))
        {
            yield return Finding(ValidationSeverity.Warning,
                "The deploy script is empty, so the deploy step only prints a reminder.",
                nameof(PipelineDefinition.Deployment),
                "Write your deploy commands in 'Deploy script'.");
        }
    }
}
