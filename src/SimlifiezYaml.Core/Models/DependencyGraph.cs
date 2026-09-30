using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Models;

public sealed class DependencyGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public IReadOnlyList<string> DependsOn { get; set; } = Array.Empty<string>();
    public string? Condition { get; set; }

    /// <summary>True for environments that should be gated by an approval in Azure DevOps.</summary>
    public bool ManualPromotion { get; set; }
}

/// <summary>
/// Summary of the stages the generator produces, for display in the wizard.
/// Mirrors <c>PipelineGeneratorService</c>: Build → Test → Artifact → Deploy_{env}… → Notify.
/// </summary>
public sealed class PipelineDependencyGraph
{
    private static readonly HashSet<string> ProductionNames = new(StringComparer.OrdinalIgnoreCase) { "prod", "production" };

    public IReadOnlyList<DependencyGraphNode> Nodes { get; set; } = Array.Empty<DependencyGraphNode>();

    public static PipelineDependencyGraph FromDefinition(PipelineDefinition definition)
    {
        var nodes = new List<DependencyGraphNode>
        {
            new() { Id = "Build", DisplayName = "Build" },
            new() { Id = "Test", DisplayName = "Test", DependsOn = new[] { "Build" }, Condition = "succeeded()" },
            new() { Id = "Artifact", DisplayName = "Artifact", DependsOn = new[] { "Test" }, Condition = "succeeded()" }
        };

        var previous = "Artifact";
        foreach (var env in definition.Environments)
        {
            var id = $"Deploy_{YamlBuilder.ToIdentifier(env)}";
            var isProduction = ProductionNames.Contains(env);
            nodes.Add(new DependencyGraphNode
            {
                Id = id,
                DisplayName = $"Deploy {env}",
                DependsOn = new[] { previous },
                Condition = isProduction ? "succeeded() + main branch" : "succeeded()",
                ManualPromotion = isProduction || env.Contains("prod", StringComparison.OrdinalIgnoreCase)
            });
            previous = id;
        }

        if (definition.Notifications.Any(n => n.NotifyOnSuccess))
            nodes.Add(new DependencyGraphNode { Id = "Notify_Success", DisplayName = "Notify on success", DependsOn = new[] { previous }, Condition = "succeeded()" });
        if (definition.Notifications.Any(n => n.NotifyOnFailure))
            nodes.Add(new DependencyGraphNode { Id = "Notify_Failure", DisplayName = "Notify on failure", DependsOn = new[] { "any stage" }, Condition = "failed()" });

        return new PipelineDependencyGraph { Nodes = nodes };
    }
}
