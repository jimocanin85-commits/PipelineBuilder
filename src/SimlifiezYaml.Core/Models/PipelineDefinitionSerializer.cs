using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimlifiezYaml.Core.Models;

/// <summary>
/// Saves and loads wizard settings as a JSON file, so a pipeline can be reopened and changed later.
/// The file is wrapped with a format marker so future versions can migrate old files.
/// </summary>
public static class PipelineDefinitionSerializer
{
    public const string Format = "simlifiezyaml-settings/v1";
    public const string FileName = "simlifiezyaml-settings.json";

    /// <summary>Largest settings file accepted, in bytes.</summary>
    public const int MaxFileSize = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class SettingsFile
    {
        public string Format { get; set; } = PipelineDefinitionSerializer.Format;
        public PipelineDefinition? Definition { get; set; }
    }

    public static string ToJson(PipelineDefinition definition) =>
        JsonSerializer.Serialize(new SettingsFile { Definition = definition }, Options);

    /// <exception cref="FormatException">The text is not a SimlifiezYaml settings file.</exception>
    public static PipelineDefinition FromJson(string json)
    {
        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The file is not valid JSON: {ex.Message}", ex);
        }

        if (file?.Format != Format || file.Definition == null)
            throw new FormatException($"The file is not a SimlifiezYaml settings file (expected format '{Format}').");

        return file.Definition;
    }
}
