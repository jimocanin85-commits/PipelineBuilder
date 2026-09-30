using SimlifiezYaml.Web.State;

namespace SimlifiezYaml.Web.Components.Steps;

public static class StepTitles
{
    public static string For(WizardStep step) => step switch
    {
        WizardStep.ProjectType => "Project type",
        WizardStep.BuildAgent => "Build agent",
        WizardStep.PipelineTemplate => "Template",
        WizardStep.EnvironmentSelection => "Environments",
        WizardStep.DeploymentTarget => "Deployment",
        WizardStep.IdentityModel => "Identity",
        WizardStep.VariableGroupsAndKeyVault => "Variables & KV",
        WizardStep.ArtifactSettings => "Artifacts",
        WizardStep.RollbackSettings => "Rollback",
        WizardStep.HealthChecks => "Health checks",
        WizardStep.Notifications => "Notifications",
        WizardStep.GovernancePolicies => "Governance",
        WizardStep.YamlPreview => "YAML preview",
        WizardStep.ValidationResults => "Validation",
        WizardStep.DownloadExport => "Export",
        _ => step.ToString()
    };
}
