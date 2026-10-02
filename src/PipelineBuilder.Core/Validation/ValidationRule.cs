using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// One check with a stable id. Input rules find problems that block generation; generated rules judge
/// the finished pipeline (governance, secrets, advice). Register a <see cref="ValidationRule"/> in
/// dependency injection to add your own.
/// </summary>
public sealed class ValidationRule
{
    private readonly Func<ValidationContext, IEnumerable<ValidationResult>> _check;

    public ValidationRule(string id, ValidationStage stage, Func<ValidationContext, IEnumerable<ValidationResult>> check)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A rule needs an id.", nameof(id));

        Id = id;
        Stage = stage;
        _check = check ?? throw new ArgumentNullException(nameof(check));
    }

    /// <summary>Stable, lower-case id such as <c>input.environments</c>; set on every finding of the rule.</summary>
    public string Id { get; }

    public ValidationStage Stage { get; }

    /// <summary>The rule's findings, each carrying the rule's id.</summary>
    public IEnumerable<ValidationResult> Check(ValidationContext context) =>
        _check(context).Select(finding => finding.RuleId == Id ? finding : finding.WithRule(Id));
}
