using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// The template catalogue: the built-in <c>Templates/templates.json</c>, plus an optional file of
/// your own templates (<see cref="PipelineBuilderOptions.TemplatesFile"/>).
/// </summary>
public sealed class TemplateCatalogue : ITemplateCatalogue
{
    private const string BuiltInResource = "PipelineBuilder.Core.Templates.templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<PipelineTemplate> _templates;

    public TemplateCatalogue() : this(new PipelineBuilderOptions())
    {
    }

    public TemplateCatalogue(PipelineBuilderOptions options)
    {
        var templates = LoadBuiltIn().ToList();
        if (!string.IsNullOrWhiteSpace(options.TemplatesFile))
        {
            foreach (var custom in LoadFile(options.TemplatesFile))
            {
                templates.RemoveAll(t => t.Id.Equals(custom.Id, StringComparison.OrdinalIgnoreCase));
                templates.Add(custom);
            }
        }
        _templates = templates;
    }

    public IReadOnlyList<PipelineTemplate> GetAllTemplates() => _templates;

    public PipelineTemplate? GetById(string id) =>
        _templates.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool ApplyTo(string templateId, PipelineDefinition definition)
    {
        var template = GetById(templateId);
        if (template == null)
            return false;

        var s = template.Settings;
        definition.TemplateId = template.Id;
        if (s.ProjectType is { } projectType) definition.ProjectType = projectType;
        if (s.Environments is { Count: > 0 } environments) definition.Environments = environments.ToList();
        if (s.ArtifactType is { } artifactType) definition.Artifact.ArtifactType = artifactType;
        if (s.DeploymentKind is { } kind) definition.Deployment.Kind = kind;
        if (s.ServerOs is { } os) definition.Deployment.ServerOs = os;
        if (s.Strategy is { } strategy) definition.DeploymentStrategy.StrategyType = strategy;
        if (s.RollbackEnabled is { } rollback) definition.Rollback.Enabled = rollback;
        if (s.CustomDeployScript is { } script) definition.Deployment.CustomScript = script;
        return true;
    }

    /// <summary>The templates shipped with PipelineBuilder.</summary>
    public static IReadOnlyList<PipelineTemplate> LoadBuiltIn()
    {
        using var stream = typeof(TemplateCatalogue).Assembly.GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException($"Embedded resource '{BuiltInResource}' is missing.");
        return Parse(stream, "built-in templates");
    }

    /// <exception cref="InvalidDataException">The file is missing or not a valid template list.</exception>
    public static IReadOnlyList<PipelineTemplate> LoadFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Templates file '{path}' was not found.");
        using var stream = File.OpenRead(path);
        return Parse(stream, path);
    }

    private static List<PipelineTemplate> Parse(Stream stream, string source)
    {
        List<PipelineTemplate>? templates;
        try
        {
            templates = JsonSerializer.Deserialize<List<PipelineTemplate>>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Templates in {source} are not valid: {ex.Message}", ex);
        }

        if (templates == null || templates.Count == 0)
            throw new InvalidDataException($"No templates found in {source}.");
        var invalid = templates.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name));
        if (invalid != null)
            throw new InvalidDataException($"Every template in {source} needs an id and a name.");
        var duplicate = templates.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidDataException($"Template id '{duplicate.Key}' appears more than once in {source}.");
        return templates;
    }
}
