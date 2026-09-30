namespace SimlifiezYaml.Core.Enums;

public enum VariableGroupScope { Pipeline, Stage, Environment }
public enum ArtifactType { PipelineArtifact, BuildArtifact, ZipPackage, DockerImage, NuGetPackage }
public enum HealthCheckType { HttpEndpoint, IisAppPool, WindowsService, PortCheck, CustomPowerShell }
public enum NotificationType { TeamsWebhook, Email, CustomWebhook }
public enum IaCTool { Terraform, Bicep, ArmTemplate, PowerShell }
public enum DeploymentStrategyType { Standard, Rolling, BlueGreen, Canary, SlotSwap }
public enum ValidationSeverity { Info, Warning, Error }
public enum TemplateCategory { DotNet, Iis, WinRm, Docker, Aks, AzureAppService, Terraform, WindowsService, FileShare, Hybrid, Node }
public enum TemplateRiskLevel { Low, Medium, High }
public enum DeploymentTarget { Cloud, OnPrem, Hybrid }
public enum RollbackTarget { Iis, WindowsService, FileShare, AzureAppServiceSlot, DockerContainer }

/// <summary>What the deploy step does with the artifact. <see cref="Custom"/> emits a placeholder script.</summary>
public enum DeploymentKind { Custom, Iis, WindowsService, FileShare, AzureAppService, DockerContainer }
public enum SecretSeverity { Info, Warning, Error }
public enum ProjectType { DotNet, Node, Docker, Terraform, Hybrid, Unknown }
public enum BuildAgentType { MicrosoftHosted, SelfHosted }
