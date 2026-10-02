namespace PipelineBuilder.Web.State;

/// <summary>The wizard's steps, in order.</summary>
public enum WizardStep
{
    ProjectType = 1,
    BuildAgent = 2,
    PipelineTemplate = 3,
    EnvironmentSelection = 4,
    DeploymentTarget = 5,
    IdentityModel = 6,
    VariableGroupsAndKeyVault = 7,
    ArtifactSettings = 8,
    RollbackSettings = 9,
    HealthChecks = 10,
    Notifications = 11,
    YamlPreview = 12,
    ValidationResults = 13,
    DownloadExport = 14
}
