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
        Rule("input.docker-artifact", DockerArtifact),
        Rule("input.health-check-url", HealthCheckUrl),
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
            yield return Error(nameof(PipelineDefinition.PoolName), "Enter the agent pool to build on.", "Type its name under 'Build agent', or untick 'Build on our own agent pool'.");
    }

    private static IEnumerable<ValidationResult> Artifact(ValidationContext context)
    {
        var artifact = context.Definition.Artifact;
        if (artifact == null)
            yield return Error(nameof(PipelineDefinition.Artifact), "Artifact configuration is required.");
        else if (string.IsNullOrWhiteSpace(artifact.ArtifactName))
            yield return Error(nameof(PipelineDefinition.Artifact),
                artifact.ArtifactType == ArtifactType.DockerImage ? "The image needs a name." : "The artifact needs a name.");
    }

    private static IEnumerable<ValidationResult> DockerArtifact(ValidationContext context)
    {
        var definition = context.Definition;
        if (definition.ProjectType == ProjectType.Docker && definition.Artifact?.ArtifactType != ArtifactType.DockerImage)
            yield return Error(nameof(PipelineDefinition.Artifact), "A Docker project is packaged as a Docker image.", "Pick one of the Docker templates on the first step.");
    }

    private static IEnumerable<ValidationResult> HealthCheckUrl(ValidationContext context)
    {
        foreach (var hc in context.Definition.HealthChecks ?? Array.Empty<HealthCheckConfig>())
        {
            var url = hc.Url?.Replace("{environment}", "env", StringComparison.OrdinalIgnoreCase);
            if (hc.Enabled && hc.HealthCheckType == HealthCheckType.HttpEndpoint && !Uri.TryCreate(url, UriKind.Absolute, out _))
                yield return Error(nameof(PipelineDefinition.HealthChecks), "The HTTP health check needs a full address.", "e.g. https://myapp-{environment}.contoso.com/health");
        }
    }
}
