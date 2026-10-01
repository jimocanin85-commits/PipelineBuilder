using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Generates the step(s) that actually deploy the downloaded package, based on
/// <see cref="DeploymentConfig.Kind"/>.
/// </summary>
public sealed class DeploymentStepService : IDeploymentStepService
{
    public IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        return deployment.Kind switch
        {
            DeploymentKind.Iis => new[]
            {
                YamlBuilder.Task("IISWebAppDeploymentOnMachineGroup@0", new Dictionary<string, string>
                {
                    ["WebSiteName"] = deployment.WebsiteNameOrDefault,
                    ["Package"] = packagePath,
                    ["TakeAppOfflineFlag"] = "true",
                    ["RemoveAdditionalFilesFlag"] = "true"
                }, $"Deploy to IIS ({environment})")
            },

            DeploymentKind.WindowsService => new[]
            {
                YamlBuilder.PowerShellStep($$"""
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
            },

            DeploymentKind.FileShare => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$ErrorActionPreference = 'Stop'
$target = {{YamlBuilder.PsLiteral(deployment.TargetPathOrDefault)}}
$package = {{YamlBuilder.PsLiteral(packagePath)}}
{{PowerShellSnippets.SyncFolderFunction}}
{{PowerShellSnippets.ResolvePackageSource}}
Sync-Folder -Source $source -Destination $target -Mirror
Write-Host "Deployed to $target"
""", $"Deploy to file share ({environment})")
            },

            DeploymentKind.AzureAppService => new[] { AppServiceDeploy(definition, environment, packagePath) },

            // Applies the repository's manifests with the image built by this run.
            DeploymentKind.Kubernetes => new[]
            {
                YamlBuilder.Task("KubernetesManifest@1", new Dictionary<string, string>
                {
                    ["action"] = "deploy",
                    ["connectionType"] = "kubernetesServiceConnection",
                    ["kubernetesServiceConnection"] = deployment.KubernetesServiceConnectionOrDefault,
                    ["namespace"] = deployment.KubernetesNamespaceOrDefault,
                    ["manifests"] = deployment.ManifestsPathOrDefault,
                    ["containers"] = $"$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)"
                }, $"Deploy to Kubernetes ({environment})")
            },

            DeploymentKind.DockerContainer => new[]
            {
                YamlBuilder.PowerShellStep($$"""
# Windows PowerShell 5.1 turns redirected native stderr into errors under 'Stop'.
$ErrorActionPreference = 'Continue'
$image = {{YamlBuilder.PsLiteral($"$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)")}}
$name = {{YamlBuilder.PsLiteral(deployment.ContainerNameOrDefault)}}
docker pull $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
docker rm -f $name 2>$null
docker run -d --name $name --restart unless-stopped $image
{{PowerShellSnippets.ThrowOnNativeFailure}}
Write-Host "Container $name is running $image"
""", $"Run Docker container ({environment})")
            },

            _ => new[]
            {
                YamlBuilder.PowerShellStep(
                    deployment.CustomScript
                        ?? $"Write-Warning 'No deployment kind selected: add your deploy commands here. The package is at {packagePath}'",
                    $"Deploy to {environment}")
            }
        };
    }

    private static string AppServiceDeploy(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        var inputs = new Dictionary<string, string>
        {
            ["azureSubscription"] = definition.AzureServiceConnection,
            ["appType"] = "webApp",
            ["appName"] = deployment.WebAppNameOrDefault,
            ["package"] = packagePath
        };

        // With the slot-swap strategy we deploy to the staging slot; the strategy adds the swap.
        if (definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.SlotSwap)
        {
            inputs["deployToSlotOrASE"] = "true";
            inputs["resourceGroupName"] = "$(RESOURCE_GROUP)";
            inputs["slotName"] = definition.DeploymentStrategy.SlotName ?? "staging";
        }

        return YamlBuilder.Task("AzureWebApp@1", inputs, $"Deploy to Azure App Service ({environment})");
    }
}
