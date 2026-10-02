namespace PipelineBuilder.Core.Enums;

public enum VariableGroupScope { Pipeline, Stage, Environment }
public enum ArtifactType { PipelineArtifact, BuildArtifact, ZipPackage, DockerImage }
public enum HealthCheckType { HttpEndpoint, IisAppPool, WindowsService, PortCheck, CustomPowerShell }
public enum NotificationType { TeamsWebhook, Email, CustomWebhook }
public enum DeploymentStrategyType { Standard, Rolling }
public enum ValidationSeverity { Info, Warning, Error }
/// <summary>Input rules block generation; generated rules judge the finished pipeline.</summary>
public enum ValidationStage { Input, Generated }
public enum TemplateCategory { DotNet, Iis, Docker, Kubernetes, WindowsService, FileShare, Node }
public enum TemplateRiskLevel { Low, Medium, High }
public enum RollbackTarget { Iis, WindowsService, FileShare, DockerContainer, Kubernetes }

/// <summary>What the deploy step does with the artifact. <see cref="Custom"/> emits a placeholder script.</summary>
public enum DeploymentKind { Custom, Iis, WindowsService, FileShare, DockerContainer, Kubernetes }
public enum SecretSeverity { Info, Warning, Error }
public enum ProjectType { DotNet, Node, Docker }
public enum BuildAgentType { MicrosoftHosted, SelfHosted }
