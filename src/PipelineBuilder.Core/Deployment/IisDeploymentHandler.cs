using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>Deploys to an IIS website on the registered servers, with folder backup and rollback.</summary>
public sealed class IisDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.Iis;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.Iis;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) => new[]
    {
        YamlBuilder.Task("IISWebAppDeploymentOnMachineGroup@0", new Dictionary<string, string>
        {
            ["WebSiteName"] = definition.Deployment.WebsiteNameOrDefault,
            ["Package"] = packagePath,
            ["TakeAppOfflineFlag"] = "true",
            ["RemoveAdditionalFilesFlag"] = "true"
        }, $"Deploy to IIS ({environment})")
    };

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
{{RollbackScripts.Header(config, environment)}}
$site = {{YamlBuilder.PsLiteral(deployment.WebsiteNameOrDefault)}}
$target = {{RollbackScripts.TargetOrNull(deployment)}}
{{PowerShellSnippets.ResolveIisSitePath}}
{{PowerShellSnippets.SyncFolderFunction}}
if (Test-Path $target) {
  Sync-Folder -Source $target -Destination $backup
  Write-Host "Backed up $target to $backup"
} else {
  Write-Host "Nothing to back up: $target does not exist yet"
}
{{RollbackScripts.Prune(config)}}
""", "Back up IIS site before deploy")
    };

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
{{RollbackScripts.Header(config, environment)}}
{{RollbackScripts.RequireBackup}}
$site = {{YamlBuilder.PsLiteral(deployment.WebsiteNameOrDefault)}}
$target = {{RollbackScripts.TargetOrNull(deployment)}}
{{PowerShellSnippets.ResolveIisSitePath}}
{{PowerShellSnippets.SyncFolderFunction}}
Sync-Folder -Source $backup -Destination $target -Mirror
Write-Host "Restored $target from $backup"
""", "Roll back IIS site")
    };
}
