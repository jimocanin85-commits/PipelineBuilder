using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>Mirrors the package into a folder or file share.</summary>
public sealed class FileShareDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.FileShare;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.FileShare;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) => new[]
    {
        ServerScript.Step(definition.Deployment, ScriptShell.PowerShell, $$"""
$ErrorActionPreference = 'Stop'
$target = {{YamlBuilder.PsLiteral(definition.Deployment.TargetPathOrDefault)}}
$package = {{ServerScript.PathLiteral(ScriptShell.PowerShell, packagePath)}}
{{PowerShellSnippets.SyncFolderFunction}}
{{PowerShellSnippets.ResolvePackageSource}}
Sync-Folder -Source $source -Destination $target -Mirror
Write-Host "Deployed to $target"
""", $"Deploy to file share ({environment})")
    };

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) =>
        RollbackScripts.BackUpFolder(config, deployment, environment);

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
{{RollbackScripts.Header(config, environment)}}
{{RollbackScripts.RequireBackup}}
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
{{PowerShellSnippets.SyncFolderFunction}}
Sync-Folder -Source $backup -Destination $target -Mirror
Write-Host "Restored $target from $backup"
""", "Roll back file share")
    };
}
