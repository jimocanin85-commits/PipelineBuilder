using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Models;

/// <summary>A starting point in the template catalogue (<c>Templates/templates.json</c>).</summary>
public sealed class PipelineTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TemplateCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public TemplateRiskLevel RiskLevel { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    /// <summary>Settings applied to the wizard when the template is chosen. Missing values are left unchanged.</summary>
    public TemplateSettings Settings { get; set; } = new();
}

public sealed class TemplateSettings
{
    public ProjectType? ProjectType { get; set; }
    public DeploymentTarget? DeploymentTarget { get; set; }
    public IReadOnlyList<string>? Environments { get; set; }
    public ArtifactType? ArtifactType { get; set; }
    public DeploymentKind? DeploymentKind { get; set; }
    public DeploymentStrategyType? Strategy { get; set; }
    public bool? RollbackEnabled { get; set; }
    public IaCTool? IacTool { get; set; }
    public string? IacWorkingDirectory { get; set; }
    public string? CustomDeployScript { get; set; }
}
