using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// PowerShell shared by the backup and rollback steps. Backups are taken in the deploy job right before
/// deploying, into <c>{BackupPath}\{environment}\{BuildId}</c>. Rollback runs in the same deployment job's
/// <c>on: failure</c> hook, on the same server, and restores that exact backup.
/// </summary>
internal static class RollbackScripts
{
    public static string Header(RollbackConfig config, string environment) => $$"""
$ErrorActionPreference = 'Stop'
$envRoot = Join-Path {{YamlBuilder.PsLiteral(config.BackupRootOrDefault)}} {{YamlBuilder.PsLiteral(environment)}}
$backup = Join-Path $envRoot '$(Build.BuildId)'
""";

    /// <summary>Keeps the newest <see cref="RollbackConfig.RetentionCount"/> backups of the environment.</summary>
    public static string Prune(RollbackConfig config)
    {
        var retention = Math.Max(1, config.RetentionCount);
        return $$"""
Get-ChildItem -Path $envRoot -Directory | Sort-Object LastWriteTime -Descending |
  Select-Object -Skip {{retention}} | Remove-Item -Recurse -Force
""";
    }

    public const string RequireBackup = """
if (-not (Test-Path $backup)) {
  Write-Warning "No backup found at $backup (first deployment?). Nothing to roll back."
  exit 0
}
""";

    /// <summary>Sets <c>envRoot</c> and <c>backup</c> on a Linux server.</summary>
    public static string BashHeader(RollbackConfig config, string environment) => $$"""
{{BashSnippets.Strict}}
envRoot={{YamlBuilder.BashLiteral(config.LinuxBackupRootOrDefault.TrimEnd('/') + "/" + environment)}}
backup="$envRoot/$(Build.BuildId)"
""";

    /// <summary>Keeps the newest <see cref="RollbackConfig.RetentionCount"/> backups of the environment (bash).</summary>
    public static string BashPrune(RollbackConfig config)
    {
        var skip = Math.Max(1, config.RetentionCount) + 1;
        return $$"""
if [ -d "$envRoot" ]; then
  ls -1dt "$envRoot"/*/ 2>/dev/null | tail -n +{{skip}} | xargs -r rm -rf
fi
""";
    }

    public const string BashRequireBackup = """
if [ ! -d "$backup" ]; then
  echo "##vso[task.logissue type=warning]No backup found at $backup (first deployment?). Nothing to roll back."
  exit 0
fi
""";

    /// <summary>The configured target folder as a PowerShell literal, or <c>$null</c> to look it up.</summary>
    public static string TargetOrNull(DeploymentConfig deployment) =>
        string.IsNullOrWhiteSpace(deployment.TargetPath) ? "$null" : YamlBuilder.PsLiteral(deployment.TargetPath);

    /// <summary>Copies the deployment folder (Windows service, file share) into the backup.</summary>
    public static IReadOnlyList<string> BackUpFolder(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
{{Header(config, environment)}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
if (Test-Path $target) {
  Sync-Folder -Source $target -Destination $backup
  Write-Host "Backed up $target to $backup"
} else {
  Write-Host "Nothing to back up: $target does not exist yet"
}
{{Prune(config)}}
""", "Back up deployment folder before deploy")
    };
}
