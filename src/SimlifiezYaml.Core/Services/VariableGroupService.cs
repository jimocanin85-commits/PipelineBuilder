using System.Text;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Services;

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

    public IReadOnlyList<ValidationResult> Validate(IReadOnlyList<VariableGroupConfig> groups, GovernancePolicyConfig? governance)
    {
        var results = new List<ValidationResult>();
        foreach (var g in groups.Where(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Variable group name is required.",
                AffectedField = nameof(VariableGroupConfig.Name),
                SuggestedFix = "Provide a valid Azure DevOps variable group name, e.g. vg-test."
            });
        }

        if (governance?.RequiredVariableGroups.Count > 0)
        {
            foreach (var required in governance.RequiredVariableGroups)
            {
                if (!groups.Any(g => g.Name.Equals(required, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new ValidationResult
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Required variable group '{required}' is missing.",
                        AffectedField = "VariableGroups",
                        SuggestedFix = $"Add variable group '{required}' at pipeline or stage scope."
                    });
                }
            }
        }
        return results;
    }
}
