using System.Text.RegularExpressions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// Problems in the settings that would make the generated YAML invalid. These block generation.
/// Each finding names the setting it is about (<see cref="ValidationResult.AffectedField"/>), so the
/// wizard can show it on the right step.
/// </summary>
internal static class InputRules
{
    public static IEnumerable<ValidationRule> All() => new[]
    {
        Rule("input.name", Name),
        Rule("input.environments", Environments),
        Rule("input.release-branch", ReleaseBranch),
        Rule("input.pool-name", PoolName),
        Rule("input.artifact", Artifact),
        Rule("input.canary-percentage", CanaryPercentage),
        Rule("input.naming-convention", NamingConvention),
        Rule("input.health-check-url", HealthCheckUrl),
        Rule("input.terraform-directory", TerraformDirectory),
    };

    private static ValidationRule Rule(string id, Func<ValidationContext, IEnumerable<ValidationResult>> check) =>
        new(id, ValidationStage.Input, check);

    private static ValidationResult Error(string field, string message, string? fix = null) =>
        new() { Severity = ValidationSeverity.Error, AffectedField = field, Message = message, SuggestedFix = fix };

    private static IEnumerable<ValidationResult> Name(ValidationContext context)
    {
        var name = context.Definition.Name;
        if (string.IsNullOrWhiteSpace(name))
            yield return Error(nameof(PipelineDefinition.Name), "Pipeline name is required and cannot be empty.");
        if (name?.Length > 255)
            yield return Error(nameof(PipelineDefinition.Name), "Pipeline name cannot exceed 255 characters.");
        if (name?.Any(char.IsControl) == true)
            yield return Error(nameof(PipelineDefinition.Name), "Pipeline name cannot contain line breaks or other control characters.");
    }

    private static IEnumerable<ValidationResult> Environments(ValidationContext context)
    {
        var environments = context.Definition.Environments;
        if (environments == null || environments.Count == 0)
            yield return Error(nameof(PipelineDefinition.Environments), "At least one environment must be specified.", "Add e.g. test, preprod, prod.");

        foreach (var env in environments ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(env))
                yield return Error(nameof(PipelineDefinition.Environments), "Environment names cannot be empty or whitespace.");
            else if (!Regex.IsMatch(env, "^[a-z0-9-]+$"))
                yield return Error(nameof(PipelineDefinition.Environments), $"Environment name '{env}' contains invalid characters. Use lowercase letters, digits and hyphens only.");
        }

        if (environments?.Count > 0 && environments.Count != environments.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            yield return Error(nameof(PipelineDefinition.Environments), "Each environment can only be listed once.");
    }

    private static IEnumerable<ValidationResult> ReleaseBranch(ValidationContext context)
    {
        var branch = context.Definition.ReleaseBranch;
        if (string.IsNullOrWhiteSpace(branch) || !Regex.IsMatch(branch, "^[A-Za-z0-9._/-]+$"))
            yield return Error(nameof(PipelineDefinition.ReleaseBranch), "Release branch must be a branch name such as 'main' (letters, digits, '.', '_', '/', '-').");
    }

    private static IEnumerable<ValidationResult> PoolName(ValidationContext context)
    {
        if (context.Definition.BuildAgent == BuildAgentType.SelfHosted && string.IsNullOrWhiteSpace(context.Definition.PoolName))
            yield return Error(nameof(PipelineDefinition.PoolName), "Pool name is required when using self-hosted build agents.");
    }

    private static IEnumerable<ValidationResult> Artifact(ValidationContext context)
    {
        var artifact = context.Definition.Artifact;
        if (artifact == null)
            yield return Error(nameof(PipelineDefinition.Artifact), "Artifact configuration is required.");
        else if (string.IsNullOrWhiteSpace(artifact.ArtifactName))
            yield return Error(nameof(PipelineDefinition.Artifact), "Artifact name is required.");
    }

    private static IEnumerable<ValidationResult> CanaryPercentage(ValidationContext context)
    {
        var strategy = context.Definition.DeploymentStrategy;
        if (strategy?.StrategyType == DeploymentStrategyType.Canary && strategy.CanaryPercentage is < 0 or > 100)
            yield return Error(nameof(PipelineDefinition.DeploymentStrategy), "Canary deployment percentage must be between 0 and 100.");
    }

    private static IEnumerable<ValidationResult> NamingConvention(ValidationContext context)
    {
        var pattern = context.Definition.Governance?.NamingConvention;
        if (!string.IsNullOrWhiteSpace(pattern) && !IsRegex(pattern))
            yield return Error(nameof(GovernancePolicyConfig.NamingConvention), "Governance naming convention is not a valid regular expression.");
    }

    private static bool IsRegex(string pattern)
    {
        try
        {
            _ = Regex.IsMatch("test", pattern);
            return true;
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    private static IEnumerable<ValidationResult> HealthCheckUrl(ValidationContext context)
    {
        foreach (var hc in context.Definition.HealthChecks ?? Array.Empty<HealthCheckConfig>())
        {
            var url = hc.Url?.Replace("{environment}", "env", StringComparison.OrdinalIgnoreCase);
            if (hc.Enabled && hc.HealthCheckType == HealthCheckType.HttpEndpoint && !Uri.TryCreate(url, UriKind.Absolute, out _))
                yield return Error(nameof(PipelineDefinition.HealthChecks), "HTTP health check requires a valid endpoint URL.", "e.g. https://myapp-{environment}.contoso.com/health");
        }
    }

    private static IEnumerable<ValidationResult> TerraformDirectory(ValidationContext context)
    {
        var iac = context.Definition.IaC;
        if (iac != null && iac.Tool == IaCTool.Terraform && string.IsNullOrWhiteSpace(iac.WorkingDirectory))
            yield return Error(nameof(PipelineDefinition.IaC), "Terraform IaC requires a working directory.");
    }
}
