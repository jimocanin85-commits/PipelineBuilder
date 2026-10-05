using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Models;

/// <summary>One thing the generated pipeline needs in Azure DevOps: an environment, a connection, a variable.</summary>
public sealed class PipelineRequirement
{
    public RequirementKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>What it is used for, in a few words.</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Where in Azure DevOps it is created, as a menu path.</summary>
    public string Where { get; init; } = string.Empty;

    /// <summary>A variable that should be marked as secret.</summary>
    public bool IsSecret { get; init; }
}
