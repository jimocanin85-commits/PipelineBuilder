using Xunit;
using YamlDotNet.Serialization;

namespace PipelineBuilder.Tests;

/// <summary>Reads a generated pipeline in tests. The deploy stage is written once and repeated per environment.</summary>
internal static class PipelineYaml
{
    /// <summary>The environment's name inside the repeated deploy stage.</summary>
    public const string Environment = "${{ environment }}";

    public const string DeployStageName = "Deploy_${{ replace(environment, '-', '_') }}";

    public static Dictionary<object, object> Parse(string yaml) =>
        Assert.IsType<Dictionary<object, object>>(new DeserializerBuilder().Build().Deserialize<object>(yaml));

    /// <summary>The stages, with the repeated deploy stage listed once.</summary>
    public static List<Dictionary<object, object>> Stages(Dictionary<object, object> root) =>
        Flatten(Assert.IsType<List<object>>(root["stages"])).Cast<Dictionary<object, object>>().ToList();

    public static Dictionary<object, object> Stage(Dictionary<object, object> root, string name) =>
        Assert.Single(Stages(root), s => (string)s["stage"] == name);

    public static Dictionary<object, object> DeployStage(Dictionary<object, object> root) => Stage(root, DeployStageName);

    public static List<Dictionary<object, object>> Jobs(Dictionary<object, object> stage) =>
        Assert.IsType<List<object>>(stage["jobs"]).Cast<Dictionary<object, object>>().ToList();

    public static Dictionary<object, object> DeployJob(Dictionary<object, object> root) =>
        Assert.Single(Jobs(DeployStage(root)), j => j.ContainsKey("deployment"));

    /// <summary>The environments parameter: the list the deploy stage is repeated for.</summary>
    public static List<string> Environments(Dictionary<object, object> root)
    {
        var parameter = Assert.Single(Assert.IsType<List<object>>(root["parameters"]).Cast<Dictionary<object, object>>(),
            p => (string)p["name"] == "environments");
        return Assert.IsType<List<object>>(parameter["default"]).Cast<string>().ToList();
    }

    /// <summary>Stage names a stage depends on, with the repeated deploy stage listed once.</summary>
    public static List<string> DependsOn(Dictionary<object, object> node) =>
        node.GetValueOrDefault("dependsOn") switch
        {
            string single => new List<string> { single },
            List<object> list => Flatten(list).Cast<string>().ToList(),
            _ => new List<string>()
        };

    private static IEnumerable<object> Flatten(IEnumerable<object> items)
    {
        foreach (var item in items)
        {
            if (item is Dictionary<object, object> { Count: 1 } map
                && map.Keys.Single() is string key && key.StartsWith("${{ each ", StringComparison.Ordinal))
            {
                foreach (var inner in Flatten(Assert.IsType<List<object>>(map.Values.Single())))
                    yield return inner;
            }
            else
            {
                yield return item;
            }
        }
    }
}
