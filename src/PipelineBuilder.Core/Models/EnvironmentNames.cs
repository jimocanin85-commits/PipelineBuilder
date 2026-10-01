namespace PipelineBuilder.Core.Models;

/// <summary>One place that decides which environments count as production.</summary>
public static class EnvironmentNames
{
    private static readonly HashSet<string> Production = new(StringComparer.OrdinalIgnoreCase) { "prod", "production" };

    /// <summary>True for <c>prod</c> and <c>production</c> (any casing).</summary>
    public static bool IsProduction(string environment) => Production.Contains(environment);
}
