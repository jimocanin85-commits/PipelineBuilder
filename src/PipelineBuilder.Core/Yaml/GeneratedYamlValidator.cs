using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Checks a generated pipeline before it is handed to the user: it must parse as YAML, and stage
/// and job names and their <c>dependsOn</c> references must be valid. The generator builds YAML
/// from text fragments, so this is the safety net that turns a generator bug into a clear error
/// instead of a file that Azure DevOps rejects.
/// </summary>
public static class GeneratedYamlValidator
{
    private static readonly Regex Identifier = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static IReadOnlyList<string> Validate(string yaml)
    {
        object? document;
        try
        {
            document = Deserializer.Deserialize<object>(yaml);
        }
        catch (YamlException ex)
        {
            return new[] { $"Not valid YAML (line {ex.Start.Line}): {ex.InnerException?.Message ?? ex.Message}" };
        }

        if (document is not Dictionary<object, object> root)
            return new[] { "The document is not a YAML mapping." };
        if (!root.TryGetValue("stages", out var stagesNode) || stagesNode is not List<object> stageList)
            return new[] { "The pipeline has no 'stages' list." };

        var problems = new List<string>();
        var stages = stageList.OfType<Dictionary<object, object>>().ToList();
        var stageNames = stages.Select(s => s.GetValueOrDefault("stage") as string ?? "").ToList();

        foreach (var name in stageNames.Where(n => !Identifier.IsMatch(n)))
            problems.Add($"Invalid stage name '{name}'.");
        foreach (var duplicate in stageNames.GroupBy(n => n).Where(g => g.Count() > 1))
            problems.Add($"Stage '{duplicate.Key}' is defined more than once.");

        foreach (var stage in stages)
        {
            var stageName = stage.GetValueOrDefault("stage") as string ?? "";
            foreach (var dependency in DependsOn(stage).Where(d => !stageNames.Contains(d)))
                problems.Add($"Stage '{stageName}' depends on unknown stage '{dependency}'.");

            var jobs = (stage.GetValueOrDefault("jobs") as List<object> ?? new()).OfType<Dictionary<object, object>>().ToList();
            var jobNames = jobs.Select(j => (j.GetValueOrDefault("job") ?? j.GetValueOrDefault("deployment")) as string ?? "").ToList();
            foreach (var name in jobNames.Where(n => !Identifier.IsMatch(n)))
                problems.Add($"Invalid job name '{name}' in stage '{stageName}'.");
            foreach (var duplicate in jobNames.GroupBy(n => n).Where(g => g.Count() > 1))
                problems.Add($"Job '{duplicate.Key}' is defined more than once in stage '{stageName}'.");
            foreach (var job in jobs)
            foreach (var dependency in DependsOn(job).Where(d => !jobNames.Contains(d)))
                problems.Add($"A job in stage '{stageName}' depends on unknown job '{dependency}'.");
        }

        return problems;
    }

    private static IEnumerable<string> DependsOn(Dictionary<object, object> node) =>
        node.GetValueOrDefault("dependsOn") switch
        {
            string single => new[] { single },
            List<object> list => list.OfType<string>(),
            _ => Array.Empty<string>()
        };
}
