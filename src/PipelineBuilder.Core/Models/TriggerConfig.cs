namespace PipelineBuilder.Core.Models;

/// <summary>
/// Configuration for pipeline trigger branches and policies.
/// Allows customization of trigger behavior instead of hardcoded values.
/// </summary>
public sealed class TriggerConfig
{
    /// <summary>
    /// Branches to include in the trigger.
    /// Default: main and develop
    /// </summary>
    public IReadOnlyList<string> IncludeBranches { get; set; } = new[] { "main", "develop" };

    /// <summary>
    /// Branches to exclude from the trigger.
    /// </summary>
    public IReadOnlyList<string> ExcludeBranches { get; set; } = Array.Empty<string>();

    /// <summary>
    /// If true, trigger on any branch change. Otherwise use IncludeBranches.
    /// </summary>
    public bool TriggerAll { get; set; } = false;

    /// <summary>
    /// Optional: Path filter for triggers (e.g., "src/*" to only trigger on src changes)
    /// </summary>
    public IReadOnlyList<string> PathFilters { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Creates a default trigger configuration.
    /// </summary>
    public static TriggerConfig Default => new();
}
