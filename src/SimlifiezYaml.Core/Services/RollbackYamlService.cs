using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Services;

/// <summary>
/// Backup and rollback steps. Backups are taken in the deploy job right before deploying, into
/// <c>{BackupPath}\{environment}\{BuildId}</c>. Rollback runs in the same deployment job's
/// <c>on: failure</c> hook, on the same server, and restores that exact backup.
/// </summary>
public sealed class RollbackYamlService : IRollbackYamlService
{
    public RollbackTarget ResolveTarget(RollbackConfig config, DeploymentConfig deployment) => deployment.Kind switch
    {
        DeploymentKind.Iis => RollbackTarget.Iis,
        DeploymentKind.WindowsService => RollbackTarget.WindowsService,
        DeploymentKind.FileShare => RollbackTarget.FileShare,
        DeploymentKind.AzureAppService => RollbackTarget.AzureAppServiceSlot,
        DeploymentKind.DockerContainer => RollbackTarget.DockerContainer,
        DeploymentKind.Kubernetes => RollbackTarget.Kubernetes,
        _ => config.Target
    };

    public IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment)
    {
        if (!config.Enabled) return Array.Empty<string>();

        var header = BackupHeader(config, environment);
        var retention = Math.Max(1, config.RetentionCount);
        var prune = $$"""
Get-ChildItem -Path $envRoot -Directory | Sort-Object LastWriteTime -Descending |
  Select-Object -Skip {{retention}} | Remove-Item -Recurse -Force
""";

        return ResolveTarget(config, deployment) switch
        {
            RollbackTarget.Iis => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
$site = {{YamlBuilder.PsLiteral(deployment.WebsiteNameOrDefault)}}
$target = {{TargetOrNull(deployment)}}
{{PowerShellSnippets.ResolveIisSitePath}}
{{PowerShellSnippets.SyncFolderFunction}}
if (Test-Path $target) {
  Sync-Folder -Source $target -Destination $backup
  Write-Host "Backed up $target to $backup"
} else {
  Write-Host "Nothing to back up: $target does not exist yet"
}
{{prune}}
""", "Back up IIS site before deploy")
            },
            RollbackTarget.WindowsService or RollbackTarget.FileShare => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
if (Test-Path $target) {
  Sync-Folder -Source $target -Destination $backup
  Write-Host "Backed up $target to $backup"
} else {
  Write-Host "Nothing to back up: $target does not exist yet"
}
{{prune}}
""", "Back up deployment folder before deploy")
            },
            RollbackTarget.DockerContainer => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
# Windows PowerShell 5.1 turns redirected native stderr into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
$name = {{YamlBuilder.PsLiteral(deployment.ContainerNameOrDefault)}}
$info = docker inspect $name 2>$null | ConvertFrom-Json
$global:LASTEXITCODE = 0
if ($info) {
  New-Item -ItemType Directory -Force -Path $backup | Out-Null
  Set-Content -Path (Join-Path $backup 'image.txt') -Value $info[0].Config.Image
  Write-Host "Recorded running image $($info[0].Config.Image)"
} else {
  Write-Host "Nothing to back up: container $name is not running"
}
{{prune}}
""", "Record running container image before deploy")
            },
            // App Service: the slot-swap strategy keeps the previous version in the staging slot.
            _ => Array.Empty<string>()
        };
    }

    public IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment)
    {
        if (!config.Enabled) return Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(config.RollbackScript))
            return new[] { YamlBuilder.ScriptStep(config.RollbackScript, "Execute custom rollback script") };

        var header = BackupHeader(config, environment);
        var requireBackup = """
if (-not (Test-Path $backup)) {
  Write-Warning "No backup found at $backup (first deployment?). Nothing to roll back."
  exit 0
}
""";

        return ResolveTarget(config, deployment) switch
        {
            RollbackTarget.Iis => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
{{requireBackup}}
$site = {{YamlBuilder.PsLiteral(deployment.WebsiteNameOrDefault)}}
$target = {{TargetOrNull(deployment)}}
{{PowerShellSnippets.ResolveIisSitePath}}
{{PowerShellSnippets.SyncFolderFunction}}
Sync-Folder -Source $backup -Destination $target -Mirror
Write-Host "Restored $target from $backup"
""", "Roll back IIS site")
            },
            RollbackTarget.WindowsService => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
{{requireBackup}}
$service = {{YamlBuilder.PsLiteral(deployment.ServiceNameOrDefault)}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
Sync-Folder -Source $backup -Destination $target -Mirror
Start-Service -Name $service
Write-Host "Restored $target from $backup and restarted $service"
""", "Roll back Windows service")
            },
            RollbackTarget.FileShare => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
{{requireBackup}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
Sync-Folder -Source $backup -Destination $target -Mirror
Write-Host "Restored $target from $backup"
""", "Roll back file share")
            },
            RollbackTarget.Kubernetes => new[]
            {
                YamlBuilder.Task("Kubernetes@1", new Dictionary<string, string>
                {
                    ["connectionType"] = "Kubernetes Service Connection",
                    ["kubernetesServiceEndpoint"] = deployment.KubernetesServiceConnectionOrDefault,
                    ["namespace"] = deployment.KubernetesNamespaceOrDefault,
                    ["command"] = "rollout",
                    ["arguments"] = $"undo deployment/{deployment.KubernetesDeploymentNameOrDefault}"
                }, "Roll back Kubernetes deployment")
            },
            RollbackTarget.DockerContainer => new[]
            {
                YamlBuilder.PowerShellStep($$"""
{{header}}
# Windows PowerShell 5.1 turns redirected native stderr into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
$record = Join-Path $backup 'image.txt'
if (-not (Test-Path $record)) {
  Write-Warning "No previous image recorded at $record. Nothing to roll back."
  exit 0
}
$image = (Get-Content -Path $record -Raw).Trim()
$name = {{YamlBuilder.PsLiteral(deployment.ContainerNameOrDefault)}}
docker rm -f $name 2>$null
docker run -d --name $name --restart unless-stopped $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
Write-Host "Rolled back container $name to $image"
""", "Roll back Docker container")
            },
            _ => new[]
            {
                // Swapping back automatically is unsafe: if the failure happened before the swap,
                // swapping would move the broken build into production.
                YamlBuilder.PowerShellStep($$"""
Write-Warning 'Deployment failed. If the slot swap already happened, swap back manually:'
Write-Warning {{YamlBuilder.PsLiteral($"  az webapp deployment slot swap -g $(RESOURCE_GROUP) -n {deployment.WebAppNameOrDefault} --slot staging --target-slot production")}}
""", "App Service rollback guidance")
            }
        };
    }

    private static string BackupHeader(RollbackConfig config, string environment) => $$"""
$ErrorActionPreference = 'Stop'
$envRoot = Join-Path {{YamlBuilder.PsLiteral(config.BackupRootOrDefault)}} {{YamlBuilder.PsLiteral(environment)}}
$backup = Join-Path $envRoot '$(Build.BuildId)'
""";

    private static string TargetOrNull(DeploymentConfig deployment) =>
        string.IsNullOrWhiteSpace(deployment.TargetPath) ? "$null" : YamlBuilder.PsLiteral(deployment.TargetPath);
}
