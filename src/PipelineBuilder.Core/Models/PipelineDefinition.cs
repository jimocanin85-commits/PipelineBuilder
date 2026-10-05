using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Models;

public sealed class PipelineDefinition
{
    public string Name { get; set; } = "my-app";
    public ProjectType ProjectType { get; set; }
    public BuildAgentType BuildAgent { get; set; } = BuildAgentType.MicrosoftHosted;
    public string? PoolName { get; set; }
    public TriggerConfig Trigger { get; set; } = TriggerConfig.Default;

    /// <summary>Branch that production deployments are restricted to.</summary>
    public string ReleaseBranch { get; set; } = "main";
    public IReadOnlyList<string> Environments { get; set; } = new[] { "test", "preprod", "prod" };
    public IReadOnlyList<VariableGroupConfig> VariableGroups { get; set; } = Array.Empty<VariableGroupConfig>();
    public KeyVaultConfig? KeyVault { get; set; }
    public ArtifactConfig Artifact { get; set; } = new();
    public DeploymentConfig Deployment { get; set; } = new();

    /// <summary>Azure Resource Manager service connection, used to read Key Vault secrets.</summary>
    public string AzureServiceConnection { get; set; } = "$(AZURE_SERVICE_CONNECTION)";

    /// <summary>Id of the template last applied, if any.</summary>
    public string? TemplateId { get; set; }
    public RollbackConfig Rollback { get; set; } = new();

    /// <summary>Approvals written in the pipeline file, e.g. between test and preprod.</summary>
    public ApprovalConfig Approval { get; set; } = new();
    public IReadOnlyList<HealthCheckConfig> HealthChecks { get; set; } = Array.Empty<HealthCheckConfig>();
    public IReadOnlyList<NotificationConfig> Notifications { get; set; } = Array.Empty<NotificationConfig>();
    public DeploymentStrategyConfig DeploymentStrategy { get; set; } = new();
    public string? DotNetProjectPath { get; set; }
    /// <summary>Node.js version for Node projects (NodeTool version spec).</summary>
    public string NodeVersion { get; set; } = "24.x";

    /// <summary>Folder the Node build writes the deployable output to.</summary>
    public string NodeOutputFolder { get; set; } = "dist";

    /// <summary>Test projects to run in the Build stage (glob).</summary>
    public string? TestProjectPath { get; set; }
}

public sealed class GeneratedPipeline
{
    public string Yaml { get; set; } = string.Empty;
    public IReadOnlyList<ValidationResult> ValidationResults { get; set; } = Array.Empty<ValidationResult>();

    /// <summary>What the pipeline needs in Azure DevOps before its first run.</summary>
    public IReadOnlyList<PipelineRequirement> Requirements { get; set; } = Array.Empty<PipelineRequirement>();
}
