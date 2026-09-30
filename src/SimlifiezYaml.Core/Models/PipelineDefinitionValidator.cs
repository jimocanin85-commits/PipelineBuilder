using SimlifiezYaml.Core.Enums;

namespace SimlifiezYaml.Core.Models;

/// <summary>
/// Validates PipelineDefinition instances to ensure required fields and constraints are met.
/// Prevents invalid YAML generation by catching configuration issues early.
/// </summary>
public static class PipelineDefinitionValidator
{
    /// <summary>
    /// Validates the pipeline definition for required fields and constraints.
    /// </summary>
    /// <param name="definition">The pipeline definition to validate</param>
    /// <returns>List of validation errors, or empty if valid</returns>
    public static IReadOnlyList<string> Validate(PipelineDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var errors = new List<string>();

        // Name validation
        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Pipeline name is required and cannot be empty.");

        if (definition.Name?.Length > 255)
            errors.Add("Pipeline name cannot exceed 255 characters.");

        if (definition.Name?.Any(char.IsControl) == true)
            errors.Add("Pipeline name cannot contain line breaks or other control characters.");

        if (string.IsNullOrWhiteSpace(definition.ReleaseBranch)
            || !System.Text.RegularExpressions.Regex.IsMatch(definition.ReleaseBranch, "^[A-Za-z0-9._/-]+$"))
            errors.Add("Release branch must be a branch name such as 'main' (letters, digits, '.', '_', '/', '-').");

        // Environment validation
        if (definition.Environments == null || definition.Environments.Count == 0)
            errors.Add("At least one environment must be specified.");

        // Validate environment names
        foreach (var env in definition.Environments ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(env))
                errors.Add("Environment names cannot be empty or whitespace.");
            else if (!IsValidEnvironmentName(env))
                errors.Add($"Environment name '{env}' contains invalid characters. Use lowercase letters, digits and hyphens only.");
        }

        // Build agent validation
        if (definition.BuildAgent == BuildAgentType.SelfHosted && string.IsNullOrWhiteSpace(definition.PoolName))
            errors.Add("Pool name is required when using self-hosted build agents.");

        // Artifact validation
        if (definition.Artifact == null)
            errors.Add("Artifact configuration is required.");

        // Deployment strategy validation
        if (definition.DeploymentStrategy != null && definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.Canary)
        {
            if (definition.DeploymentStrategy.CanaryPercentage < 0 || definition.DeploymentStrategy.CanaryPercentage > 100)
                errors.Add("Canary deployment percentage must be between 0 and 100.");
        }

        // Governance validation
        if (definition.Governance != null && !string.IsNullOrWhiteSpace(definition.Governance.NamingConvention))
        {
            try
            {
                System.Text.RegularExpressions.Regex.IsMatch("test", definition.Governance.NamingConvention);
            }
            catch (System.Text.RegularExpressions.RegexParseException)
            {
                errors.Add("Governance naming convention is not a valid regular expression.");
            }
        }

        // Health check validation
        if (definition.HealthChecks != null)
        {
            foreach (var hc in definition.HealthChecks)
            {
                if (hc.Enabled && hc.HealthCheckType == HealthCheckType.HttpEndpoint && !Uri.TryCreate(hc.Url?.Replace("{environment}", "env", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out _))
                    errors.Add("HTTP health check requires a valid endpoint URL.");
            }
        }

        // IaC validation
        if (definition.IaC != null && definition.IaC.Tool == IaCTool.Terraform && string.IsNullOrWhiteSpace(definition.IaC.WorkingDirectory))
            errors.Add("Terraform IaC requires a working directory.");

        return errors;
    }

    /// <summary>
    /// Validates that a pipeline definition is valid, throwing if invalid.
    /// </summary>
    /// <param name="definition">The pipeline definition to validate</param>
    /// <exception cref="ArgumentException">Thrown if validation fails</exception>
    public static void ValidateOrThrow(PipelineDefinition definition)
    {
        var errors = Validate(definition);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Pipeline definition validation failed with {errors.Count} error(s):\n" +
                string.Join("\n", errors.Select(e => $"  - {e}")),
                nameof(definition));
        }
    }

    /// <summary>
    /// Checks if an environment name contains only valid characters (lowercase alphanumeric and hyphens).
    /// </summary>
    private static bool IsValidEnvironmentName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9-]+$");
    }
}
