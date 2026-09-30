using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Web.State;

/// <summary>Which wizard step edits a given setting (the <c>AffectedField</c> of a validation result).</summary>
public static class StepMap
{
    private static readonly Dictionary<string, WizardStep> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(PipelineDefinition.Name)] = WizardStep.ProjectType,
        [nameof(PipelineDefinition.ProjectType)] = WizardStep.ProjectType,
        [nameof(PipelineDefinition.PoolName)] = WizardStep.BuildAgent,
        [nameof(PipelineDefinition.BuildAgent)] = WizardStep.BuildAgent,
        [nameof(PipelineDefinition.AgentDiagnostics)] = WizardStep.BuildAgent,
        [nameof(PipelineDefinition.Environments)] = WizardStep.EnvironmentSelection,
        [nameof(PipelineDefinition.ReleaseBranch)] = WizardStep.EnvironmentSelection,
        [nameof(PipelineDefinition.Trigger)] = WizardStep.EnvironmentSelection,
        [nameof(PipelineDefinition.DeploymentTarget)] = WizardStep.DeploymentTarget,
        [nameof(PipelineDefinition.Deployment)] = WizardStep.DeploymentTarget,
        [nameof(PipelineDefinition.DeploymentStrategy)] = WizardStep.DeploymentTarget,
        [nameof(PipelineDefinition.IaC)] = WizardStep.DeploymentTarget,
        [nameof(PipelineDefinition.AzureServiceConnection)] = WizardStep.IdentityModel,
        [nameof(PipelineDefinition.VariableGroups)] = WizardStep.VariableGroupsAndKeyVault,
        [nameof(PipelineDefinition.KeyVault)] = WizardStep.VariableGroupsAndKeyVault,
        [nameof(PipelineDefinition.Artifact)] = WizardStep.ArtifactSettings,
        [nameof(PipelineDefinition.Rollback)] = WizardStep.RollbackSettings,
        [nameof(PipelineDefinition.HealthChecks)] = WizardStep.HealthChecks,
        [nameof(PipelineDefinition.Notifications)] = WizardStep.Notifications,
        [nameof(PipelineDefinition.Governance)] = WizardStep.GovernancePolicies,
        [nameof(GovernancePolicyConfig.NamingConvention)] = WizardStep.GovernancePolicies,
        ["Yaml"] = WizardStep.GovernancePolicies
    };

    /// <summary>The step for a field, or null when the issue isn't tied to a setting (e.g. a line in the YAML).</summary>
    public static WizardStep? ForField(string? field) =>
        field != null && Fields.TryGetValue(field, out var step) ? step : null;
}
