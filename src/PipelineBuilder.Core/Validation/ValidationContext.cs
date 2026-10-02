using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Validation;

/// <summary>What a rule can look at: the settings and, after generation, the pipeline YAML.</summary>
public sealed class ValidationContext
{
    public ValidationContext(PipelineDefinition definition, string? yaml = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Yaml = yaml ?? string.Empty;
    }

    public PipelineDefinition Definition { get; }

    /// <summary>The generated pipeline; empty for input rules, which run before generation.</summary>
    public string Yaml { get; }
}
