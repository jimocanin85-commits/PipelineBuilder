using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Replaces a Docker container on the registered servers (Linux or Windows): logs in to the registry,
/// pulls the image built by this run and starts it. Rollback restarts the previous image.
/// </summary>
public sealed class DockerContainerDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.DockerContainer;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.DockerContainer;

    public override ScriptShell Shell(DeploymentConfig deployment) =>
        deployment.ServerOs == ServerOs.Linux ? ScriptShell.Bash : ScriptShell.PowerShell;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        var image = $"$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)";
        var display = $"Run Docker container ({environment})";

        // The registry service connection logs the server in, so no password is handled in a script.
        var login = YamlBuilder.Task("Docker@2", new Dictionary<string, string>
        {
            ["command"] = "login",
            ["containerRegistry"] = definition.Artifact.ContainerRegistryConnection
        }, "Log in to the container registry");

        var run = Shell(deployment) == ScriptShell.Bash
            ? YamlBuilder.BashStep($$"""
{{BashSnippets.Strict}}
image={{YamlBuilder.BashLiteral(image)}}
name={{YamlBuilder.BashLiteral(deployment.ContainerNameOrDefault)}}
docker pull "$image"
docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" --restart unless-stopped{{RunOptions(deployment, YamlBuilder.BashLiteral)}} "$image"
echo "Container $name is running $image"
""", display)
            : YamlBuilder.PowerShellStep($$"""
# Windows PowerShell 5.1 turns redirected native stderr into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
$image = {{YamlBuilder.PsLiteral(image)}}
$name = {{YamlBuilder.PsLiteral(deployment.ContainerNameOrDefault)}}
docker pull $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
docker rm -f $name 2>$null
docker run -d --name $name --restart unless-stopped{{RunOptions(deployment, YamlBuilder.PsLiteral)}} $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
Write-Host "Container $name is running $image"
""", display);

        return new[] { login, run };
    }

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment)
    {
        const string display = "Record running container image before deploy";
        if (Shell(deployment) == ScriptShell.Bash)
        {
            // Three dollar signs: docker's own {{.Config.Image}} template must reach bash unchanged.
            return new[]
            {
                YamlBuilder.BashStep($$$"""
{{{RollbackScripts.BashHeader(config, environment)}}}
name={{{YamlBuilder.BashLiteral(deployment.ContainerNameOrDefault)}}}
image=`docker inspect --format '{{.Config.Image}}' "$name" 2>/dev/null || true`
if [ -n "$image" ]; then
  mkdir -p "$backup"
  echo "$image" > "$backup/image.txt"
  echo "Recorded running image $image"
else
  echo "Nothing to back up: container $name is not running"
fi
{{{RollbackScripts.BashPrune(config)}}}
""", display)
            };
        }

        return new[]
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
""", display)
        };
    }

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment)
    {
        const string display = "Roll back Docker container";
        if (Shell(deployment) == ScriptShell.Bash)
        {
            return new[]
            {
                YamlBuilder.BashStep($$"""
{{RollbackScripts.BashHeader(config, environment)}}
record="$backup/image.txt"
if [ ! -f "$record" ]; then
  echo "##vso[task.logissue type=warning]No previous image recorded at $record. Nothing to roll back."
  exit 0
fi
image=`cat "$record"`
name={{YamlBuilder.BashLiteral(deployment.ContainerNameOrDefault)}}
docker rm -f "$name" >/dev/null 2>&1 || true
docker run -d --name "$name" --restart unless-stopped{{RunOptions(deployment, YamlBuilder.BashLiteral)}} "$image"
echo "Rolled back container $name to $image"
""", display)
            };
        }

        return new[]
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
docker run -d --name $name --restart unless-stopped{{RunOptions(deployment, YamlBuilder.PsLiteral)}} $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
Write-Host "Rolled back container $name to $image"
""", display)
        };
    }

    public override IEnumerable<ValidationResult> Validate(PipelineDefinition definition)
    {
        if (definition.Artifact.ArtifactType != ArtifactType.DockerImage)
        {
            yield return Finding(ValidationSeverity.Warning,
                "Docker deployments need a container image, but the artifact is not a Docker image.",
                nameof(PipelineDefinition.Artifact),
                "Set the artifact type to DockerImage.");
        }

        if (!definition.Deployment.ContainerPorts.Any(p => !string.IsNullOrWhiteSpace(p)))
        {
            yield return Finding(ValidationSeverity.Info,
                "The container publishes no ports, so nothing outside the server can reach it.",
                nameof(PipelineDefinition.Deployment),
                "Add a port such as 8080:80 if the container serves requests.");
        }
    }

    /// <summary>The <c>-p</c> and <c>-e</c> options, each value quoted for the shell; empty when there are none.</summary>
    private static string RunOptions(DeploymentConfig deployment, Func<string?, string> quote)
    {
        var options = deployment.ContainerPorts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => $"-p {quote(p.Trim())}")
            .Concat(deployment.ContainerEnvironment.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => $"-e {quote(e.Trim())}"))
            .ToList();
        return options.Count == 0 ? string.Empty : " " + string.Join(" ", options);
    }
}
