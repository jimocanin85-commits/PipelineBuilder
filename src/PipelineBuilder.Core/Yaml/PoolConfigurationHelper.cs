using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Helper class to generate consistent pool configuration across all stage generators.
/// Centralizes pool strategy logic to prevent duplication and inconsistencies.
/// </summary>
public static class PoolConfigurationHelper
{
    /// <summary>
    /// Generates the pool configuration YAML for build agents.
    /// </summary>
    /// <param name="buildAgent">The build agent type (Microsoft-hosted or self-hosted)</param>
    /// <param name="poolName">The name of the self-hosted pool (used only if buildAgent is SelfHosted)</param>
    /// <returns>Pool configuration YAML string in format: "name: 'PoolName'" or "vmImage: 'windows-latest'"</returns>
    public static string GeneratePoolConfiguration(BuildAgentType buildAgent, string? poolName)
    {
        if (buildAgent == BuildAgentType.SelfHosted)
        {
            // Validate pool name for self-hosted agents
            var validPoolName = string.IsNullOrWhiteSpace(poolName) ? "Default" : poolName;
            return $"name: '{validPoolName}'";
        }

        // Default to Microsoft-hosted Windows image
        return "vmImage: 'windows-latest'";
    }

    /// <summary>
    /// Pool for the Build stage. Microsoft-hosted builds use Linux, which starts and builds faster;
    /// self-hosted builds use the chosen pool.
    /// </summary>
    public static string GenerateBuildPoolConfiguration(BuildAgentType buildAgent, string? poolName) =>
        buildAgent == BuildAgentType.SelfHosted
            ? GeneratePoolConfiguration(buildAgent, poolName)
            : "vmImage: 'ubuntu-latest'";
}
