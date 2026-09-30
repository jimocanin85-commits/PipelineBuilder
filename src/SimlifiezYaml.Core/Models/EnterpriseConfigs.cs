using SimlifiezYaml.Core.Enums;

namespace SimlifiezYaml.Core.Models;

public sealed class VariableGroupConfig
{
    public string Name { get; set; } = string.Empty;
    public VariableGroupScope Scope { get; set; } = VariableGroupScope.Pipeline;
    public string? EnvironmentName { get; set; }
    public bool ContainsSecrets { get; set; }
}

public sealed class KeyVaultConfig
{
    public string ServiceConnection { get; set; } = "$(AZURE_SERVICE_CONNECTION)";
    public string KeyVaultName { get; set; } = string.Empty;
    public string SecretsFilter { get; set; } = "*";
    public bool RunAsPreJob { get; set; } = true;
}

public sealed class ArtifactConfig
{
    public ArtifactType ArtifactType { get; set; } = ArtifactType.PipelineArtifact;
    public string ArtifactName { get; set; } = "drop";
    public string? PackagePath { get; set; }
    public string? PublishPath { get; set; }
    public string? DownloadPath { get; set; }

    /// <summary>Docker registry service connection used to push images.</summary>
    public string ContainerRegistryConnection { get; set; } = "$(DOCKER_SERVICE_CONNECTION)";
}

/// <summary>
/// Where and how the artifact is deployed. Values default to pipeline variables so the generated
/// YAML never hard-codes server paths; set them here or define the variables in a variable group.
/// </summary>
public sealed class DeploymentConfig
{
    public DeploymentKind Kind { get; set; } = DeploymentKind.Custom;

    /// <summary>Folder the app is deployed to (IIS site folder, service folder or file share).</summary>
    public string? TargetPath { get; set; }

    /// <summary>Windows service name (WindowsService).</summary>
    public string? ServiceName { get; set; }

    /// <summary>IIS website name (Iis).</summary>
    public string? WebsiteName { get; set; }

    /// <summary>Azure App Service name (AzureAppService).</summary>
    public string? WebAppName { get; set; }

    /// <summary>Container name (DockerContainer).</summary>
    public string? ContainerName { get; set; }

    /// <summary>Script run by the Custom deploy step. Defaults to a placeholder.</summary>
    public string? CustomScript { get; set; }

    public string TargetPathOrDefault => string.IsNullOrWhiteSpace(TargetPath) ? "$(DEPLOY_PATH)" : TargetPath;
    public string ServiceNameOrDefault => string.IsNullOrWhiteSpace(ServiceName) ? "$(SERVICE_NAME)" : ServiceName;
    public string WebsiteNameOrDefault => string.IsNullOrWhiteSpace(WebsiteName) ? "Default Web Site" : WebsiteName;
    public string WebAppNameOrDefault => string.IsNullOrWhiteSpace(WebAppName) ? "$(WEBAPP_NAME)" : WebAppName;
    public string ContainerNameOrDefault => string.IsNullOrWhiteSpace(ContainerName) ? "$(CONTAINER_NAME)" : ContainerName;

    /// <summary>True for targets that run on servers registered in an Azure DevOps environment.</summary>
    public bool IsServerDeployment => Kind is DeploymentKind.Iis or DeploymentKind.WindowsService or DeploymentKind.FileShare;
}

public sealed class RollbackConfig
{
    public bool Enabled { get; set; }
    public string? BackupPath { get; set; }
    public string? RollbackScript { get; set; }
    public bool RestorePreviousArtifact { get; set; }
    public int RetentionCount { get; set; } = 3;

    /// <summary>
    /// Used only when <see cref="DeploymentConfig.Kind"/> is <see cref="DeploymentKind.Custom"/>;
    /// otherwise the rollback target follows the deployment kind.
    /// </summary>
    public RollbackTarget Target { get; set; } = RollbackTarget.Iis;

    /// <summary>Root folder for backups. Each environment and build gets its own subfolder.</summary>
    public string BackupRootOrDefault => string.IsNullOrWhiteSpace(BackupPath) ? @"D:\backups" : BackupPath;
}

public sealed class HealthCheckConfig
{
    public bool Enabled { get; set; }
    public HealthCheckType HealthCheckType { get; set; } = HealthCheckType.HttpEndpoint;
    public string? Url { get; set; }
    public int ExpectedStatusCode { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public string? ServiceName { get; set; }
    public string? AppPoolName { get; set; }
    public int? Port { get; set; }
    public string? CustomScript { get; set; }
}

public sealed class NotificationConfig
{
    public NotificationType NotificationType { get; set; }
    public string? WebhookUrlVariable { get; set; }
    public string? TeamsWebhookVariable { get; set; }
    public IReadOnlyList<string> EmailRecipients { get; set; } = Array.Empty<string>();
    public bool NotifyOnSuccess { get; set; }
    public bool NotifyOnFailure { get; set; } = true;
}

public sealed class InfrastructureAsCodeConfig
{
    public IaCTool Tool { get; set; } = IaCTool.Terraform;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool PlanOnly { get; set; }
    public bool ApplyOnApproval { get; set; } = true;
    public string ServiceConnection { get; set; } = "$(AZURE_SERVICE_CONNECTION)";
    public string? BackendConfig { get; set; }
}

public sealed class DeploymentStrategyConfig
{
    public DeploymentStrategyType StrategyType { get; set; } = DeploymentStrategyType.Standard;
    public int BatchSize { get; set; } = 1;
    public int CanaryPercentage { get; set; } = 10;
    public string? SlotName { get; set; } = "staging";
    public bool RollbackOnFailure { get; set; } = true;
}

public sealed class GovernancePolicyConfig
{
    public bool RequiredApprovals { get; set; } = true;
    public IReadOnlyList<string> RequiredVariableGroups { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ForbiddenTasks { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredTasks { get; set; } = Array.Empty<string>();
    public string? NamingConvention { get; set; }
    public bool RequireHealthCheck { get; set; } = true;
    public bool RequireRollback { get; set; }
}

public sealed class RepoScanResult
{
    public ProjectType ProjectType { get; set; }
    public string? FrameworkVersion { get; set; }
    public bool HasDockerfile { get; set; }
    public bool HasTests { get; set; }
    public bool HasTerraform { get; set; }
    public bool HasBicep { get; set; }
    public IReadOnlyList<string> SuggestedTemplates { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DetectedFiles { get; set; } = Array.Empty<string>();
}

public sealed class AgentDiagnosticConfig
{
    public bool CheckPowerShellVersion { get; set; } = true;
    public bool CheckWinRm { get; set; } = true;
    public bool CheckDocker { get; set; }
    public bool CheckIisModule { get; set; } = true;
    public bool CheckNetworkAccess { get; set; } = true;
    public bool CheckPermissions { get; set; } = true;
    public IReadOnlyList<string> DeploymentFolders { get; set; } = Array.Empty<string>();
}

public sealed class SecretGovernanceResult
{
    public bool HasPlainTextSecret { get; set; }
    public string? SecretLocation { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public SecretSeverity Severity { get; set; }
}
