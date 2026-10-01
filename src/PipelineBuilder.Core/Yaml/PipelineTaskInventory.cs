using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Lists the tasks a pipeline actually runs, read from the parsed YAML rather than by searching
/// the text, so a task name in a comment, display name or script is not counted.
/// Step shortcuts count as the task they stand for (<c>script</c> is CmdLine@2, <c>powershell</c>
/// and <c>pwsh</c> are PowerShell@2, <c>bash</c> is Bash@3).
/// </summary>
public static class PipelineTaskInventory
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    private static readonly Dictionary<string, string> Shortcuts = new(StringComparer.Ordinal)
    {
        ["script"] = "CmdLine@2",
        ["powershell"] = "PowerShell@2",
        ["pwsh"] = "PowerShell@2",
        ["bash"] = "Bash@3"
    };

    /// <summary>Every task reference in the pipeline, e.g. <c>DotNetCoreCLI@2</c>, once per step.</summary>
    public static IReadOnlyList<string> FindTasks(string yaml)
    {
        object? document;
        try
        {
            document = Deserializer.Deserialize<object>(yaml);
        }
        catch (YamlException)
        {
            return Array.Empty<string>();
        }

        var tasks = new List<string>();
        Collect(document, tasks);
        return tasks;
    }

    /// <summary>
    /// True when <paramref name="task"/> matches a policy entry. An entry with a version
    /// (<c>CmdLine@2</c>) must match exactly; one without (<c>CmdLine</c>) matches every version.
    /// </summary>
    public static bool Matches(string task, string policyEntry)
    {
        var entry = policyEntry.Trim();
        if (entry.Length == 0) return false;
        return entry.Contains('@')
            ? task.Equals(entry, StringComparison.OrdinalIgnoreCase)
            : task.Split('@')[0].Equals(entry, StringComparison.OrdinalIgnoreCase);
    }

    private static void Collect(object? node, List<string> tasks)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach (var (key, value) in map)
                {
                    if (key is "task" && value is string task)
                        tasks.Add(task.Trim());
                    else if (key is string name && Shortcuts.TryGetValue(name, out var shortcut) && value is string)
                        tasks.Add(shortcut);
                    else
                        Collect(value, tasks);
                }
                break;
            case List<object> list:
                foreach (var item in list)
                    Collect(item, tasks);
                break;
        }
    }
}
