namespace PipelineBuilder.Core.DependencyInjection;

public sealed class PipelineBuilderOptions
{
    /// <summary>
    /// Optional path to a JSON file with extra templates (same format as the built-in
    /// <c>Templates/templates.json</c>). A template with the same id replaces the built-in one.
    /// </summary>
    public string? TemplatesFile { get; set; }
}
