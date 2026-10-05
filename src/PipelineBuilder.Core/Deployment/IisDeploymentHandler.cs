using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Deploys to an IIS website, with folder backup and rollback. An agent on the server uses Azure's
/// IIS task. A build agent that deploys over the network cannot, so it sends a script that stops
/// the app pool, replaces the files and starts the pool again.
/// </summary>
public sealed class IisDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.Iis;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.Iis;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        var display = $"Deploy to IIS ({environment})";
        if (deployment.RunFrom == DeployFrom.Server)
        {
            return new[]
            {
                YamlBuilder.Task("IISWebAppDeploymentOnMachineGroup@0", new Dictionary<string, string>
                {
                    ["WebSiteName"] = deployment.WebsiteNameOrDefault,
                    ["Package"] = packagePath,
                    ["TakeAppOfflineFlag"] = "true",
                    ["RemoveAdditionalFilesFlag"] = "true"
                }, display)
            };
        }

        return new[]
        {
            ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
$ErrorActionPreference = 'Stop'
$site = {{YamlBuilder.PsLiteral(deployment.WebsiteNameOrDefault)}}
$package = {{ServerScript.PathLiteral(ScriptShell.PowerShell, packagePath)}}
$target = $null
{{PowerShellSnippets.ResolveIisSitePath}}
{{PowerShellSnippets.SyncFolderFunction}}
{{PowerShellSnippets.ResolvePackageSource}}
$pool = $website.applicationPool
if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') {
  Stop-WebAppPool -Name $pool
  for ($i = 0; $i -lt 30 -and (Get-WebAppPoolState -Name $pool).Value -ne 'Stopped'; $i++) { Start-Sleep -Seconds 1 }
}
Sync-Folder -Source $source -Destination $target -Mirror
Start-WebAppPool -Name $pool
Write-Host "Deployed to $target and started app pool $pool"
""", display)
        };
    }

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
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
        ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
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
