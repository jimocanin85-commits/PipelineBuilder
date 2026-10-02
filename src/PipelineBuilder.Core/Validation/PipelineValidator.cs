using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Deployment;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// The one place validation findings come from: an ordered chain of <see cref="ValidationRule"/>s.
/// </summary>
public sealed class PipelineValidator : IPipelineValidator
{
    public PipelineValidator(IEnumerable<ValidationRule> rules)
    {
        Rules = rules?.ToList() ?? throw new ArgumentNullException(nameof(rules));

        var duplicates = Rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new ArgumentException($"Validation rule ids must be unique. Duplicates: {string.Join(", ", duplicates)}", nameof(rules));
    }

    /// <summary>A validator with the built-in rules, for use without dependency injection.</summary>
    public static PipelineValidator CreateDefault() => new(BuiltInRules.Create(
        DeploymentKindRegistry.CreateDefault(), new VariableGroupService(), new SecretsGovernanceService(), new KeyVaultYamlService()));

    /// <summary>The rules in the order they run.</summary>
    public IReadOnlyList<ValidationRule> Rules { get; }

    public IReadOnlyList<ValidationResult> ValidateInput(PipelineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Run(ValidationStage.Input, new ValidationContext(definition));
    }

    public IReadOnlyList<ValidationResult> ValidateGenerated(PipelineDefinition definition, string yaml)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Run(ValidationStage.Generated, new ValidationContext(definition, yaml));
    }

    private List<ValidationResult> Run(ValidationStage stage, ValidationContext context) =>
        Rules.Where(r => r.Stage == stage).SelectMany(r => r.Check(context)).ToList();
}
