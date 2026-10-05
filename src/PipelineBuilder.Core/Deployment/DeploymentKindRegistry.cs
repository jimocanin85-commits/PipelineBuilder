using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// The deployment kinds PipelineBuilder knows. Each kind is one <see cref="IDeploymentKindHandler"/>;
/// a handler registered later for the same kind replaces the built-in one.
/// </summary>
public sealed class DeploymentKindRegistry : IDeploymentKinds
{
    private readonly Dictionary<DeploymentKind, IDeploymentKindHandler> _handlers = new();

    public DeploymentKindRegistry(IEnumerable<IDeploymentKindHandler> handlers)
    {
        foreach (var handler in handlers)
            _handlers[handler.Kind] = handler;

        if (!_handlers.ContainsKey(DeploymentKind.Custom))
            throw new ArgumentException("A handler for the Custom deployment kind is required.", nameof(handlers));

        All = _handlers.Values.OrderBy(h => h.Kind).ToList();
    }

    /// <summary>The handlers that ship with PipelineBuilder, one per <see cref="DeploymentKind"/>.</summary>
    public static IReadOnlyList<IDeploymentKindHandler> BuiltInHandlers() => new IDeploymentKindHandler[]
    {
        new CustomDeploymentHandler(),
        new IisDeploymentHandler(),
        new WindowsServiceDeploymentHandler(),
        new FileShareDeploymentHandler(),
        new DockerContainerDeploymentHandler(),
        new KubernetesDeploymentHandler(),
        new LinuxServiceDeploymentHandler(),
        new AnsibleDeploymentHandler(),
    };

    /// <summary>A registry with the built-in handlers, for use without dependency injection.</summary>
    public static DeploymentKindRegistry CreateDefault() => new(BuiltInHandlers());

    public IReadOnlyList<IDeploymentKindHandler> All { get; }

    public IDeploymentKindHandler For(DeploymentKind kind) =>
        _handlers.TryGetValue(kind, out var handler) ? handler : _handlers[DeploymentKind.Custom];

    public bool UsesServerResources(PipelineDefinition definition) =>
        For(definition.Deployment.Kind).RunsOnServers && definition.Deployment.RunFrom == DeployFrom.Server;

    public bool DeploysFromAgentToServers(PipelineDefinition definition) =>
        For(definition.Deployment.Kind).RunsOnServers && definition.Deployment.RunFrom == DeployFrom.Agent;

    public ScriptShell ShellFor(PipelineDefinition definition) =>
        For(definition.Deployment.Kind).Shell(definition.Deployment);

    public IReadOnlyList<string> GenerateBackupSteps(PipelineDefinition definition, string environment)
    {
        if (!RollsBack(definition)) return Array.Empty<string>();
        return RollbackHandler(definition).GenerateBackupSteps(definition.Rollback, definition.Deployment, environment);
    }

    public IReadOnlyList<string> GenerateRollbackSteps(PipelineDefinition definition, string environment)
    {
        var config = definition.Rollback;
        if (!RollsBack(definition)) return Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(config.RollbackScript))
            return new[] { YamlBuilder.ScriptStep(config.RollbackScript, "Execute custom rollback script") };

        return RollbackHandler(definition).GenerateRollbackSteps(config, definition.Deployment, environment);
    }

    private bool RollsBack(PipelineDefinition definition) =>
        definition.Rollback.Enabled && For(definition.Deployment.Kind).SupportsRollback;

    /// <summary>The kind's own handler, or for a kind without a rollback target (Custom), the chosen target's handler.</summary>
    private IDeploymentKindHandler RollbackHandler(PipelineDefinition definition)
    {
        var own = For(definition.Deployment.Kind);
        if (own.RollbackTarget != null)
            return own;

        return All.FirstOrDefault(h => h.RollbackTarget == definition.Rollback.Target) ?? own;
    }
}
