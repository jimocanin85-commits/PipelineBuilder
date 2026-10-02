using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Web.State;

/// <summary>Which wizard step edits a given setting (the <c>AffectedField</c> of a validation result).</summary>
public static class StepMap
{
    private static readonly Dictionary<string, WizardStep> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(PipelineDefinition.Name)] = WizardStep.Start,
        [nameof(PipelineDefinition.ProjectType)] = WizardStep.Start,
        [nameof(PipelineDefinition.Artifact)] = WizardStep.Start,
        [nameof(PipelineDefinition.Environments)] = WizardStep.Target,
        [nameof(PipelineDefinition.ReleaseBranch)] = WizardStep.Target,
        [nameof(PipelineDefinition.Trigger)] = WizardStep.Target,
        [nameof(PipelineDefinition.Deployment)] = WizardStep.Target,
        [nameof(PipelineDefinition.DeploymentStrategy)] = WizardStep.Target,
        [nameof(PipelineDefinition.PoolName)] = WizardStep.Target,
        [nameof(PipelineDefinition.BuildAgent)] = WizardStep.Target,
        [nameof(PipelineDefinition.AzureServiceConnection)] = WizardStep.Target,
        [nameof(PipelineDefinition.VariableGroups)] = WizardStep.Target,
        [nameof(PipelineDefinition.KeyVault)] = WizardStep.Target,
        [nameof(PipelineDefinition.Rollback)] = WizardStep.Safety,
        [nameof(PipelineDefinition.HealthChecks)] = WizardStep.Safety,
        [nameof(PipelineDefinition.Notifications)] = WizardStep.Safety
    };

    /// <summary>The step for a field, or null when the issue isn't tied to a setting (e.g. a line in the YAML).</summary>
    public static WizardStep? ForField(string? field) =>
        field != null && Fields.TryGetValue(field, out var step) ? step : null;
}
