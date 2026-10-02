using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Core.Models;

public sealed class ValidationResult
{
    public ValidationSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? AffectedField { get; init; }
    public string? SuggestedFix { get; init; }

    /// <summary>Id of the rule that produced the finding, e.g. <c>input.environments</c>.</summary>
    public string? RuleId { get; init; }

    /// <summary>A copy of the finding that carries the rule's id.</summary>
    public ValidationResult WithRule(string ruleId) => new()
    {
        Severity = Severity,
        Message = Message,
        AffectedField = AffectedField,
        SuggestedFix = SuggestedFix,
        RuleId = ruleId
    };
}
