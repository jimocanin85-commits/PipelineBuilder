using System.Text;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class VariableGroupService : IVariableGroupService
{
    public string GeneratePipelineVariables(PipelineDefinition definition)
    {
        // Always emitted: the build, test and publish steps use $(BuildConfiguration).
        // Stage-scoped groups are treated as pipeline scope (stages have no separate setting).
        var pipelineGroups = definition.VariableGroups
            .Where(g => g.Scope is VariableGroupScope.Pipeline or VariableGroupScope.Stage)
            .ToList();

        var sb = new StringBuilder("variables:");
        sb.AppendLine();
        foreach (var g in pipelineGroups)
        {
            sb.AppendLine($"  - group: {YamlBuilder.YamlString(g.Name)}");
        }
        sb.AppendLine("  - name: BuildConfiguration");
        sb.AppendLine("    value: Release");
        return sb.ToString().TrimEnd();
    }

    public string GenerateStageVariables(IReadOnlyList<VariableGroupConfig> groups)
    {
        if (groups.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var g in groups)
            sb.AppendLine($"    - group: {YamlBuilder.YamlString(g.Name)}");
        return sb.ToString().TrimEnd();
    }

    public IReadOnlyList<ValidationResult> Validate(IReadOnlyList<VariableGroupConfig> groups)
    {
        var results = new List<ValidationResult>();
        foreach (var g in groups.Where(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Variable group name is required.",
                AffectedField = "VariableGroups",
                SuggestedFix = "Provide a valid Azure DevOps variable group name, e.g. vg-test."
            });
        }

        // Least privilege: secrets shared at pipeline level reach every job, including the build.
        foreach (var g in groups.Where(x => x.ContainsSecrets && x.Scope != VariableGroupScope.Environment && !string.IsNullOrWhiteSpace(x.Name)))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Warning,
                Message = $"Variable group '{g.Name}' contains secrets but is available to the whole pipeline, including the build.",
                AffectedField = "VariableGroups",
                SuggestedFix = "Set its scope to Environment so only the deployments that need the secrets can read them."
            });
        }

        return results;
    }
}
