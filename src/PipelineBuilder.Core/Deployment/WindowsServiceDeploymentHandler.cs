using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>Stops a Windows service, mirrors the package into its folder and starts it again.</summary>
public sealed class WindowsServiceDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.WindowsService;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.WindowsService;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        return new[]
        {
            ServerScript.Step(definition.Deployment, ScriptShell.PowerShell, $$"""
$ErrorActionPreference = 'Stop'
$service = {{YamlBuilder.PsLiteral(deployment.ServiceNameOrDefault)}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
$package = {{YamlBuilder.PsLiteral(packagePath)}}
{{PowerShellSnippets.SyncFolderFunction}}
{{PowerShellSnippets.ResolvePackageSource}}
$svc = Get-Service -Name $service -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
  Stop-Service -Name $service -Force
  $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}
Sync-Folder -Source $source -Destination $target -Mirror
if ($svc) { Start-Service -Name $service } else { Write-Warning "Service $service does not exist yet; create it before the first deployment." }
Write-Host "Deployed to $target"
""", $"Deploy Windows service ({environment})")
        };
    }

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) =>
        RollbackScripts.BackUpFolder(config, deployment, environment);

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
{{RollbackScripts.Header(config, environment)}}
{{RollbackScripts.RequireBackup}}
$service = {{YamlBuilder.PsLiteral(deployment.ServiceNameOrDefault)}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
Sync-Folder -Source $backup -Destination $target -Mirror
Start-Service -Name $service
Write-Host "Restored $target from $backup and restarted $service"
""", "Roll back Windows service")
    };
}
