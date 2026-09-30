using System.Text.RegularExpressions;
using SimlifiezYaml.Core.Enums;

namespace SimlifiezYaml.Core.Models;

/// <summary>
/// Checks a <see cref="PipelineDefinition"/> for problems that would make the generated YAML invalid.
/// These block generation; softer, policy-level findings come from <c>GovernanceValidationService</c>.
/// Each result names the setting it is about (<see cref="ValidationResult.AffectedField"/>), so the
/// wizard can show it on the right step.
/// </summary>
public static class PipelineDefinitionValidator
{
    /// <summary>All blocking problems, each with the setting it concerns.</summary>
    public static IReadOnlyList<ValidationResult> ValidateDetailed(PipelineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var results = new List<ValidationResult>();
        void Error(string field, string message, string? fix = null) =>
            results.Add(new ValidationResult { Severity = ValidationSeverity.Error, AffectedField = field, Message = message, SuggestedFix = fix });

        if (string.IsNullOrWhiteSpace(definition.Name))
            Error(nameof(PipelineDefinition.Name), "Pipeline name is required and cannot be empty.");
        if (definition.Name?.Length > 255)
            Error(nameof(PipelineDefinition.Name), "Pipeline name cannot exceed 255 characters.");
        if (definition.Name?.Any(char.IsControl) == true)
            Error(nameof(PipelineDefinition.Name), "Pipeline name cannot contain line breaks or other control characters.");

        if (definition.Environments == null || definition.Environments.Count == 0)
            Error(nameof(PipelineDefinition.Environments), "At least one environment must be specified.", "Add e.g. test, preprod, prod.");
        foreach (var env in definition.Environments ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(env))
                Error(nameof(PipelineDefinition.Environments), "Environment names cannot be empty or whitespace.");
            else if (!Regex.IsMatch(env, "^[a-z0-9-]+$"))
                Error(nameof(PipelineDefinition.Environments), $"Environment name '{env}' contains invalid characters. Use lowercase letters, digits and hyphens only.");
        }
        if (definition.Environments?.Count > 0 && definition.Environments.Count != definition.Environments.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            Error(nameof(PipelineDefinition.Environments), "Each environment can only be listed once.");

        if (string.IsNullOrWhiteSpace(definition.ReleaseBranch) || !Regex.IsMatch(definition.ReleaseBranch, "^[A-Za-z0-9._/-]+$"))
            Error(nameof(PipelineDefinition.ReleaseBranch), "Release branch must be a branch name such as 'main' (letters, digits, '.', '_', '/', '-').");

        if (definition.BuildAgent == BuildAgentType.SelfHosted && string.IsNullOrWhiteSpace(definition.PoolName))
            Error(nameof(PipelineDefinition.PoolName), "Pool name is required when using self-hosted build agents.");

        if (definition.Artifact == null)
            Error(nameof(PipelineDefinition.Artifact), "Artifact configuration is required.");
        else if (string.IsNullOrWhiteSpace(definition.Artifact.ArtifactName))
            Error(nameof(PipelineDefinition.Artifact), "Artifact name is required.");

        if (definition.DeploymentStrategy?.StrategyType == DeploymentStrategyType.Canary
            && definition.DeploymentStrategy.CanaryPercentage is < 0 or > 100)
            Error(nameof(PipelineDefinition.DeploymentStrategy), "Canary deployment percentage must be between 0 and 100.");

        if (definition.Governance != null && !string.IsNullOrWhiteSpace(definition.Governance.NamingConvention))
        {
            try
            {
                _ = Regex.IsMatch("test", definition.Governance.NamingConvention);
            }
            catch (RegexParseException)
            {
                Error(nameof(GovernancePolicyConfig.NamingConvention), "Governance naming convention is not a valid regular expression.");
            }
        }

        foreach (var hc in definition.HealthChecks ?? Array.Empty<HealthCheckConfig>())
        {
            var url = hc.Url?.Replace("{environment}", "env", StringComparison.OrdinalIgnoreCase);
            if (hc.Enabled && hc.HealthCheckType == HealthCheckType.HttpEndpoint && !Uri.TryCreate(url, UriKind.Absolute, out _))
                Error(nameof(PipelineDefinition.HealthChecks), "HTTP health check requires a valid endpoint URL.", "e.g. https://myapp-{environment}.contoso.com/health");
        }

        if (definition.IaC != null && definition.IaC.Tool == IaCTool.Terraform && string.IsNullOrWhiteSpace(definition.IaC.WorkingDirectory))
            Error(nameof(PipelineDefinition.IaC), "Terraform IaC requires a working directory.");

        return results;
    }

    /// <summary>The blocking problems as plain messages.</summary>
    public static IReadOnlyList<string> Validate(PipelineDefinition definition) =>
        ValidateDetailed(definition).Select(r => r.Message).ToList();

    /// <exception cref="ArgumentException">Thrown if there are blocking problems.</exception>
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
}
