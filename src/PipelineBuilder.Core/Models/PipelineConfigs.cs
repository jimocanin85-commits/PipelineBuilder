using System.Text.Json.Serialization;
using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Models;

public sealed class VariableGroupConfig
{
    public string Name { get; set; } = string.Empty;
    public VariableGroupScope Scope { get; set; } = VariableGroupScope.Pipeline;
    public string? EnvironmentName { get; set; }
    public bool ContainsSecrets { get; set; }
}

public sealed class KeyVaultConfig
{
    private static readonly char[] NameSeparators = { ',', ';', '\n', '\r' };

    public string ServiceConnection { get; set; } = "$(AZURE_SERVICE_CONNECTION)";
    public string KeyVaultName { get; set; } = string.Empty;

    /// <summary>
    /// The secrets to get, by name and separated by commas. <c>*</c> gets every secret in the vault,
    /// which hands the deployment more than it needs.
    /// </summary>
    public string SecretsFilter { get; set; } = "*";
    public bool RunAsPreJob { get; set; } = true;

    /// <summary>The names in <see cref="SecretsFilter"/>, each once; empty when every secret is fetched.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> SecretNames =>
        (SecretsFilter ?? string.Empty)
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name != "*")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>Operating system of the servers. Used by Docker and Custom; the other kinds imply it.</summary>
    public ServerOs ServerOs { get; set; } = ServerOs.Windows;

    /// <summary>
    /// Who runs the deploy steps on your own servers: an agent on each server, or the build agent over
    /// the network (WinRM to Windows servers, SSH to Linux servers).
    /// </summary>
    public DeployFrom RunFrom { get; set; } = DeployFrom.Server;

    /// <summary>Account the build agent logs in as on Linux servers (<see cref="DeployFrom.Agent"/>).</summary>
    public string? SshUser { get; set; }

    /// <summary>Playbook in the repository (Ansible).</summary>
    public string? PlaybookPath { get; set; }

    /// <summary>Inventory in the repository; <c>{environment}</c> is replaced per environment (Ansible).</summary>
    public string? InventoryPath { get; set; }

    /// <summary>Name of the secure file holding the SSH key; empty when the agent has its own key (Ansible).</summary>
    public string? AnsibleSshKeyFile { get; set; }

    /// <summary>
    /// Agent pool for the deploy job when it differs from the build's, e.g. a Linux pool for Ansible
    /// when the build runs on Windows. Only used when the deploy job runs on an agent.
    /// </summary>
    public string? AgentPool { get; set; }

    /// <summary>Folder the app is deployed to (IIS site folder, service folder or file share).</summary>
    public string? TargetPath { get; set; }

    /// <summary>Service name: a Windows service (WindowsService) or a systemd unit (LinuxService).</summary>
    public string? ServiceName { get; set; }

    /// <summary>IIS website name (Iis).</summary>
    public string? WebsiteName { get; set; }

    /// <summary>Container name (DockerContainer).</summary>
    public string? ContainerName { get; set; }

    /// <summary>Published ports, each as <c>host:container</c>, e.g. <c>8080:80</c> (DockerContainer).</summary>
    public IReadOnlyList<string> ContainerPorts { get; set; } = Array.Empty<string>();

    /// <summary>Environment variables for the container, each as <c>NAME=value</c> (DockerContainer).</summary>
    public IReadOnlyList<string> ContainerEnvironment { get; set; } = Array.Empty<string>();

    /// <summary>Script run by the Custom deploy step. Empty means a reminder to write one.</summary>
    public string? CustomScript { get; set; }

    /// <summary>Kubernetes service connection (Kubernetes).</summary>
    public string? KubernetesServiceConnection { get; set; }

    /// <summary>Kubernetes namespace (Kubernetes).</summary>
    public string? KubernetesNamespace { get; set; }

    /// <summary>Manifest files in the repository, e.g. <c>manifests/*.yaml</c> (Kubernetes).</summary>
    public string? ManifestsPath { get; set; }

    /// <summary>Name of the Kubernetes deployment, used to roll back (Kubernetes).</summary>
    public string? KubernetesDeploymentName { get; set; }

    [JsonIgnore]
    public string TargetPathOrDefault => string.IsNullOrWhiteSpace(TargetPath) ? "$(DEPLOY_PATH)" : TargetPath;
    [JsonIgnore]
    public string ServiceNameOrDefault => string.IsNullOrWhiteSpace(ServiceName) ? "$(SERVICE_NAME)" : ServiceName;
    [JsonIgnore]
    public string WebsiteNameOrDefault => string.IsNullOrWhiteSpace(WebsiteName) ? "Default Web Site" : WebsiteName;
    [JsonIgnore]
    public string KubernetesServiceConnectionOrDefault => string.IsNullOrWhiteSpace(KubernetesServiceConnection) ? "$(K8S_SERVICE_CONNECTION)" : KubernetesServiceConnection;
    [JsonIgnore]
    public string KubernetesNamespaceOrDefault => string.IsNullOrWhiteSpace(KubernetesNamespace) ? "$(K8S_NAMESPACE)" : KubernetesNamespace;
    [JsonIgnore]
    public string ManifestsPathOrDefault => string.IsNullOrWhiteSpace(ManifestsPath) ? "manifests/*.yaml" : ManifestsPath;
    [JsonIgnore]
    public string KubernetesDeploymentNameOrDefault => string.IsNullOrWhiteSpace(KubernetesDeploymentName) ? "$(K8S_DEPLOYMENT)" : KubernetesDeploymentName;
    [JsonIgnore]
    public string PlaybookPathOrDefault => string.IsNullOrWhiteSpace(PlaybookPath) ? "site.yml" : PlaybookPath.Trim();
    [JsonIgnore]
    public string InventoryPathOrDefault => string.IsNullOrWhiteSpace(InventoryPath) ? "inventories/{environment}" : InventoryPath.Trim();
    [JsonIgnore]
    public string SshUserOrDefault => string.IsNullOrWhiteSpace(SshUser) ? "$(SSH_USER)" : SshUser.Trim();
    [JsonIgnore]
    public string ContainerNameOrDefault => string.IsNullOrWhiteSpace(ContainerName) ? "$(CONTAINER_NAME)" : ContainerName;
}

