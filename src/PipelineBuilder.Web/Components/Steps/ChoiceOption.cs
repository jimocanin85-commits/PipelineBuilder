namespace PipelineBuilder.Web.Components.Steps;

/// <summary>One option of a Choice: its value, its name, and a line that says what it means.</summary>
public sealed record ChoiceOption<TValue>(TValue Value, string Text, string? Description = null);
