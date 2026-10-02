using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Yaml;

public static class PoolConfigurationHelper
{
    /// <summary>
    /// The agent pool for the build, for deployments that run from an agent (Kubernetes) and for
    /// notifications. Deployments to your own servers run on those servers, not on this pool.
    /// </summary>
    public static string GeneratePoolConfiguration(BuildAgentType buildAgent, string? poolName)
    {
        if (buildAgent == BuildAgentType.SelfHosted)
            return $"name: '{(string.IsNullOrWhiteSpace(poolName) ? "Default" : poolName)}'";

        return "vmImage: 'ubuntu-latest'";
    }
}
