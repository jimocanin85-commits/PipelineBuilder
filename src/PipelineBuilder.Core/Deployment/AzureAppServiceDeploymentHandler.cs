using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Deploys to Azure App Service. With the slot-swap strategy it deploys to the staging slot, and the
/// previous version stays there after the swap, so there is no backup step.
/// </summary>
public sealed class AzureAppServiceDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.AzureAppService;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.AzureAppServiceSlot;
    public override bool SupportsSlotSwap => true;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var inputs = new Dictionary<string, string>
        {
            ["azureSubscription"] = definition.AzureServiceConnection,
            ["appType"] = "webApp",
            ["appName"] = definition.Deployment.WebAppNameOrDefault,
            ["package"] = packagePath
        };

        // With the slot-swap strategy we deploy to the staging slot; the strategy adds the swap.
        if (definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.SlotSwap)
        {
            inputs["deployToSlotOrASE"] = "true";
            inputs["resourceGroupName"] = "$(RESOURCE_GROUP)";
            inputs["slotName"] = definition.DeploymentStrategy.SlotName ?? "staging";
        }

        return new[] { YamlBuilder.Task("AzureWebApp@1", inputs, $"Deploy to Azure App Service ({environment})") };
    }

    // Swapping back automatically is unsafe: if the failure happened before the swap,
    // swapping would move the broken build into production.
    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.PowerShellStep($$"""
Write-Warning 'Deployment failed. If the slot swap already happened, swap back manually:'
Write-Warning {{YamlBuilder.PsLiteral($"  az webapp deployment slot swap -g $(RESOURCE_GROUP) -n {deployment.WebAppNameOrDefault} --slot staging --target-slot production")}}
""", "App Service rollback guidance")
    };

    public override IEnumerable<ValidationResult> Validate(PipelineDefinition definition)
    {
        if (definition.Rollback.Enabled)
        {
            yield return Finding(ValidationSeverity.Info,
                "App Service rollback is not automatic: the failure hook prints the command to swap the slots back.",
                nameof(PipelineDefinition.Rollback),
                "Use the slot-swap strategy so production only changes after the new version is deployed.");
        }
    }
}
