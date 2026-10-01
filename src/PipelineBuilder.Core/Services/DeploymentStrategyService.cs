using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Wraps the deploy step(s) for the chosen strategy. Rolling deployments use Azure DevOps'
/// native <c>rolling</c> strategy (see <c>DeploymentStageGenerator</c>); blue-green and canary
/// need traffic routing that depends on your load balancer, so they emit a clear warning step
/// where that logic belongs.
/// </summary>
public sealed class DeploymentStrategyService : IDeploymentStrategyService
{
    public IReadOnlyList<string> GenerateStrategySteps(
        DeploymentStrategyConfig config, string environment, string deploymentTaskYaml, string webAppName = "$(WEBAPP_NAME)",
        string azureServiceConnection = "$(AZURE_SERVICE_CONNECTION)")
    {
        return config.StrategyType switch
        {
            DeploymentStrategyType.BlueGreen => new[]
            {
                deploymentTaskYaml,
                YamlBuilder.PowerShellStep(
                    $"Write-Warning 'Blue-green: switch traffic to the new environment for {environment} here (load balancer / DNS).'",
                    "Blue-green traffic switch (placeholder)")
            },
            DeploymentStrategyType.Canary => new[]
            {
                YamlBuilder.PowerShellStep(
                    $"Write-Warning 'Canary: route {config.CanaryPercentage}% of traffic for {environment} here before full rollout (load balancer).'",
                    "Canary traffic split (placeholder)"),
                deploymentTaskYaml
            },
            DeploymentStrategyType.SlotSwap => new[]
            {
                deploymentTaskYaml,
                YamlBuilder.Task("AzureAppServiceManage@0", new Dictionary<string, string>
                {
                    ["azureSubscription"] = azureServiceConnection,
                    ["Action"] = "Swap Slots",
                    ["WebAppName"] = webAppName,
                    ["ResourceGroupName"] = "$(RESOURCE_GROUP)",
                    ["SourceSlot"] = config.SlotName ?? "staging",
                    ["SwapWithProduction"] = "true"
                }, "Swap deployment slots")
            },
            _ => new[] { deploymentTaskYaml }
        };
    }

    public string GetStrategyNote(DeploymentStrategyConfig config) =>
        config.StrategyType switch
        {
            DeploymentStrategyType.Rolling => $"Rolling deploys to {Math.Max(1, config.BatchSize)} server(s) at a time. Requires servers registered in the Azure DevOps environment (on-premises target).",
            DeploymentStrategyType.Canary => $"Canary releases {config.CanaryPercentage}% traffic before full promotion. Traffic routing must be added for your load balancer.",
            DeploymentStrategyType.SlotSwap => "Deploys to the staging slot, then swaps it into production.",
            DeploymentStrategyType.BlueGreen => "Blue-green keeps two environments; add the traffic switch for your load balancer.",
            _ => "Deploys the whole environment in one go."
        };
}
