using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipelineBuilder.Core.Models;

/// <summary>
/// Saves and loads wizard settings as a JSON file, so a pipeline can be reopened and changed later.
/// The file is wrapped with a format marker so future versions can migrate old files.
/// </summary>
public static class PipelineDefinitionSerializer
{
    public const string Format = "pipelinebuilder-settings/v1";

    /// <summary>Files saved before the project was renamed from SimlifiezYaml; same content.</summary>
    public const string LegacyFormat = "simlifiezyaml-settings/v1";
    public const string FileName = "pipelinebuilder-settings.json";

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

    /// <summary>
    /// For reading. A settings file can come from anywhere, so a value the wizard counts on must not
    /// be missing: <c>"deployment": null</c> is refused here instead of failing later, mid-click.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new(Options) { RespectNullableAnnotations = true };

    public static string ToJson(PipelineDefinition definition) =>
        JsonSerializer.Serialize(new SettingsFile { Definition = definition }, Options);

    /// <exception cref="FormatException">The text is not a PipelineBuilder settings file.</exception>
    public static PipelineDefinition FromJson(string json)
    {
        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The file is not valid JSON: {ex.Message}", ex);
        }

        if ((file?.Format != Format && file?.Format != LegacyFormat) || file.Definition == null)
            throw new FormatException($"The file is not a PipelineBuilder settings file (expected format '{Format}').");

        RejectEmptyItems(file.Definition);
        return file.Definition;
    }

    /// <summary>A list may be empty, but an item in it may not be <c>null</c>.</summary>
    private static void RejectEmptyItems(PipelineDefinition definition)
    {
        var lists = new IEnumerable<object?>[]
        {
            definition.Environments, definition.VariableGroups, definition.HealthChecks, definition.Notifications,
            definition.Approval.Environments, definition.Deployment.ContainerPorts, definition.Deployment.ContainerEnvironment,
            definition.Trigger.IncludeBranches, definition.Trigger.ExcludeBranches, definition.Trigger.PathFilters
        };
        if (lists.Any(list => list.Contains(null)))
            throw new FormatException("The file has an empty item (null) in a list.");
        if (definition.Notifications.Any(notification => notification.EmailRecipients.Contains(null!)))
            throw new FormatException("The file has an empty item (null) in a list.");
    }
}
