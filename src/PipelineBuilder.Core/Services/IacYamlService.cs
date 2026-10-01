using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class IacYamlService : IIacYamlService
{
    public IReadOnlyList<string> GenerateIacSteps(InfrastructureAsCodeConfig config, string environment)
    {
        return config.Tool switch
        {
            IaCTool.Terraform => GenerateTerraform(config, environment),
            IaCTool.Bicep => new[]
            {
                YamlBuilder.Task("AzureCLI@2", new Dictionary<string, string>
                {
                    ["azureSubscription"] = config.ServiceConnection,
                    ["scriptType"] = "pscore",
                    ["scriptLocation"] = "inlineScript",
                    // what-if previews the change without applying it.
                    ["inlineScript"] = $"az deployment group {(config.PlanOnly ? "what-if" : "create")} -g $(RESOURCE_GROUP) -f \"{config.WorkingDirectory}/main.bicep\""
                }, config.PlanOnly ? $"Preview Bicep changes for {environment}" : $"Deploy Bicep to {environment}")
            },
            IaCTool.ArmTemplate => new[]
            {
                YamlBuilder.Task("AzureResourceManagerTemplateDeployment@3", new Dictionary<string, string>
                {
                    ["deploymentScope"] = "Resource Group",
                    ["deploymentMode"] = config.PlanOnly ? "Validation" : "Incremental",
                    ["azureResourceManagerConnection"] = config.ServiceConnection,
                    ["subscriptionId"] = "$(AZURE_SUBSCRIPTION_ID)",
                    ["resourceGroupName"] = "$(RESOURCE_GROUP)",
                    ["location"] = "$(AZURE_LOCATION)",
                    ["templateLocation"] = "Linked artifact",
                    ["csmFile"] = $"{config.WorkingDirectory}/azuredeploy.json"
                }, config.PlanOnly ? $"Validate ARM template for {environment}" : $"Deploy ARM template to {environment}")
            },
            IaCTool.PowerShell => new[]
            {
                YamlBuilder.PowerShellStep(
                    $"Set-Location {YamlBuilder.PsLiteral(config.WorkingDirectory)}; .\\Deploy-Infrastructure.ps1 -Environment {YamlBuilder.PsLiteral(environment)}{(config.PlanOnly ? " -WhatIf" : "")}",
                    "Run PowerShell IaC deployment script")
            },
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> GenerateTerraform(InfrastructureAsCodeConfig config, string environment)
    {
        var steps = new List<string>
        {
            YamlBuilder.Task("TerraformTaskV4@4", new Dictionary<string, string>
            {
                ["provider"] = "azurerm",
                ["command"] = "init",
                ["workingDirectory"] = config.WorkingDirectory,
                ["backendServiceArm"] = config.ServiceConnection,
                ["backendAzureRmResourceGroupName"] = "$(TF_STATE_RG)",
                ["backendAzureRmStorageAccountName"] = "$(TF_STATE_STORAGE)",
                ["backendAzureRmContainerName"] = "$(TF_STATE_CONTAINER)",
                ["backendAzureRmKey"] = $"$(Build.DefinitionName)-{environment}.tfstate"
            }, "Terraform init")
        };

        steps.Add(YamlBuilder.Task("TerraformTaskV4@4", new Dictionary<string, string>
        {
            ["provider"] = "azurerm",
            ["command"] = "plan",
            ["workingDirectory"] = config.WorkingDirectory,
            ["commandOptions"] = "-input=false -out=tfplan",
            ["environmentServiceNameAzureRM"] = config.ServiceConnection
        }, "Terraform plan"));

        // Approval gating is done by the environment the Infrastructure job targets
        // (see InfrastructureAsCodeConfig.ApplyOnApproval), not by skipping apply here.
        if (!config.PlanOnly)
        {
            steps.Add(YamlBuilder.Task("TerraformTaskV4@4", new Dictionary<string, string>
            {
                ["provider"] = "azurerm",
                ["command"] = "apply",
                ["workingDirectory"] = config.WorkingDirectory,
                // Apply exactly the plan produced above, not a fresh one.
                ["commandOptions"] = "-input=false tfplan",
                ["environmentServiceNameAzureRM"] = config.ServiceConnection
            }, "Terraform apply"));
        }

        return steps;
    }
}
