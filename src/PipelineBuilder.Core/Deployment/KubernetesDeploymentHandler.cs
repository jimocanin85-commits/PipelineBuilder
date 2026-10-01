using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Applies the repository's manifests with the image built by this run, from a pipeline agent.
/// Rollback is <c>kubectl rollout undo</c>, so there is no backup step.
/// </summary>
public sealed class KubernetesDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.Kubernetes;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.Kubernetes;
    public override bool DeploysFromAgentOnly => true;
    public override bool NeedsRepositoryCheckout => true;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) => new[]
    {
        YamlBuilder.Task("KubernetesManifest@1", new Dictionary<string, string>
        {
            ["action"] = "deploy",
            ["connectionType"] = "kubernetesServiceConnection",
            ["kubernetesServiceConnection"] = definition.Deployment.KubernetesServiceConnectionOrDefault,
            ["namespace"] = definition.Deployment.KubernetesNamespaceOrDefault,
            ["manifests"] = definition.Deployment.ManifestsPathOrDefault,
            ["containers"] = $"$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)"
        }, $"Deploy to Kubernetes ({environment})")
    };

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.Task("Kubernetes@1", new Dictionary<string, string>
        {
            ["connectionType"] = "Kubernetes Service Connection",
            ["kubernetesServiceEndpoint"] = deployment.KubernetesServiceConnectionOrDefault,
            ["namespace"] = deployment.KubernetesNamespaceOrDefault,
            ["command"] = "rollout",
            ["arguments"] = $"undo deployment/{deployment.KubernetesDeploymentNameOrDefault}"
        }, "Roll back Kubernetes deployment")
    };

    public override IEnumerable<ValidationResult> Validate(PipelineDefinition definition)
    {
        if (definition.Artifact.ArtifactType != ArtifactType.DockerImage)
        {
            yield return Finding(ValidationSeverity.Warning,
                "Kubernetes deployments need a container image, but the artifact is not a Docker image.",
                nameof(PipelineDefinition.Artifact),
                "Set the artifact type to DockerImage.");
        }
    }
}