/// <summary>
/// An approval written in the pipeline file: the pipeline waits for a person before it deploys to
/// the listed environments. The approval on an Azure DevOps environment is separate and stays there.
/// </summary>
public sealed class ApprovalConfig
{
    /// <summary>Shortest and longest waiting time: one hour to four weeks.</summary>
    public const int MinWaitHours = 1;
    public const int MaxWaitHours = 672;

    /// <summary>Environments that wait for approval before they are deployed to.</summary>
    public IReadOnlyList<string> Environments { get; set; } = Array.Empty<string>();

    /// <summary>Users or groups who may approve, comma-separated. Empty means everyone who may start the pipeline.</summary>
    public string? Approvers { get; set; }

    /// <summary>Users or groups who get the email, comma-separated.</summary>
    public string? NotifyUsers { get; set; }

    /// <summary>How long the pipeline waits before it rejects the deployment by itself.</summary>
    public int WaitHours { get; set; } = 24;

    [JsonIgnore]
    public int WaitMinutes => Math.Clamp(WaitHours, MinWaitHours, MaxWaitHours) * 60;
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

    /// <summary>Root folder for backups on Windows servers. Each environment and build gets its own subfolder.</summary>
    [JsonIgnore]
    public string BackupRootOrDefault => string.IsNullOrWhiteSpace(BackupPath) ? @"D:\backups" : BackupPath;

    /// <summary>Root folder for backups on Linux servers.</summary>
    [JsonIgnore]
    public string LinuxBackupRootOrDefault => string.IsNullOrWhiteSpace(BackupPath) ? "/var/backups/pipelinebuilder" : BackupPath;
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

public sealed class DeploymentStrategyConfig
{
    public DeploymentStrategyType StrategyType { get; set; } = DeploymentStrategyType.Standard;
    public int BatchSize { get; set; } = 1;
    public bool RollbackOnFailure { get; set; } = true;
}

public sealed class SecretGovernanceResult
{
    public bool HasPlainTextSecret { get; set; }
    public string? SecretLocation { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public SecretSeverity Severity { get; set; }
}
