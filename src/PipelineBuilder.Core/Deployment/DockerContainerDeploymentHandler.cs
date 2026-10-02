using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>Replaces a Docker container on the registered servers; rollback restarts the previous image.</summary>
public sealed class DockerContainerDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.DockerContainer;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.DockerContainer;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
# Windows PowerShell 5.1 turns redirected native stderr into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
$image = {{YamlBuilder.PsLiteral($"$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)")}}
$name = {{YamlBuilder.PsLiteral(definition.Deployment.ContainerNameOrDefault)}}
docker pull $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
docker rm -f $name 2>$null
docker run -d --name $name --restart unless-stopped $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
Write-Host "Container $name is running $image"
""", $"Run Docker container ({environment})")
    };

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
{{RollbackScripts.Header(config, environment)}}
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
{{RollbackScripts.Prune(config)}}
""", "Record running container image before deploy")
    };

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
{{RollbackScripts.Header(config, environment)}}
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
    };
}
