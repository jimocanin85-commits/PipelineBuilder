namespace PipelineBuilder.Core.Enums;

public enum VariableGroupScope { Pipeline, Stage, Environment }
public enum ArtifactType { PipelineArtifact, BuildArtifact, ZipPackage, DockerImage }
public enum HealthCheckType { HttpEndpoint, IisAppPool, WindowsService, PortCheck, CustomPowerShell }
public enum NotificationType { TeamsWebhook, Email, CustomWebhook }
public enum DeploymentStrategyType { Standard, Rolling }
public enum ValidationSeverity { Info, Warning, Error }
/// <summary>Input rules block generation; generated rules judge the finished pipeline.</summary>
public enum ValidationStage { Input, Generated }
public enum RollbackTarget { Iis, WindowsService, FileShare, DockerContainer, Kubernetes, LinuxService }

/// <summary>What the deploy step does with the artifact. <see cref="Custom"/> runs the user's own script.</summary>
public enum DeploymentKind { Custom, Iis, WindowsService, FileShare, DockerContainer, Kubernetes, LinuxService }

/// <summary>The operating system of the servers a deployment runs on.</summary>
public enum ServerOs { Windows, Linux }

/// <summary>The shell a generated script step is written in.</summary>
public enum ScriptShell { PowerShell, Bash }
public enum SecretSeverity { Info, Warning, Error }
public enum ProjectType { DotNet, Node, Docker }
public enum BuildAgentType { MicrosoftHosted, SelfHosted }

/// <summary>Something that must exist in Azure DevOps before the pipeline can run.</summary>
public enum RequirementKind { Environment, ServiceConnection, VariableGroup, Variable }
